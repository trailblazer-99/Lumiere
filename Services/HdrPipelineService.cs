using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
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
public sealed class HdrPipelineService
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
                   AppServices.DisplayManager.CanStreamHdr;
        }
    }
    public bool IsHdrActive => _hdrActive;

    public string ContentFormatLabel => _contentFormat switch
    {
        HdrContentFormat.Hdr10       => "HDR10",
        HdrContentFormat.Hlg         => "HLG",
        HdrContentFormat.DolbyVision => "Dolby Vision",
        _                            => "SDR"
    };

    public string DisplayCapabilityLabel => _displayCapability switch
    {
        DisplayHdrCapability.Hdr10       => "HDR10 Display",
        DisplayHdrCapability.DolbyVision => "Dolby Vision Display",
        DisplayHdrCapability.Wcg         => "WCG Display",
        _                                => AppServices.DisplayManager.CanStreamHdr ? "HDR Streaming Capable Display" : "SDR Display"
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

            // Read initial capability from the already-initialised DisplayManager
            // (no second DisplayInformation object required).
            RefreshDisplayCapability();

            // React to display HDR changes (e.g. user toggles Windows HDR in Settings or moves window across screens)
            AppServices.DisplayManager.AdvancedColorInfoChanged += (_, _) =>
            {
                RefreshDisplayCapability();
                if (AppServices.Playback.CurrentTrack?.IsVideo == true)
                {
                    try
                    {
                        var player = AppServices.PlaybackViewModel.Session.MediaPlayer;
                        MediaPlaybackItem? currentItem = null;
                        if (player.Source is MediaPlaybackItem mpi) currentItem = mpi;
                        else if (player.Source is MediaPlaybackList mpl) currentItem = mpl.CurrentItem;
                        if (currentItem != null)
                        {
                            ConfigurePipeline(player, currentItem);
                        }
                    }
                    catch { }
                }
            };

            // Perform non-blocking WMI scan to detect dual-GPU hybrid graphics setups
            // (e.g. integrated AMD Radeon + discrete Nvidia RTX/GTX).
            _ = EnsureGpuEnvironmentDetectedAsync();

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

        await Task.Run(() =>
        {
            try
            {
                var gpuNames = new List<string>();
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["Name"] is string name && !string.IsNullOrWhiteSpace(name))
                    {
                        gpuNames.Add(name.Trim());
                    }
                }

                if (gpuNames.Count > 1)
                {
                    _isDualGpuPresent = true;
                    _gpuEnvironmentDescription = $"Dual GPU Environment ({string.Join(" + ", gpuNames)})";
                    Debug.WriteLine($"[HDR Dual-GPU] Multi-GPU environment detected: {_gpuEnvironmentDescription}. Configured direct DXGI shared-surface MPO pipeline for 10-bit HDR color preservation across adapters.");
                }
                else if (gpuNames.Count == 1)
                {
                    _isDualGpuPresent = false;
                    _gpuEnvironmentDescription = $"Single GPU ({gpuNames[0]})";
                }

                _gpuDetectionComplete = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Dual-GPU] GPU detection scan failed: {ex.Message}");
                _gpuDetectionComplete = true;
            }
        });
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

            if (dm.SupportsHdr10)
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
            _contentFormat     = HdrContentFormat.None;
            _lastDetectedItem  = null;
            _detectionComplete = false;
            return _contentFormat;
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
        _contentFormat     = ScanContentFormat(item);
        _lastDetectedItem  = item;
        _detectionComplete = true;
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
            string? sourcePath = AppServices.Playback.CurrentTrack?.SourcePath;
            if (!string.IsNullOrEmpty(sourcePath))
            {
                string fName = System.IO.Path.GetFileName(sourcePath);
                var containerTracks = MediaTrackFormatHelper.GetContainerTracks(sourcePath);
                var vTrack = containerTracks.Find(t => t.TrackType == 1);

                if (fName.Contains("DV", StringComparison.OrdinalIgnoreCase) || 
                    fName.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase) ||
                    fName.Contains("DolbyVision", StringComparison.OrdinalIgnoreCase) ||
                    (vTrack != null && vTrack.CodecId.Contains("DOLBY", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.WriteLine("[HDR] Detected: Dolby Vision from container/filename");
                    return HdrContentFormat.DolbyVision;
                }

                if (fName.Contains("HDR", StringComparison.OrdinalIgnoreCase) ||
                    fName.Contains("10bit", StringComparison.OrdinalIgnoreCase) ||
                    fName.Contains("ST2084", StringComparison.OrdinalIgnoreCase) ||
                    fName.Contains("BT2020", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("[HDR] Detected: HDR10 from container/filename");
                    return HdrContentFormat.Hdr10;
                }
            }

            foreach (var track in item.VideoTracks)
            {
                var props = track.GetEncodingProperties();

                // Layer 1 — Dolby Vision: subtype string
                if (props.Subtype != null &&
                    props.Subtype.Contains("DOLBY", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("[HDR] Detected: Dolby Vision");
                    return HdrContentFormat.DolbyVision;
                }

                // Layer 2 — MF_MT_TRANSFER_FUNCTION
                //   13 = MFVideoTransferFunction_2084 (PQ / ST2084) → HDR10
                //   15 = MFVideoTransferFunction_HLG               → HLG
                if (props.Properties.TryGetValue(
                    new Guid("93B7BE49-B4B2-4F40-A66E-C13B5F8E4E82"),
                    out var tfValue) && tfValue is uint tf)
                {
                    if (tf == 13)
                    {
                        Debug.WriteLine("[HDR] Detected: HDR10 (PQ/ST2084)");
                        return HdrContentFormat.Hdr10;
                    }
                    if (tf == 15)
                    {
                        Debug.WriteLine("[HDR] Detected: HLG");
                        return HdrContentFormat.Hlg;
                    }
                }

                // Layer 3 — MF_MT_VIDEO_PRIMARIES — BT.2020 = 9  →  likely HDR
                if (props.Properties.TryGetValue(
                    new Guid("dbfbe4d7-0740-4ee0-8192-850AB0E21935"),
                    out var primValue) && primValue is uint prims && prims == 9)
                {
                    Debug.WriteLine("[HDR] Inferred HDR10 from BT.2020 primaries");
                    return HdrContentFormat.Hdr10;
                }

                // Layer 4 — Subtype string contains "HDR"
                if (!string.IsNullOrEmpty(props.Subtype) &&
                    props.Subtype.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[HDR] Detected HDR from subtype: {props.Subtype}");
                    return HdrContentFormat.Hdr10;
                }

                // Layer 5 — HEVC/VP9 Main10 profile (MPEG-2 profile GUID, value 2)
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
                    Debug.WriteLine("[HDR] Inferred HDR10 from HEVC/VP9 10-bit profile");
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

        // Refresh display capability in real-time before checking if we should enable HDR output.
        // Reads from the shared DisplayManager — no duplicate COM call.
        RefreshDisplayCapability();

        // 1. Detect content format (cached per item — skips track scan on fullscreen toggle)
        var format = DetectContentFormat(item);

        // 2. Determine whether HDR output (and brightness override) should be active.
        //
        //    ForceOn  — user explicitly opted in; always boost regardless of display or content.
        //    ForceSdr — user explicitly opted out; always tone-map down to SDR.
        //    Auto     — enable only when playing detected HDR content on an HDR-capable screen
        //               (including laptops that support "Stream HDR video" but report an SDR desktop).
        bool isDisplayHdrCapable = _displayCapability == DisplayHdrCapability.Hdr10 ||
                                   _displayCapability == DisplayHdrCapability.DolbyVision ||
                                   AppServices.DisplayManager.CanStreamHdr;
        bool isContentHdr = format != HdrContentFormat.None;

        bool shouldEnableHdr = settings.HdrMode switch
        {
            HdrMode.ForceOn  => true,   // always boost — user's explicit choice
            HdrMode.ForceSdr => false,  // always SDR   — user's explicit choice
            _                => isContentHdr && isDisplayHdrCapable
        };

        // 3. Ensure the native MPO pipeline handles HDR (frame-server mode bypasses it)
        TryConfigureNativePipeline(player, shouldEnableHdr, _isDualGpuPresent);

        // 4. Configure Media Foundation tone-mapping operator and display-adaptive color grading
        ApplyToneMapping(item, settings.ToneMappingMode, shouldEnableHdr, isContentHdr);

        _hdrActive = shouldEnableHdr;
        UpdateBrightnessOverride();

        var args = new HdrStateChangedEventArgs
        {
            IsHdrActive               = _hdrActive,
            ContentFormat             = _contentFormat,
            DisplayCapability         = _displayCapability,
            ToneMappingMode           = settings.ToneMappingMode,
            PeakBrightnessNits        = (int)AppServices.DisplayManager.MaxLuminanceInNits,
            IsDualGpuEnvironment      = _isDualGpuPresent,
            IsHdrStreamingCapableOnly = AppServices.DisplayManager.IsHdrStreamingCapableOnly
        };

        Debug.WriteLine($"[HDR] Pipeline — active={_hdrActive}, " +
                        $"content={_contentFormat}, display={_displayCapability} (Profile={AppServices.DisplayManager.ActiveDisplayProfile}), " +
                        $"dualGpu={_isDualGpuPresent}, toneMap={settings.ToneMappingMode}, peak={AppServices.DisplayManager.MaxLuminanceInNits:F0} nits");

        HdrStateChanged?.Invoke(this, args);
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

            // On dual-GPU laptops (e.g. AMD Radeon iGPU + Nvidia GTX/RTX dGPU), ensure RealTimePlayback
            // is enabled during HDR playback so the presentation engine presents directly to DWM MPO
            // without composition queue latency across adapters.
            if (isDualGpu && isHdrActive && !player.RealTimePlayback)
            {
                player.RealTimePlayback = true;
                Debug.WriteLine("[HDR Dual-GPU] Enabled RealTimePlayback for direct cross-adapter MPO presentation");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Pipeline configuration failed: {ex.Message}");
        }
    }

    private static void ApplyToneMapping(MediaPlaybackItem? item, ToneMappingMode mode, bool isHdrActive, bool isContentHdr)
    {
        if (item == null || item.VideoTracks.Count == 0)
        {
            return;
        }

        try
        {
            var displayManager = AppServices.DisplayManager;
            var displayProfile = displayManager.ActiveDisplayProfile;
            float peakNits = displayManager.MaxLuminanceInNits;
            float sdrWhiteNits = displayManager.SdrWhiteLevelInNits;

            // Resolve Tone Mapping Operator: Display-Adaptive or manual preference
            uint toneMapOperatorIndex = mode switch
            {
                ToneMappingMode.DisplayAdaptive => displayProfile switch
                {
                    Display.DisplayProfileKind.TrueHdrOledOrMiniLed => 3u, // High-End HDR: Direct passthrough / Clip at screen peak
                    Display.DisplayProfileKind.EntryHdr            => 2u, // Entry HDR: BT.2408 highlight compression
                    Display.DisplayProfileKind.WideColorGamutSdr   => 2u, // WCG SDR: BT.2408 DCI-P3 reference
                    _                                              => 2u  // Standard SDR: BT.2408 ITU standard
                },
                ToneMappingMode.Bt2408   => 2u, // BT.2408 (ITU standard reference)
                ToneMappingMode.Aces     => 1u, // ACES (Cinematic highlights)
                ToneMappingMode.Reinhard => 0u, // Reinhard (Smooth roll-off)
                ToneMappingMode.Clip     => 3u, // Clip
                _                        => 2u
            };

            var toneMapGuid = new Guid("DE9AC8C9-9602-4A85-AA27-BCE095709DFF"); // MF_VIDEO_TONEMAPPING_OPERATOR
            var primariesGuid = new Guid("dbfbe4d7-0740-4ee0-8192-850AB0E21935"); // MF_MT_VIDEO_PRIMARIES
            var transferFuncGuid = new Guid("93B7BE49-B4B2-4F40-A66E-C13B5F8E4E82"); // MF_MT_TRANSFER_FUNCTION
            var yuvMatrixGuid = new Guid("3e23d46a-0ba1-44e5-a4e3-b9c1046182d4"); // MF_MT_YUV_MATRIX
            var nominalRangeGuid = new Guid("66753cff-82ec-42f2-ac1a-e69a0b76cf55"); // MF_MT_VIDEO_NOMINAL_RANGE

            foreach (var track in item.VideoTracks)
            {
                try
                {
                    var props = track.GetEncodingProperties();
                    props.Properties[toneMapGuid] = toneMapOperatorIndex;

                    if (isContentHdr)
                    {
                        // Explicitly declare incoming stream format as 10-bit BT.2020 PQ Studio Range
                        // This allows the Video Processor to apply the 3x3 BT.2020->BT.709 color conversion matrix
                        // and ST 2084 PQ tone curve accurately without oversaturating or crushing dark tones!
                        props.Properties[primariesGuid] = 9u;      // MFVideoPrimaries_BT2020 (9)
                        props.Properties[transferFuncGuid] = 13u;  // MFVideoTransferFunction_2084 / PQ (13)
                        props.Properties[yuvMatrixGuid] = 3u;      // MFVideoYUVMatrix_BT2020 (3)
                        props.Properties[nominalRangeGuid] = 1u;   // MFNominalRange_Normal (Studio range 64-940)
                    }
                    else
                    {
                        // Standard SDR source: BT.709 sRGB Gamma 2.2
                        props.Properties[primariesGuid] = 2u;      // MFVideoPrimaries_BT709 (2)
                        props.Properties[transferFuncGuid] = 5u;   // MFVideoTransferFunction_709 (5)
                        props.Properties[yuvMatrixGuid] = 2u;      // MFVideoYUVMatrix_BT709 (2)
                        props.Properties[nominalRangeGuid] = 1u;   // MFNominalRange_Normal (Studio range 16-235)
                    }
                }
                catch { }
            }

            Debug.WriteLine($"[HDR Display-Adaptive ToneMapping] Profile: {displayProfile}, Screen Peak: {peakNits:F0} nits, White: {sdrWhiteNits:F0} nits, Mode: '{mode}' (operator index {toneMapOperatorIndex}, HdrActive={isHdrActive}) across {item.VideoTracks.Count} video tracks");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR ToneMapping] Failed to apply tone mapping: {ex.Message}");
        }
    }

    private BrightnessOverrideHelper? _brightnessOverride;
    private bool _isAppFullscreen;

    public void SetFullscreenState(bool isFullscreen)
    {
        _isAppFullscreen = isFullscreen;
        UpdateBrightnessOverride();
    }

    private void UpdateBrightnessOverride()
    {
        try
        {
            bool isContentHdr = _contentFormat != HdrContentFormat.None;
            bool shouldBoost = (_hdrActive || (_isAppFullscreen && isContentHdr)) && AppServices.Settings.Current.AutoBoostHdrBrightness;

            if (shouldBoost)
            {
                if (_brightnessOverride == null)
                {
                    _brightnessOverride = new BrightnessOverrideHelper();
                }
                _brightnessOverride.TryOverrideToMax(_hwnd);
            }
            else
            {
                if (_brightnessOverride != null)
                {
                    _brightnessOverride.Release();
                    _brightnessOverride.Dispose();
                    _brightnessOverride = null; // Recreate next time to capture any manual brightness changes
                }
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
        _contentFormat     = HdrContentFormat.None;
        _hdrActive         = false;
        _lastDetectedItem  = null; // clear cache so next media gets a fresh detection
        _detectionComplete = false;

        try { _brightnessOverride?.Release(); }
        catch { }

        try { _brightnessOverride?.Dispose(); }
        catch { }

        _brightnessOverride = null; // prevent use of disposed instance

        Debug.WriteLine("[HDR] Content state reset");
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
