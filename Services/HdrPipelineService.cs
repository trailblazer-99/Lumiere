using System;
using System.Diagnostics;
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
/// <b>Tone-mapping:</b>  The native Media Processing Object (MPO) pipeline
/// (<c>IsVideoFrameServerEnabled = false</c>) lets the Windows compositor
/// and display hardware handle HDR → SDR tone-mapping.  The
/// <see cref="AppSettings.ToneMappingMode"/> and
/// <see cref="AppSettings.PeakBrightnessNits"/> settings are preserved for
/// future custom-renderer support but are <b>not</b> applied by this
/// service.
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
                   _displayCapability == DisplayHdrCapability.DolbyVision;
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
        _                                => "SDR Display"
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

            // React to display HDR changes (e.g. user toggles Windows HDR in Settings)
            // by listening to the single shared display manager event.
            AppServices.DisplayManager.AdvancedColorInfoChanged += (_, _) => RefreshDisplayCapability();

            Debug.WriteLine("[HDR] Initialized — display tracking via AdvancedColorDisplayManager");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Initialize error: {ex.Message}");
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

            if (dm.SupportsHdr10)
                _displayCapability = DisplayHdrCapability.Hdr10;
            else if (dm.SupportsWcg)
                _displayCapability = DisplayHdrCapability.Wcg;
            else
                _displayCapability = DisplayHdrCapability.Sdr;

            Debug.WriteLine($"[HDR] Display capability: {_displayCapability}");
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
                     props.Subtype.Contains("VP90", StringComparison.OrdinalIgnoreCase)) &&
                    profile == 2)
                {
                    Debug.WriteLine("[HDR] Inferred HDR10 from HEVC/VP9 10-bit profile");
                    return HdrContentFormat.Hdr10;
                }
            }

            // Layer 6 — Filename / metadata keyword matching
            try
            {
                var currentTrack = AppServices.Playback.CurrentTrack;
                if (currentTrack != null)
                {
                    string pathToCheck = (currentTrack.SourcePath ?? currentTrack.Title ?? "").ToLowerInvariant();
                    if (pathToCheck.Contains("hdr")    || pathToCheck.Contains("10bit") ||
                        pathToCheck.Contains("dovi")   || pathToCheck.Contains("dolby") ||
                        pathToCheck.Contains("vision") || pathToCheck.Contains("hlg")   ||
                        pathToCheck.Contains("st2084") || pathToCheck.Contains("bt2020"))
                    {
                        foreach (var track in item.VideoTracks)
                        {
                            var props = track.GetEncodingProperties();
                            if (props.Subtype != null &&
                                (props.Subtype.Contains("HEVC", StringComparison.OrdinalIgnoreCase) ||
                                 props.Subtype.Contains("H265", StringComparison.OrdinalIgnoreCase) ||
                                 props.Subtype.Contains("AV01", StringComparison.OrdinalIgnoreCase) ||
                                 props.Subtype.Contains("AV1",  StringComparison.OrdinalIgnoreCase) ||
                                 props.Subtype.Contains("VP9",  StringComparison.OrdinalIgnoreCase)))
                            {
                                Debug.WriteLine("[HDR] Detected HDR via keyword matching on HEVC/AV1/VP9 video track");
                                return HdrContentFormat.Hdr10;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Filename Fallback] Error: {ex.Message}");
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
        //    Auto     — unconditionally enable so the brightness boost fires on every display,
        //               including laptops that support "Stream HDR video" but report an SDR desktop.
        //               This is the safest fallback for heterogeneous hardware.
        bool shouldEnableHdr = settings.HdrMode switch
        {
            HdrMode.ForceOn  => true,   // always boost — user's explicit choice
            HdrMode.ForceSdr => false,  // always SDR   — user's explicit choice
            _                => true    // Auto: unconditional (covers all display configurations)
        };

        // 3. Ensure the native MPO pipeline handles HDR (frame-server mode bypasses it)
        TryConfigureNativePipeline(player);

        _hdrActive = shouldEnableHdr;
        UpdateBrightnessOverride();

        var args = new HdrStateChangedEventArgs
        {
            IsHdrActive        = _hdrActive,
            ContentFormat      = _contentFormat,
            DisplayCapability  = _displayCapability,
            ToneMappingMode    = settings.ToneMappingMode,
            PeakBrightnessNits = settings.PeakBrightnessNits
        };

        Debug.WriteLine($"[HDR] Pipeline — active={_hdrActive}, " +
                        $"content={_contentFormat}, display={_displayCapability}, " +
                        $"toneMap={settings.ToneMappingMode}, peak={settings.PeakBrightnessNits} nits");

        HdrStateChanged?.Invoke(this, args);
    }

    // ── Native MPO pipeline ───────────────────────────────────────────

    private static void TryConfigureNativePipeline(MediaPlayer player)
    {
        try
        {
            if (player.IsVideoFrameServerEnabled)
            {
                player.IsVideoFrameServerEnabled = false;
                Debug.WriteLine("[HDR] Disabled VideoFrameServer — native MPO pipeline active");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Pipeline configuration failed: {ex.Message}");
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
            if (_hdrActive && _isAppFullscreen)
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
}
