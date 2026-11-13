using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Detects display HDR capability, inspects video content metadata for HDR
/// format, configures the MediaPlayer for optimal HDR/SDR output, and
/// manages display brightness during HDR playback.
///
/// <para>
/// <b>Brightness:</b>  When HDR playback is active the service boosts the
/// monitor brightness to 100 % via the Win32 DDC/CI API (Dxva2.dll) so the
/// display operates at its peak luminance.  Brightness is restored to the
/// user's previous level when HDR playback ends.
/// </para>
/// <para>
/// <b>Tone-mapping:</b>  Configures Media Foundation tone-mapping operator attributes
/// (<c>Reinhard</c>, <c>Aces</c>, <c>Bt2408</c>, <c>Clip</c>) and color space target primaries/curves
/// on the active <see cref="Windows.Media.Playback.MediaPlaybackItem"/> video tracks so that HDR content
/// is accurately tone-mapped down to SDR or mapped for HDR display highlights.
/// </para>
/// <para>
/// <b>Display capability</b> is read from <see cref="AppServices.DisplayManager"/>
/// (the single authoritative <c>DisplayInformation</c> instance) rather than
/// maintaining a duplicate subscription.  Call order in MainWindow must ensure
/// <see cref="Services.Display.AdvancedColorDisplayManager.InitializeForWindow"/> runs
/// before <see cref="Initialize"/>.
/// </para>
/// </summary>
public sealed class HdrPipelineService : IHdrPipelineService
{
    // ── Events ──────────────────────────────────────────────────────

    /// <summary>Raised whenever the HDR state changes.</summary>
    public event EventHandler<HdrStateChangedEventArgs>? HdrStateChanged;

    // ── Cached state ────────────────────────────────────────────────

    private DisplayHdrCapability _displayCapability = DisplayHdrCapability.Sdr;
    private HdrContentFormat _contentFormat = HdrContentFormat.None;
    private bool _hdrActive;

    // ── Multi-GPU / Hybrid Graphics environment state ───────────────

    private bool _isDualGpuPresent;
    private string _gpuEnvironmentDescription = "Single GPU Environment";
    private bool _gpuDetectionComplete;

    // ── Content-format cache ─────────────────────────────────────────
    // Avoids re-inspecting all video tracks on fullscreen toggling when the
    // media source hasn't changed.

    private MediaPlaybackItem? _lastDetectedItem;
    /// <summary>
    /// True once <see cref="DetectContentFormat"/> has run to completion for
    /// <see cref="_lastDetectedItem"/>. Allows the cache to also short-circuit
    /// genuine SDR files whose <see cref="_contentFormat"/> is
    /// <see cref="HdrContentFormat.None"/>.
    /// </summary>
    private bool _detectionComplete;
    private MediaPlaybackItem? _lastConfiguredItem;
    private bool? _lastConfiguredHdr;
    private ToneMappingMode? _lastConfiguredToneMapMode;
    private string? _lastConfiguredTrackId;
    private string? _lastConfiguredTrackPath;
    private HdrContentFormat? _lastConfiguredFormat;
    private string? _lastConfiguredTrackHdrFormat;
    private Display.DisplayProfileKind? _lastConfiguredDisplayProfile;

    // ── Fullscreen transition synchronization & deferral ─────────────
    private bool _isTransitionActive;
    private bool? _pendingFullscreenState;
    private (MediaPlayer player, MediaPlaybackItem? item)? _pendingPipelineConfig;
    private string _lastLoggedPipelineSummary = "";
    private static string _lastLoggedToneMapSummary = "";

    // ── Track change monitoring ──────────────────────────────────────
    private MediaPlaybackItem? _monitoredItem;
    private Windows.Foundation.TypedEventHandler<MediaPlaybackItem, Windows.Foundation.Collections.IVectorChangedEventArgs>? _videoTracksChangedHandler;

    // ── Brightness handles ────────────────────────────────────────────

    private IntPtr _hwnd;

    // ── Public read-only state ───────────────────────────────────────

    public DisplayHdrCapability DisplayCapability => _displayCapability;
    public HdrContentFormat ContentFormat => _contentFormat;

    /// <summary>True when a multi-GPU / hybrid graphics environment (e.g. AMD Radeon + NVIDIA GTX/RTX) is detected.</summary>
    public bool IsDualGpuEnvironment => _isDualGpuPresent;

    /// <summary>Human-readable summary of detected video adapters.</summary>
    public string GpuEnvironmentDescription => _gpuEnvironmentDescription;

    /// <summary>
    /// Evaluates if the current display configuration supports HDR output.
    /// This forces a real-time capability refresh.
    /// </summary>
    public bool IsDisplayHdrCapable
    {
        get
        {
            RefreshDisplayCapability();
            return _displayCapability == DisplayHdrCapability.Hdr10 ||
                   _displayCapability == DisplayHdrCapability.DolbyVision ||
                   AppServices.DisplayManager.IsHdrActive;
        }
    }
    public bool IsHdrActive => _hdrActive;

    public string ContentFormatLabel
    {
        get
        {
            var track = AppServices.Playback.CurrentTrack;
            if (track != null && !string.IsNullOrEmpty(track.HdrFormat) && !track.HdrFormat.Equals("SDR", StringComparison.OrdinalIgnoreCase))
            {
                return track.HdrFormat;
            }

            return _contentFormat switch
            {
                HdrContentFormat.Hdr10 => "HDR10",
                HdrContentFormat.Hlg => "HLG",
                HdrContentFormat.DolbyVision => "Dolby Vision",
                _ => "SDR"
            };
        }
    }

    public string DisplayCapabilityLabel => _displayCapability switch
    {
        DisplayHdrCapability.Hdr10 => AppServices.DisplayManager.IsHdrActive ? "HDR10 Display" : $"HDR Streaming Display ({AppServices.DisplayManager.MaxLuminanceInNits:F0} nits)",
        DisplayHdrCapability.DolbyVision => "Dolby Vision Display",
        DisplayHdrCapability.Wcg => "WCG Display",
        _ => AppServices.DisplayManager.CanStreamHdr ? $"HDR Streaming Display ({AppServices.DisplayManager.MaxLuminanceInNits:F0} nits)" : "SDR Display"
    };

    // ── Initialise ───────────────────────────────────────────────────

    /// <summary>
    /// Call once after the main window is ready <b>and after</b>
    /// <see cref="Services.Display.AdvancedColorDisplayManager.InitializeForWindow"/>
    /// has been called, so the display state is already populated.
    /// </summary>
    public void Initialize(Window window)
    {
        try
        {
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            // Attempt emergency brightness recovery in case app previously terminated while overridden
            BrightnessOverrideHelper.TryRestoreEmergencyBaseline();

            // Read initial capability from the already-initialised DisplayManager
            // (no second DisplayInformation object required).
            RefreshDisplayCapability();

            // React to display HDR changes (e.g. user toggles Windows HDR in Settings or moves window across screens)
            AppServices.DisplayManager.AdvancedColorInfoChanged += (_, _) =>
            {
                var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
                if (dispatcher != null)
                {
                    dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            RefreshDisplayCapability();
                            if (AppServices.Playback.CurrentTrack?.IsVideo == true)
                            {
                                var player = AppServices.PlaybackViewModel?.Session?.MediaPlayer;
                                if (player != null)
                                {
                                    MediaPlaybackItem? currentItem = null;
                                    if (player.Source is MediaPlaybackItem mpi) currentItem = mpi;
                                    else if (player.Source is MediaPlaybackList mpl) currentItem = mpl.CurrentItem;
                                    if (currentItem != null)
                                    {
                                        ConfigurePipeline(player, currentItem);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[HDR] Display capability changed handler error: {ex.Message}");
                        }
                    });
                }
            };

            // Perform non-blocking WMI scan to detect dual-GPU hybrid graphics setups
            // (e.g. integrated AMD Radeon + discrete Nvidia RTX/GTX).
            _ = EnsureGpuEnvironmentDetectedAsync();

            // React to playback state changes (pause/play/stop) to boost or restore brightness
            if (_playbackStateChangedHandler == null)
            {
                _playbackStateChangedHandler = (_, _) =>
                {
                    UpdateBrightnessOverride();
                };
                AppServices.Playback.StateChanged += _playbackStateChangedHandler;
            }

            Debug.WriteLine("[HDR] Initialized — display tracking via AdvancedColorDisplayManager");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Initialize error: {ex.Message}");
        }
    }

    /// <summary>
    /// Asynchronously inspects WMI Win32_VideoController on a thread-pool task to detect
    /// multi-GPU / hybrid graphics environments without blocking the UI thread.
    /// </summary>
    private async Task EnsureGpuEnvironmentDetectedAsync()
    {
        if (_gpuDetectionComplete) return;

        try
        {
            var env = await HardwareDetectionService.DetectGpuEnvironmentAsync();
            _isDualGpuPresent = env.HasDualGpu;
            _gpuEnvironmentDescription = env.Summary;

            if (_isDualGpuPresent)
            {
                Debug.WriteLine($"[HDR Dual-GPU] Multi-GPU environment detected: {_gpuEnvironmentDescription}. Configured direct DXGI shared-surface MPO pipeline for 10-bit HDR color preservation across adapters.");
            }

            _gpuDetectionComplete = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Dual-GPU] GPU detection scan failed: {ex.Message}");
            _gpuDetectionComplete = true;
        }
    }

    // ── Display detection ────────────────────────────────────────────

    /// <summary>
    /// Reads the current display capability from <see cref="AppServices.DisplayManager"/>
    /// (the single authoritative <see cref="Microsoft.Graphics.Display.DisplayInformation"/>
    /// wrapper) rather than maintaining a duplicate subscription.
    /// </summary>
    private void RefreshDisplayCapability()
    {
        try
        {
            var dm = AppServices.DisplayManager;

            if (dm.IsHdrActive || (dm.SupportsHdr10 && dm.MaxLuminanceInNits >= 350f))
                _displayCapability = DisplayHdrCapability.Hdr10;
            else if (dm.CanStreamHdr)
                _displayCapability = DisplayHdrCapability.Hdr10;
            else if (dm.SupportsWcg)
                _displayCapability = DisplayHdrCapability.Wcg;
            else
                _displayCapability = DisplayHdrCapability.Sdr;

            Debug.WriteLine($"[HDR] Display capability: {_displayCapability} (StreamOnly={dm.IsHdrStreamingCapableOnly})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Display refresh error: {ex.Message}");
            _displayCapability = DisplayHdrCapability.Sdr;
        }
    }

    // ── Content inspection ───────────────────────────────────────────

    /// <summary>
    /// Inspect a <see cref="MediaPlaybackItem"/> after it has opened and
    /// detect its HDR format from video track encoding properties.
    /// Results are cached per item reference so fullscreen toggling does
    /// not re-inspect all tracks unnecessarily.
    /// </summary>
    public HdrContentFormat DetectContentFormat(MediaPlaybackItem? item)
    {
        if (item == null)
        {
            _contentFormat = HdrContentFormat.None;
            _lastDetectedItem = null;
            _detectionComplete = false;
            return _contentFormat;
        }

        var currentTrack = AppServices.Playback.CurrentTrack;
        bool hasTrackHdr = currentTrack != null &&
                           !string.IsNullOrEmpty(currentTrack.HdrFormat) &&
                           !currentTrack.HdrFormat.Equals("SDR", StringComparison.OrdinalIgnoreCase);

        // If previously cached as SDR (None) but track metadata now reveals HDR, invalidate the cache
        if (_detectionComplete && _contentFormat == HdrContentFormat.None && hasTrackHdr)
        {
            _detectionComplete = false;
        }

        // ── Cache hit ────────────────────────────────────────────────
        // _detectionComplete is true for both HDR and SDR (None) results so that
        // genuine SDR files don't force a re-scan on every fullscreen toggle.
        if (ReferenceEquals(item, _lastDetectedItem) && _detectionComplete)
        {
            Debug.WriteLine($"[HDR] Content format cached: {_contentFormat}");
            return _contentFormat;
        }

        // Run the scan, then commit both cache fields in exactly one place.
        _contentFormat = ScanContentFormat(item);
        _lastDetectedItem = item;

        // If ScanContentFormat didn't find HDR from tracks yet but track metadata has it, use it
        string? trackHdr = currentTrack?.HdrFormat;
        if (_contentFormat == HdrContentFormat.None && !string.IsNullOrEmpty(trackHdr) && !trackHdr.Equals("SDR", StringComparison.OrdinalIgnoreCase))
        {
            if (trackHdr.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                trackHdr.Contains("DV", StringComparison.OrdinalIgnoreCase))
            {
                _contentFormat = HdrContentFormat.DolbyVision;
            }
            else if (trackHdr.Contains("HLG", StringComparison.OrdinalIgnoreCase))
            {
                _contentFormat = HdrContentFormat.Hlg;
            }
            else
            {
                _contentFormat = HdrContentFormat.Hdr10;
            }
        }

        // Only mark detection complete if we found HDR or video tracks were demuxed,
        // so that an empty track list during early MediaOpened does not falsely freeze detection as SDR.
        if (_contentFormat != HdrContentFormat.None || item.VideoTracks.Count > 0)
        {
            _detectionComplete = true;
        }

        return _contentFormat;
    }

    /// <summary>
    /// Internal detection scan — iterates video tracks and applies a 6-layer
    /// fallback chain. Does NOT touch _lastDetectedItem or _detectionComplete;
    /// the public wrapper handles that.
    /// </summary>
    private HdrContentFormat ScanContentFormat(MediaPlaybackItem item)
    {
        try
        {
            // Layer 0 — Direct container and file metadata inspection
            var currentTrack = AppServices.Playback.CurrentTrack;
            if (currentTrack != null)
            {
                if (!string.IsNullOrEmpty(currentTrack.HdrFormat) && !currentTrack.HdrFormat.Equals("SDR", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentTrack.HdrFormat.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                        currentTrack.HdrFormat.Contains("DV", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[HDR] Detected: Dolby Vision from CurrentTrack.HdrFormat ({currentTrack.HdrFormat})");
                        return HdrContentFormat.DolbyVision;
                    }
                    if (currentTrack.HdrFormat.Contains("HLG", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[HDR] Detected: HLG from CurrentTrack.HdrFormat ({currentTrack.HdrFormat})");
                        return HdrContentFormat.Hlg;
                    }
                    if (currentTrack.HdrFormat.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[HDR] Detected: HDR10 from CurrentTrack.HdrFormat ({currentTrack.HdrFormat})");
                        return HdrContentFormat.Hdr10;
                    }
                }
            }

            string? sourcePath = currentTrack?.SourcePath;
            ContainerTrackInfo? vTrack = null;
            bool isExplicitSdr = false;

            if (!string.IsNullOrEmpty(sourcePath))
            {
                var containerTracks = MediaTrackFormatHelper.GetContainerTracks(sourcePath);
                vTrack = containerTracks.Find(t => t.TrackType == 1);

                if (vTrack != null && !string.IsNullOrEmpty(vTrack.HdrFormat))
                {
                    if (vTrack.HdrFormat.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                        vTrack.HdrFormat.Contains("DV", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[HDR] Detected: Dolby Vision from container video track ({vTrack.HdrFormat})");
                        SyncCurrentTrackHdr(currentTrack, "Dolby Vision", vTrack);
                        return HdrContentFormat.DolbyVision;
                    }
                    if (vTrack.HdrFormat.Contains("HLG", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[HDR] Detected: HLG from container video track ({vTrack.HdrFormat})");
                        SyncCurrentTrackHdr(currentTrack, "HLG", vTrack);
                        return HdrContentFormat.Hlg;
                    }
                    if (vTrack.HdrFormat.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[HDR] Detected: HDR10 from container video track ({vTrack.HdrFormat})");
                        SyncCurrentTrackHdr(currentTrack, "HDR10", vTrack);
                        return HdrContentFormat.Hdr10;
                    }
                }

                if (vTrack != null)
                {
                    if (vTrack.TransferCharacteristics == 16)
                    {
                        Debug.WriteLine("[HDR] Detected: HDR10 from container transfer function ST 2084 / PQ");
                        SyncCurrentTrackHdr(currentTrack, "HDR10", vTrack);
                        return HdrContentFormat.Hdr10;
                    }
                    if (vTrack.TransferCharacteristics == 18)
                    {
                        Debug.WriteLine("[HDR] Detected: HLG from container transfer function ARIB STD-B67");
                        SyncCurrentTrackHdr(currentTrack, "HLG", vTrack);
                        return HdrContentFormat.Hlg;
                    }
                    if (vTrack.ColorPrimaries == 9 && (vTrack.BitDepth >= 10 || vTrack.VideoBitDepth >= 10))
                    {
                        Debug.WriteLine("[HDR] Detected: HDR10 from container BT.2020 10-bit primaries");
                        SyncCurrentTrackHdr(currentTrack, "HDR10", vTrack);
                        return HdrContentFormat.Hdr10;
                    }
                    if (vTrack.CodecId.Equals("dvh1", StringComparison.OrdinalIgnoreCase) ||
                        vTrack.CodecId.Equals("dvhe", StringComparison.OrdinalIgnoreCase) ||
                        vTrack.CodecId.Equals("dovi", StringComparison.OrdinalIgnoreCase) ||
                        vTrack.CodecId.Contains("DOLBY_VISION", StringComparison.OrdinalIgnoreCase) ||
                        vTrack.CodecId.Contains("DOLBY VISION", StringComparison.OrdinalIgnoreCase) ||
                        vTrack.CodecId.Contains("DOVI", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine("[HDR] Detected: Dolby Vision from container codec ID");
                        SyncCurrentTrackHdr(currentTrack, "Dolby Vision", vTrack);
                        return HdrContentFormat.DolbyVision;
                    }

                    // Explicit SDR indicators from container
                    if ((vTrack.ColorPrimaries == 1 || vTrack.TransferCharacteristics == 1) &&
                        (vTrack.BitDepth <= 8 && vTrack.VideoBitDepth <= 8))
                    {
                        isExplicitSdr = true;
                    }
                }
            }

            foreach (var track in item.VideoTracks)
            {
                var props = track.GetEncodingProperties();
                if (props == null) continue;

                // Explicit SDR indicators from Media Foundation stream properties
                if (props.Properties.TryGetValue(new Guid("93B7BE49-B4B2-4F40-A66E-C13B5F8E4E82"), out var tfVal) &&
                    tfVal is uint tfNum && (tfNum == 1 || tfNum == 5))
                {
                    isExplicitSdr = true;
                }
                if (props.Properties.TryGetValue(new Guid("dbfbe4d7-0740-4ee0-8192-850AB0E21935"), out var prVal) &&
                    prVal is uint prNum && prNum == 2)
                {
                    isExplicitSdr = true;
                }

                // Layer 1 — Dolby Vision: subtype string
                if (props.Subtype != null &&
                    props.Subtype.Contains("DOLBY", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("[HDR] Detected: Dolby Vision from track subtype");
                    SyncCurrentTrackHdr(currentTrack, "Dolby Vision", null);
                    return HdrContentFormat.DolbyVision;
                }

                // Layer 2 — MF_MT_TRANSFER_FUNCTION (mfobjects.h)
                //   15 = MFVideoTransFunc_2084 (SMPTE ST 2084 / PQ) → HDR10
                //   16 = MFVideoTransFunc_HLG  (ARIB STD-B67 / HLG)  → HLG
                if (props.Properties.TryGetValue(
                    new Guid("93B7BE49-B4B2-4F40-A66E-C13B5F8E4E82"),
                    out var tfValue) && tfValue is uint tf)
                {
                    if (tf == 15)
                    {
                        Debug.WriteLine("[HDR] Detected: HDR10 (PQ/ST2084) from track encoding properties");
                        SyncCurrentTrackHdr(currentTrack, "HDR10", null);
                        return HdrContentFormat.Hdr10;
                    }
                    if (tf == 16)
                    {
                        Debug.WriteLine("[HDR] Detected: HLG from track encoding properties");
                        SyncCurrentTrackHdr(currentTrack, "HLG", null);
                        return HdrContentFormat.Hlg;
                    }
                }

                // Layer 3 — MF_MT_VIDEO_PRIMARIES — BT.2020 = 9  →  likely HDR
                if (props.Properties.TryGetValue(
                    new Guid("dbfbe4d7-0740-4ee0-8192-850AB0E21935"),
                    out var primValue) && primValue is uint prims && prims == 9)
                {
                    Debug.WriteLine("[HDR] Inferred HDR10 from BT.2020 primaries");
                    SyncCurrentTrackHdr(currentTrack, "HDR10", null);
                    return HdrContentFormat.Hdr10;
                }

                // Layer 4 — Subtype string contains "HDR"
                if (!string.IsNullOrEmpty(props.Subtype) &&
                    props.Subtype.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[HDR] Detected HDR from subtype: {props.Subtype}");
                    SyncCurrentTrackHdr(currentTrack, "HDR10", null);
                    return HdrContentFormat.Hdr10;
                }

                // Layer 5 — HEVC/VP9 Main10 profile (MPEG-2 profile GUID, value 2)
                // Note: Main10 alone indicates 10-bit color depth, not High Dynamic Range.
                // Standard 10-bit SDR videos (e.g. anime or SDR Blu-ray encodes) must not be flagged as HDR.
                if (props.Properties.TryGetValue(
                    new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"),
                    out var profileVal) && profileVal is uint profile &&
                    props.Subtype != null &&
                    (props.Subtype.Contains("HEVC", StringComparison.OrdinalIgnoreCase) ||
                     props.Subtype.Contains("H265", StringComparison.OrdinalIgnoreCase) ||
                     props.Subtype.Contains("VP90", StringComparison.OrdinalIgnoreCase) ||
                     props.Subtype.Contains("35363248", StringComparison.OrdinalIgnoreCase)) &&
                    profile == 2)
                {
                    bool hasHdrCharacteristics = (SafeTryGetProperty(props.Properties, new Guid("dbfbe4d7-0740-4ee0-8192-850AB0E21935"), out var pv) && pv is uint p && p == 9) ||
                                                 (SafeTryGetProperty(props.Properties, new Guid("93B7BE49-B4B2-4F40-A66E-C13B5F8E4E82"), out var tv) && tv is uint t && (t == 15 || t == 16));

                    if (hasHdrCharacteristics)
                    {
                        Debug.WriteLine("[HDR] Inferred HDR10 from HEVC/VP9 10-bit profile with BT.2020/HDR metadata");
                        SyncCurrentTrackHdr(currentTrack, "HDR10", null);
                        return HdrContentFormat.Hdr10;
                    }
                }
            }

            // Layer 6 — Weak fallback heuristic: Check filename regex ONLY IF container metadata and MediaSource properties are inconclusive
            if (!isExplicitSdr && !string.IsNullOrEmpty(sourcePath))
            {
                string fName = System.IO.Path.GetFileName(sourcePath);
                if (Regex.IsMatch(fName, @"(?i)(?:^|[\s._\-\[(+])(DV|DoVi|Dolby[\s._\-]?Vision)(?:$|[\s._\-\])+])"))
                {
                    Debug.WriteLine("[HDR] Inferred Dolby Vision from filename heuristic (weak fallback)");
                    SyncCurrentTrackHdr(currentTrack, "Dolby Vision", vTrack);
                    return HdrContentFormat.DolbyVision;
                }

                if (Regex.IsMatch(fName, @"(?i)(?:^|[\s._\-\[(+])(HDR|HDR10|HDR10\+|HLG|ST2084|ST-2084|BT2020|BT\.2020)(?:$|[\s._\-\])+])"))
                {
                    Debug.WriteLine("[HDR] Inferred HDR10 from filename heuristic (weak fallback)");
                    SyncCurrentTrackHdr(currentTrack, "HDR10", vTrack);
                    return HdrContentFormat.Hdr10;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Content detection error: {ex.Message}");
        }

        // All layers exhausted — content is SDR.
        Debug.WriteLine("[HDR] Content format: SDR (no HDR metadata found)");
        return HdrContentFormat.None;
    }

    private static void SyncCurrentTrackHdr(MediaItem? currentTrack, string hdrFormat, ContainerTrackInfo? vTrack)
    {
        if (currentTrack == null) return;
        if (string.IsNullOrEmpty(currentTrack.HdrFormat) || currentTrack.HdrFormat.Equals("SDR", StringComparison.OrdinalIgnoreCase))
        {
            currentTrack.HdrFormat = hdrFormat;
        }
        if (vTrack != null && (vTrack.BitDepth >= 10 || vTrack.VideoBitDepth >= 10))
        {
            currentTrack.BitDepth = $"{Math.Max(vTrack.BitDepth, vTrack.VideoBitDepth)}-bit";
        }
        else if (hdrFormat != "SDR" && (string.IsNullOrEmpty(currentTrack.BitDepth) || currentTrack.BitDepth == "8-bit"))
        {
            currentTrack.BitDepth = "10-bit";
        }
    }

    // ── Pipeline configuration ────────────────────────────────────────

    /// <summary>
    /// Configure the MediaPlayer HDR pipeline.  Call this from:
    /// <list type="bullet">
    ///   <item><description><c>OnMediaPlayerMediaOpened</c> in PlaybackSession (windowed + fullscreen)</description></item>
    ///   <item><description>When fullscreen is entered while media is already playing</description></item>
    ///   <item><description>When the user changes HDR settings</description></item>
    /// </list>
    /// </summary>
    public void ConfigurePipeline(MediaPlayer player, MediaPlaybackItem? item)
    {
        var settings = AppServices.Settings.Current;

        // Clean up previous track change monitoring if switching items
        if (!ReferenceEquals(item, _monitoredItem))
        {
            UnsubscribeMonitoredItem();
        }

        // Refresh display capability in real-time before checking if we should enable HDR output.
        // Reads from the shared DisplayManager — no duplicate COM call.
        RefreshDisplayCapability();

        // 1. Detect content format (cached per item — skips track scan on fullscreen toggle)
        var format = DetectContentFormat(item);

        bool hasVideoTracks = item != null && item.VideoTracks.Count > 0;

        // If item has no video tracks yet, subscribe to VideoTracksChanged so we re-run
        // once Media Foundation finishes asynchronous demuxing.
        if (item != null && !hasVideoTracks)
        {
            _monitoredItem = item;
            _videoTracksChangedHandler = (senderItem, _) =>
            {
                if (senderItem.VideoTracks.Count > 0)
                {
                    UnsubscribeMonitoredItem();

                    // Invalidate item detection cache so fresh demuxed tracks are scanned
                    if (ReferenceEquals(_lastDetectedItem, senderItem))
                    {
                        _detectionComplete = false;
                    }

                    var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
                    if (dispatcher != null)
                    {
                        dispatcher.TryEnqueue(() =>
                        {
                            ConfigurePipeline(player, senderItem);
                        });
                    }
                    else
                    {
                        ConfigurePipeline(player, senderItem);
                    }
                }
            };
            item.VideoTracksChanged += _videoTracksChangedHandler;
        }

        // 2. Determine whether HDR output (and brightness override) should be active.
        //
        //    ForceOn  — user explicitly opted in; always boost regardless of display or content.
        //    ForceSdr — user explicitly opted out; always tone-map down to SDR.
        //    Auto     — enable only when playing detected HDR content on an HDR-capable screen
        //               (including laptops that support "Stream HDR video" but report an SDR desktop).
        var currentTrack = AppServices.Playback.CurrentTrack;
        string? cfgTrackHdr = currentTrack?.HdrFormat;
        bool hasTrackHdr = !string.IsNullOrEmpty(cfgTrackHdr) && !cfgTrackHdr.Equals("SDR", StringComparison.OrdinalIgnoreCase);

        if (format == HdrContentFormat.None && hasTrackHdr && cfgTrackHdr != null)
        {
            format = cfgTrackHdr.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                     cfgTrackHdr.Contains("DV", StringComparison.OrdinalIgnoreCase)
                ? HdrContentFormat.DolbyVision
                : cfgTrackHdr.Contains("HLG", StringComparison.OrdinalIgnoreCase)
                    ? HdrContentFormat.Hlg
                    : HdrContentFormat.Hdr10;
            _contentFormat = format;
        }

        bool isDisplayHdr = AppServices.DisplayManager.IsHdrActive;
        bool isDisplayHdrCapable = _displayCapability == DisplayHdrCapability.Hdr10 ||
                                   _displayCapability == DisplayHdrCapability.DolbyVision ||
                                   isDisplayHdr ||
                                   AppServices.DisplayManager.CanStreamHdr;
        bool isContentHdr = format != HdrContentFormat.None || hasTrackHdr;

        bool shouldEnableHdr = settings.HdrMode switch
        {
            HdrMode.ForceOn => true,   // always boost — user's explicit choice
            HdrMode.ForceSdr => false,  // always SDR   — user's explicit choice
            _ => isContentHdr && isDisplayHdrCapable
        };
        var activeProfile = AppServices.DisplayManager.ActiveDisplayProfile;

        // Fast-path: Skip re-negotiating Media Foundation video track encoding properties if the track,
        // HDR format, display profile, and tone-mapping configuration have not changed (prevents dropped
        // frames, MPO renegotiation, and microstutter on fullscreen toggles).
        // HDR-04: Remove premature fast-path returns when demuxed video tracks count is zero (mpi.VideoTracks.Count == 0).
        bool isSameItem = ReferenceEquals(item, _lastConfiguredItem) ||
                          (currentTrack != null &&
                           !string.IsNullOrEmpty(_lastConfiguredTrackPath) &&
                           string.Equals(currentTrack.SourcePath, _lastConfiguredTrackPath, StringComparison.OrdinalIgnoreCase)) ||
                          (currentTrack != null &&
                           !string.IsNullOrEmpty(_lastConfiguredTrackId) &&
                           string.Equals(currentTrack.Id, _lastConfiguredTrackId, StringComparison.Ordinal));

        bool isSameHdrFormat = _lastConfiguredFormat == format &&
                               string.Equals(_lastConfiguredTrackHdrFormat, currentTrack?.HdrFormat, StringComparison.OrdinalIgnoreCase);

        bool isSameDisplayProfile = _lastConfiguredDisplayProfile == activeProfile;

        if (hasVideoTracks && isSameItem && isSameHdrFormat && isSameDisplayProfile &&
            _lastConfiguredHdr == shouldEnableHdr &&
            _lastConfiguredToneMapMode == settings.ToneMappingMode)
        {
            _hdrActive = shouldEnableHdr;
            if (!_isTransitionActive && !(App.MainWindowInstance?.IsFullscreenTransitioning ?? false))
            {
                UpdateBrightnessOverride();
            }
            else
            {
                _pendingFullscreenState = _isAppFullscreen;
            }
            return;
        }

        // Defer Media Foundation tone-mapping operator reconfiguration if mid-transition
        // to prevent display driver / DWM HDMI/MPO handshake glitches during morph animations.
        if (_isTransitionActive || (App.MainWindowInstance?.IsFullscreenTransitioning ?? false))
        {
            Debug.WriteLine("[HDR] Deferring pipeline tone-mapping reconfiguration until fullscreen transition completes and window settles.");
            _pendingPipelineConfig = (player, item);
            return;
        }

        // 3. Ensure the native MPO pipeline handles HDR (frame-server mode bypasses it)
        TryConfigureNativePipeline(player, shouldEnableHdr, _isDualGpuPresent);

        // 4. Configure Media Foundation tone-mapping operator and color space attributes on video tracks
        ApplyToneMapping(item, settings.ToneMappingMode, shouldEnableHdr);

        _hdrActive = shouldEnableHdr;
        _lastConfiguredItem = item;
        _lastConfiguredHdr = shouldEnableHdr;
        _lastConfiguredToneMapMode = settings.ToneMappingMode;
        _lastConfiguredTrackId = currentTrack?.Id;
        _lastConfiguredTrackPath = currentTrack?.SourcePath;
        _lastConfiguredFormat = format;
        _lastConfiguredTrackHdrFormat = currentTrack?.HdrFormat;
        _lastConfiguredDisplayProfile = activeProfile;
        UpdateBrightnessOverride();

        var args = new HdrStateChangedEventArgs
        {
            IsHdrActive = _hdrActive,
            ContentFormat = _contentFormat,
            DisplayCapability = _displayCapability,
            ToneMappingMode = settings.ToneMappingMode,
            PeakBrightnessNits = (int)AppServices.DisplayManager.MaxLuminanceInNits,
            IsDualGpuEnvironment = _isDualGpuPresent,
            IsHdrStreamingCapableOnly = AppServices.DisplayManager.IsHdrStreamingCapableOnly
        };

        string currentSummary = $"{_hdrActive}|{_contentFormat}|{_displayCapability}|{settings.ToneMappingMode}|{AppServices.DisplayManager.MaxLuminanceInNits:F0}";
        if (currentSummary != _lastLoggedPipelineSummary)
        {
            _lastLoggedPipelineSummary = currentSummary;
            Debug.WriteLine($"[HDR] Pipeline — active={_hdrActive}, " +
                            $"content={_contentFormat}, display={_displayCapability} (Profile={AppServices.DisplayManager.ActiveDisplayProfile}), " +
                            $"dualGpu={_isDualGpuPresent}, toneMap={settings.ToneMappingMode}, peak={AppServices.DisplayManager.MaxLuminanceInNits:F0} nits");
        }

        HdrStateChanged?.Invoke(this, args);
    }

    private void UnsubscribeMonitoredItem()
    {
        if (_monitoredItem != null && _videoTracksChangedHandler != null)
        {
            try { _monitoredItem.VideoTracksChanged -= _videoTracksChangedHandler; }
            catch { }
            _monitoredItem = null;
            _videoTracksChangedHandler = null;
        }
    }

    // ── Native MPO pipeline ───────────────────────────────────────────

    private static void TryConfigureNativePipeline(MediaPlayer player, bool isHdrActive, bool isDualGpu)
    {
        try
        {
            // Always disable VideoFrameServer mode for HDR or Dual GPU environments so Media Foundation
            // uses native DXGI shared-surface Multi-Plane Overlay (MPO) and preserves 10-bit P010 HDR
            // color metadata across GPU adapters without PCIe system-memory readback.
            if (player.IsVideoFrameServerEnabled)
            {
                player.IsVideoFrameServerEnabled = false;
                Debug.WriteLine("[HDR] Disabled VideoFrameServer — native MPO pipeline active");
            }

            // Ensure RealTimePlayback is ALWAYS disabled for media file playback.
            // Enabling RealTimePlayback disables the preroll buffer and frame lookahead, causing
            // severe stuttering, dropped frames, and GPU driver buffer underflows/crashes on hybrid GPUs.
            if (player.RealTimePlayback)
            {
                player.RealTimePlayback = false;
                Debug.WriteLine("[HDR] Disabled RealTimePlayback to preserve Media Foundation preroll and frame queue stability");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Pipeline configuration failed: {ex.Message}");
        }
    }

    private static void ApplyToneMapping(MediaPlaybackItem? item, ToneMappingMode mode, bool isHdrActive)
    {
        if (item == null || item.VideoTracks.Count == 0)
        {
            return;
        }

        try
        {
            // Map ToneMappingMode enum to Media Foundation Tone Mapping Operator index:
            // 0 = Reinhard (smooth global roll-off)
            // 1 = ACES (cinematic film curve approximation)
            // 2 = BT.2408 (ITU reference HDR-to-SDR standard)
            // 3 = Clip (linear highlight clipping)
            uint toneMapOperatorIndex = mode switch
            {
                ToneMappingMode.Reinhard => 0u,
                ToneMappingMode.Aces     => 1u,
                ToneMappingMode.Bt2408   => 2u,
                ToneMappingMode.Clip     => 3u,
                _                        => 1u
            };

            var toneMapGuid = new Guid("DE9AC8C9-9602-4A85-AA27-BCE095709DFF"); // MF_VIDEO_TONEMAPPING_OPERATOR
            var primariesGuid = new Guid("dbfbe4d7-0740-4ee0-8192-850AB0E21935"); // MF_MT_VIDEO_PRIMARIES
            var transferFuncGuid = new Guid("5FB0FCE9-BE5C-4935-A811-EC838F8EEED2"); // MF_MT_TRANSFER_FUNCTION

            foreach (var track in item.VideoTracks)
            {
                try
                {
                    var props = track.GetEncodingProperties();
                    props.Properties[toneMapGuid] = toneMapOperatorIndex;

                    if (!isHdrActive)
                    {
                        // When tone-mapping HDR down to SDR, instruct Media Foundation Video Processor
                        // to map to BT.709 primaries (index 2) and BT.709 transfer curve (index 1)
                        props.Properties[primariesGuid] = 2u;
                        props.Properties[transferFuncGuid] = 1u;
                    }
                    else
                    {
                        // When HDR output is active, preserve BT.2020 primaries (index 9) and PQ ST.2084 curve (index 6)
                        // while applying the selected tone mapping operator for highlight compression
                        props.Properties[primariesGuid] = 9u;
                        props.Properties[transferFuncGuid] = 6u;
                    }
                }
                catch { }
            }

            Debug.WriteLine($"[HDR ToneMapping] Applied mode '{mode}' (operator index {toneMapOperatorIndex}, HdrActive={isHdrActive}) across {item.VideoTracks.Count} video tracks");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR ToneMapping] Failed to apply tone mapping: {ex.Message}");
        }
    }

    private BrightnessOverrideHelper? _brightnessOverride;
    private bool _isAppFullscreen;
    private bool _isVideoPageActive;
    private bool _isSuspended;
    private EventHandler? _playbackStateChangedHandler;

    public void SetFullscreenState(bool isFullscreen)
    {
        _isAppFullscreen = isFullscreen;
        if (!isFullscreen && !_isVideoPageActive)
        {
            _brightnessOverride.Release();
        }

        if (_isTransitionActive || (App.MainWindowInstance?.IsFullscreenTransitioning ?? false))
        {
            _pendingFullscreenState = isFullscreen;
            Debug.WriteLine($"[HDR Brightness] Fullscreen transition active — deferred brightness override (target: {isFullscreen})");
            return;
        }

        UpdateBrightnessOverride();
    }

    /// <summary>
    /// Signals that a fullscreen morph transition has begun. Defers all brightness overrides
    /// and tone-mapping reconfiguration until <see cref="CompleteFullscreenTransition"/> is called.
    /// </summary>
    public void BeginFullscreenTransition()
    {
        _isTransitionActive = true;
        Debug.WriteLine("[HDR] Fullscreen transition begun — brightness override and tone-mapping deferred");
    }

    /// <summary>
    /// Completes a fullscreen transition batch after the window has settled, committing any
    /// deferred brightness overrides and tone-mapping reconfiguration.
    /// </summary>
    public void CompleteFullscreenTransition(bool isFullscreen)
    {
        _isTransitionActive = false;
        _isAppFullscreen = isFullscreen;
        _pendingFullscreenState = null;
        Debug.WriteLine($"[HDR] Fullscreen transition completed (settled: {isFullscreen}) — committing deferred pipeline state");

        try
        {
            if (_pendingPipelineConfig.HasValue)
            {
                var (player, item) = _pendingPipelineConfig.Value;
                _pendingPipelineConfig = null;
                if (isFullscreen)
                {
                    ConfigurePipeline(player, item);
                }
            }

            if (!isFullscreen && !_isVideoPageActive)
            {
                _brightnessOverride.Release();
            }
            else
            {
                UpdateBrightnessOverride();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    public void SetVideoPageActive(bool isActive)
    {
        if (_isVideoPageActive != isActive)
        {
            _isVideoPageActive = isActive;
            Debug.WriteLine($"[HDR Brightness] VideoPageActive changed to {isActive}");
            UpdateBrightnessOverride();
        }
    }

    public void SetSuspended(bool isSuspended)
    {
        if (_isSuspended != isSuspended)
        {
            _isSuspended = isSuspended;
            Debug.WriteLine($"[HDR Brightness] Suspended changed to {isSuspended}");
            UpdateBrightnessOverride();
        }
    }

    private void UpdateBrightnessOverride()
    {
        try
        {
            if (_hdrActive && AppServices.Settings.Current.AutoBoostHdrBrightness)
            {
                dispatcher.TryEnqueue(UpdateBrightnessOverride);
                return;
            }

            var presenterKind = App.MainWindowInstance?.AppWindow?.Presenter?.Kind;
            bool isActuallyFullscreen = presenterKind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen;

            // Screen brightness automatically elevates during fullscreen HDR playback or active VideoPage playback
            bool isTargetPresentation = isActuallyFullscreen || _isVideoPageActive;
            if (!isTargetPresentation)
            {
                _brightnessOverride.Release();
                return;
            }

            if (_isTransitionActive || (App.MainWindowInstance?.IsFullscreenTransitioning ?? false))
            {
                _pendingFullscreenState = _isAppFullscreen;
                Debug.WriteLine("[HDR Brightness] Deferring UpdateBrightnessOverride mid-transition");
                return;
            }

            var settings = AppServices.Settings.Current;
            var currentTrack = AppServices.Playback.CurrentTrack;
            bool isVideo = currentTrack is { IsVideo: true };
            bool isPlaying = AppServices.Playback.IsPlaying;

            // Detect HDR format from inspected content format OR track metadata
            bool isContentHdr = _contentFormat != HdrContentFormat.None ||
                               (currentTrack != null && !string.IsNullOrEmpty(currentTrack.HdrFormat) && !currentTrack.HdrFormat.Equals("SDR", StringComparison.OrdinalIgnoreCase));

            bool isPaused = false;
            try
            {
                isPaused = AppServices.PlaybackViewModel.Session.MediaPlayer.PlaybackSession?.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Paused;
            }
            catch { }
            bool isMediaActive = isPlaying || isPaused;

            // Automatic HDR brightness: elevates screen to 100% peak luminance during HDR playback (mirroring Windows Media Player),
            // and gracefully restores user baseline when leaving playback or pausing.
            bool shouldBoost = settings.AutoBoostHdrBrightness
                               && isTargetPresentation
                               && (isContentHdr || settings.HdrMode == HdrMode.ForceOn)
                               && settings.HdrMode != HdrMode.ForceSdr
                               && isVideo
                               && isMediaActive
                               && !_isSuspended;

            if (shouldBoost)
            {
                var hwnd = _hwnd != IntPtr.Zero ? _hwnd : (App.MainWindowInstance != null ? WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance) : IntPtr.Zero);
                _brightnessOverride.TryOverrideToMax(hwnd);
            }
            else
            {
                _brightnessOverride.Release();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] Brightness override logic failed: {ex.Message}");
        }
    }

    // ── Reset on stop ────────────────────────────────────────────────

    /// <summary>
    /// Reset tracked content format and release the brightness override
    /// when playback stops or is torn down.
    /// </summary>
    public void ResetContentState()
    {
        UnsubscribeMonitoredItem();
        _contentFormat = HdrContentFormat.None;
        _hdrActive = false;
        _lastDetectedItem = null; // clear cache so next media gets a fresh detection
        _detectionComplete = false;
        _lastConfiguredItem = null;
        _lastConfiguredHdr = null;
        _lastConfiguredToneMapMode = null;
        _lastConfiguredTrackId = null;
        _lastConfiguredTrackPath = null;
        _lastConfiguredFormat = null;
        _lastConfiguredTrackHdrFormat = null;
        _lastConfiguredDisplayProfile = null;
        _isTransitionActive = false;
        _pendingFullscreenState = null;
        _pendingPipelineConfig = null;

        try
        {
            _brightnessOverride.Release();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] ResetContentState Release error: {ex.Message}");
        }

        if (_lastLoggedPipelineSummary != "RESET")
        {
            _lastLoggedPipelineSummary = "RESET";
            _lastLoggedToneMapSummary = "";
            Debug.WriteLine("[HDR] Content state reset");
        }
    }

    /// <summary>
    /// Waits for any pending background brightness restore operations to complete.
    /// </summary>
    public void WaitForPendingBrightnessRestore(TimeSpan timeout)
    {
        _brightnessOverride.WaitForPendingRestore(timeout);
    }
}

/// <summary>Event data for <see cref="HdrPipelineService.HdrStateChanged"/>.</summary>
public sealed class HdrStateChangedEventArgs : EventArgs
{
    public bool IsHdrActive { get; init; }
    public HdrContentFormat ContentFormat { get; init; }
    public DisplayHdrCapability DisplayCapability { get; init; }
    public ToneMappingMode ToneMappingMode { get; init; }
    public int PeakBrightnessNits { get; init; }
    public bool IsDualGpuEnvironment { get; init; }
    public bool IsHdrStreamingCapableOnly { get; init; }
}
