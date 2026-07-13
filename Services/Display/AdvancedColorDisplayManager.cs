using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.Graphics.Display;

namespace LumiereMediaPlayer.Services.Display;

/// <summary>
/// Single authoritative source for display advanced-color state.
/// <para>
/// Owned by <see cref="AppServices.DisplayManager"/>. Both
/// <see cref="HdrPipelineService"/> and UI code read from this class so that
/// only <b>one</b> <see cref="DisplayInformation"/> instance is kept alive
/// per window.
/// </para>
/// <para>
/// <b>Thread safety:</b> <see cref="DisplayInformation.AdvancedColorInfoChanged"/>
/// fires on the thread that created the <see cref="DisplayInformation"/> object
/// (the UI thread in WinUI 3). All field writes therefore happen on the UI thread.
/// Callers that read properties from non-UI threads should be aware that values
/// can change between reads — in practice this is benign for display-capability
/// checks whose staleness window is imperceptible.
/// </para>
/// </summary>
public sealed class AdvancedColorDisplayManager
{
    private DisplayInformation? _displayInfo;

    // ── Current state ────────────────────────────────────────────────
    // All fields are written on the UI thread (DisplayInformation events fire there).
    // Marked volatile so that reads from non-UI threads always see the latest value
    // without a full memory barrier.

    private volatile bool  _isHdrActive;
    private volatile bool  _canStreamHdr;
    private float          _sdrWhiteLevelInNits = 80f; // float is not atomic; read from UI thread only
    private volatile bool  _supportsHdr10;
    private volatile bool  _supportsWcg;

    // ── Events ───────────────────────────────────────────────────────

    public event EventHandler? AdvancedColorInfoChanged;

    // ── Public properties ─────────────────────────────────────────────

    /// <summary>HDR output is currently active on the desktop.</summary>
    public bool IsHdrActive => _isHdrActive;

    /// <summary>HDR streaming is supported (may be true even when desktop is SDR).</summary>
    public bool CanStreamHdr => _canStreamHdr;

    /// <summary>
    /// SDR reference white level reported by the display driver.
    /// Read this on the UI thread only — it is a non-atomic float.
    /// </summary>
    public float SdrWhiteLevelInNits => _sdrWhiteLevelInNits;

    /// <summary>
    /// True when the display is HDR10-capable for the purposes of pipeline configuration.
    /// Covers both active HDR desktop mode and "Stream HDR video" laptop screens.
    /// </summary>
    public bool SupportsHdr10 => _supportsHdr10;

    /// <summary>True when the display supports WCG but not full HDR luminance.</summary>
    public bool SupportsWcg   => _supportsWcg;

    // ── Initialise ───────────────────────────────────────────────────

    public AdvancedColorDisplayManager() { }

    public void InitializeForWindow(Window window)
    {
        try
        {
            var hwnd     = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);

            _displayInfo = DisplayInformation.CreateForWindowId(windowId);
            _displayInfo.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged;
            UpdateColorInfo();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HDR] Failed to init DisplayInformation: {ex.Message}");
        }
    }

    // ── Event handler ─────────────────────────────────────────────────

    private void OnAdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        // DisplayInformation events fire on the UI thread in WinUI 3.
        UpdateColorInfo();
    }

    // ── Core update ───────────────────────────────────────────────────

    private void UpdateColorInfo()
    {
        if (_displayInfo == null) return;

        try
        {
            var aci = _displayInfo.GetAdvancedColorInfo();

            _isHdrActive         = aci.CurrentAdvancedColorKind == DisplayAdvancedColorKind.HighDynamicRange;
            _canStreamHdr        = aci.IsAdvancedColorKindAvailable(DisplayAdvancedColorKind.HighDynamicRange);
            _sdrWhiteLevelInNits = (float)aci.SdrWhiteLevelInNits;

            // HDR10 / HDR10+ capable: covers active HDR desktop AND "Stream HDR video" laptops.
            _supportsHdr10 = aci.CurrentAdvancedColorKind == DisplayAdvancedColorKind.HighDynamicRange
                          || aci.IsHdrMetadataFormatCurrentlySupported(DisplayHdrMetadataFormat.Hdr10)
                          || aci.IsHdrMetadataFormatCurrentlySupported(DisplayHdrMetadataFormat.Hdr10Plus);

            // WCG: wide gamut but not full HDR luminance range.
            _supportsWcg = aci.CurrentAdvancedColorKind == DisplayAdvancedColorKind.WideColorGamut;

            System.Diagnostics.Debug.WriteLine(
                $"[HDR Display] HDR Active: {_isHdrActive}, Stream: {_canStreamHdr}, " +
                $"SupportsHdr10: {_supportsHdr10}, SupportsWcg: {_supportsWcg}, " +
                $"SDR White Level: {_sdrWhiteLevelInNits} nits");

            // Raise on the UI thread so subscribers don't need to marshal.
            // UpdateColorInfo() is already called on the UI thread (see OnAdvancedColorInfoChanged),
            // but guard with DispatcherQueue.TryEnqueue in case it is ever called from another context.
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                AdvancedColorInfoChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HDR Display] Failed to read AdvancedColorInfo: {ex.Message}");
        }
    }
}
