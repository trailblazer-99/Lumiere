using System;
using System.Diagnostics;
using LumiereMediaPlayer.Models;
using Windows.Graphics.Display;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Detects display HDR capability, inspects video content metadata for HDR
/// format, and configures the MediaPlayer accordingly for optimal HDR/SDR
/// output and auto-brightness.
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

    // ── Display / brightness handles ─────────────────────────────────

    private DisplayInformation? _displayInfo;
    private DisplayEnhancementOverride? _brightnessOverride;

    // ── Public read-only state ───────────────────────────────────────

    public DisplayHdrCapability DisplayCapability => _displayCapability;
    public HdrContentFormat ContentFormat => _contentFormat;
    public bool IsHdrActive => _hdrActive;

    public string ContentFormatLabel => _contentFormat switch
    {
        HdrContentFormat.Hdr10 => "HDR10",
        HdrContentFormat.Hlg => "HLG",
        HdrContentFormat.DolbyVision => "Dolby Vision",
        _ => "SDR"
    };

    public string DisplayCapabilityLabel => _displayCapability switch
    {
        DisplayHdrCapability.Hdr10 => "HDR10 Display",
        DisplayHdrCapability.DolbyVision => "Dolby Vision Display",
        DisplayHdrCapability.Wcg => "WCG Display",
        _ => "SDR Display"
    };

    // ── Initialise ───────────────────────────────────────────────────

    /// <summary>
    /// Call once after the main window is ready so that
    /// <c>DisplayInformation.GetForCurrentView()</c> resolves correctly.
    /// </summary>
    public void Initialize()
    {
        // DisplayInformation — tracks HDR capability of the display
        try
        {
            if (Windows.UI.Core.CoreWindow.GetForCurrentThread() != null)
            {
                _displayInfo = DisplayInformation.GetForCurrentView();
                RefreshDisplayCapability();

                _displayInfo.ColorProfileChanged      += (_, _) => RefreshDisplayCapability();
                _displayInfo.AdvancedColorInfoChanged  += (_, _) => RefreshDisplayCapability();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] DisplayInformation unavailable: {ex.Message}");
        }

        // DisplayEnhancementOverride — lets the app request peak brightness
        // during HDR playback; the OS/driver honours it while our app has focus.
        try
        {
            if (Windows.UI.Core.CoreWindow.GetForCurrentThread() != null)
            {
                _brightnessOverride = DisplayEnhancementOverride.GetForCurrentView();
                Debug.WriteLine("[HDR] DisplayEnhancementOverride acquired");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] DisplayEnhancementOverride unavailable: {ex.Message}");
        }
    }

    // ── Display detection ────────────────────────────────────────────

    private void RefreshDisplayCapability()
    {
        try
        {
            if (_displayInfo == null) return;

            var aci = _displayInfo.GetAdvancedColorInfo();
            _displayCapability = aci.CurrentAdvancedColorKind switch
            {
                AdvancedColorKind.HighDynamicRange => DisplayHdrCapability.Hdr10,
                AdvancedColorKind.WideColorGamut   => DisplayHdrCapability.Wcg,
                _                                   => DisplayHdrCapability.Sdr
            };

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
    /// </summary>
    public HdrContentFormat DetectContentFormat(MediaPlaybackItem? item)
    {
        if (item == null)
        {
            _contentFormat = HdrContentFormat.None;
            return _contentFormat;
        }

        try
        {
            foreach (var track in item.VideoTracks)
            {
                var props = track.GetEncodingProperties();

                // Dolby Vision: profile encoded in subtype
                if (props.Subtype != null &&
                    props.Subtype.Contains("DOLBY", StringComparison.OrdinalIgnoreCase))
                {
                    _contentFormat = HdrContentFormat.DolbyVision;
                    Debug.WriteLine("[HDR] Detected: Dolby Vision");
                    return _contentFormat;
                }

                // MF_MT_TRANSFER_FUNCTION:
                //   MFVideoTransferFunction_2084 = 13  →  HDR10 / PQ
                //   MFVideoTransferFunction_HLG  = 15  →  HLG
                if (props.Properties.TryGetValue(
                    new Guid("93B7BE49-B4B2-4F40-A66E-C13B5F8E4E82"),
                    out var tfValue) && tfValue is uint tf)
                {
                    if (tf == 13)
                    {
                        _contentFormat = HdrContentFormat.Hdr10;
                        Debug.WriteLine("[HDR] Detected: HDR10 (PQ/ST2084)");
                        return _contentFormat;
                    }
                    if (tf == 15)
                    {
                        _contentFormat = HdrContentFormat.Hlg;
                        Debug.WriteLine("[HDR] Detected: HLG");
                        return _contentFormat;
                    }
                }

                // MF_MT_VIDEO_PRIMARIES — BT.2020 = 9  →  likely HDR
                if (props.Properties.TryGetValue(
                    new Guid("dbfbe4d7-0740-4ee0-8192-850AB0E21935"),
                    out var primValue) && primValue is uint prims && prims == 9)
                {
                    _contentFormat = HdrContentFormat.Hdr10;
                    Debug.WriteLine("[HDR] Inferred HDR10 from BT.2020 primaries");
                    return _contentFormat;
                }

                // Subtype string fallback
                if (!string.IsNullOrEmpty(props.Subtype) &&
                    props.Subtype.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                {
                    _contentFormat = HdrContentFormat.Hdr10;
                    Debug.WriteLine($"[HDR] Detected HDR from subtype: {props.Subtype}");
                    return _contentFormat;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Content detection error: {ex.Message}");
        }

        _contentFormat = HdrContentFormat.None;
        return _contentFormat;
    }

    // ── Pipeline configuration ────────────────────────────────────────

    /// <summary>
    /// Configure the MediaPlayer HDR pipeline.  Call this from:
    /// <list type="bullet">
    ///   <item><description><c>OnMediaOpened</c> in VideoPage (windowed)</description></item>
    ///   <item><description><c>OnFullscreenMediaOpened</c> in MainWindow (fullscreen)</description></item>
    ///   <item><description>When fullscreen is entered while media is already playing</description></item>
    ///   <item><description>When the user changes HDR settings</description></item>
    /// </list>
    /// </summary>
    public void ConfigurePipeline(MediaPlayer player, MediaPlaybackItem? item)
    {
        var settings = AppServices.Settings.Current;

        // 1. Detect content format
        var format = DetectContentFormat(item);

        // 2. Determine whether HDR output should be active
        bool shouldEnableHdr = settings.HdrMode switch
        {
            HdrMode.ForceOn  => true,
            HdrMode.ForceSdr => false,
            _                => format != HdrContentFormat.None // Enable HDR if content has HDR format
                                // Ignore display HDR capability; rely on OS to handle tone‑mapping
        };

        // 3. RealTimePlayback is intentionally omitted. 
        // Setting RealTimePlayback = true breaks pausing and seeking for local media files
        // because it instructs Media Foundation to treat the source as a live communication stream.

        // 4. Ensure the native MPO pipeline handles HDR (frame-server mode bypasses it)
        TryConfigureNativePipeline(player);

        // 5. Auto-brightness via DisplayEnhancementOverride
        TryApplyAutoBrightness(shouldEnableHdr, settings);

        _hdrActive = shouldEnableHdr;

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
            // IsVideoFrameServerEnabled=true routes decoded frames through a
            // software path that bypasses the OS Media Processing Object (MPO),
            // which is responsible for hardware HDR pass-through and tone-mapping.
            // Ensure it is false so the OS compositor can do HDR natively.
            if (player.IsVideoFrameServerEnabled)
            {
                player.IsVideoFrameServerEnabled = false;
                Debug.WriteLine("[HDR] Disabled VideoFrameServer — native MPO pipeline active");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Native pipeline config error: {ex.Message}");
        }
    }

    // ── Auto-brightness via DisplayEnhancementOverride ────────────────

    private void TryApplyAutoBrightness(bool hdrActive, AppSettings settings)
    {
        if (_brightnessOverride == null)
        {
            Debug.WriteLine("[HDR] No DisplayEnhancementOverride available; auto-brightness disabled.");
            return;
        }

        try
        {
            if (hdrActive)
            {
                // Define brightness override settings – maximum level (100%)
                // You can also use CreateFromNits(settings.PeakBrightnessNits) for an absolute nits value.
                var brightnessSettings = BrightnessOverrideSettings.CreateFromLevel(1.0);
                _brightnessOverride.BrightnessOverrideSettings = brightnessSettings;

                // Request the OS to apply the enhancement if supported.
                if (_brightnessOverride.CanOverride)
                {
                    _brightnessOverride.RequestOverride();
                    Debug.WriteLine($"[HDR] Auto-brightness override requested (peak {settings.PeakBrightnessNits} nits). CanOverride: true");
                }
                else
                {
                    Debug.WriteLine("[HDR] DisplayEnhancementOverride not supported on this device; cannot request HDR brightness.");
                }
            }
            else
            {
                // Release the brightness override — OS returns to its default adaptive-brightness.
                _brightnessOverride.StopOverride();
                Debug.WriteLine("[HDR] Auto-brightness override released");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR] Auto-brightness error: {ex.GetType().FullName}: {ex.Message} (HResult=0x{ex.HResult:X8})");
            // Ensure the override is released if an error occurs.
            try { _brightnessOverride.StopOverride(); } catch { }
        }
    }

    // ── Reset on stop ────────────────────────────────────────────────

    /// <summary>
    /// Reset tracked content format and release the brightness override
    /// when playback stops or is torn down.
    /// </summary>
    public void ResetContentState()
    {
        _contentFormat = HdrContentFormat.None;
        _hdrActive = false;

        try { _brightnessOverride?.StopOverride(); }
        catch { }
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
