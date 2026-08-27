using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.Graphics.Display;

namespace LumiereMediaPlayer.Services.Display;

public enum DisplayProfileKind
{
    StandardSdr,            // Standard sRGB SDR (<= 300 nits)
    WideColorGamutSdr,      // Wide color gamut SDR (DCI-P3 / AdobeRGB)
    EntryHdr,               // DisplayHDR 400 / Laptop HDR (350-550 nits)
    TrueHdrOledOrMiniLed    // High-end HDR (>= 600 nits, OLED / Mini-LED)
}

/// <summary>
/// Single authoritative source for display advanced-color state and screen hardware characterization.
/// </summary>
public sealed class AdvancedColorDisplayManager
{
    private DisplayInformation? _displayInfo;

    // ── Current state ────────────────────────────────────────────────
    private volatile bool  _isHdrActive;
    private volatile bool  _canStreamHdr;
    private float          _sdrWhiteLevelInNits = 80f;
    private float          _maxLuminanceInNits = 300f;
    private float          _minLuminanceInNits = 0.1f;
    private float          _maxFullFrameLuminanceInNits = 250f;
    private volatile bool  _supportsHdr10;
    private volatile bool  _supportsWcg;
    private DisplayProfileKind _activeProfile = DisplayProfileKind.StandardSdr;
    private DisplayAdvancedColorKind _currentColorKind = DisplayAdvancedColorKind.StandardDynamicRange;

    // ── Events ───────────────────────────────────────────────────────
    public event EventHandler? AdvancedColorInfoChanged;

    // ── Public properties ─────────────────────────────────────────────
    public bool IsHdrActive => _isHdrActive;
    public bool CanStreamHdr => _canStreamHdr;
    public bool IsHdrStreamingCapableOnly => !_isHdrActive && _canStreamHdr;
    public float SdrWhiteLevelInNits => _sdrWhiteLevelInNits;
    public float MaxLuminanceInNits => _maxLuminanceInNits;
    public float MinLuminanceInNits => _minLuminanceInNits;
    public float MaxFullFrameLuminanceInNits => _maxFullFrameLuminanceInNits;
    public bool SupportsHdr10 => _supportsHdr10 || _canStreamHdr;
    public bool SupportsWcg => _supportsWcg;
    public DisplayProfileKind ActiveDisplayProfile => _activeProfile;
    public DisplayAdvancedColorKind CurrentColorKind => _currentColorKind;

    public string DisplayProfileSummary => _activeProfile switch
    {
        DisplayProfileKind.TrueHdrOledOrMiniLed => $"High-End HDR Display ({_maxLuminanceInNits:F0} nits peak)",
        DisplayProfileKind.EntryHdr            => $"Entry HDR Display ({_maxLuminanceInNits:F0} nits peak)",
        DisplayProfileKind.WideColorGamutSdr   => $"Wide Color Gamut Display (DCI-P3 SDR)",
        _                                      => $"Standard sRGB Display ({_sdrWhiteLevelInNits:F0} nits white)"
    };

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

    private void OnAdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        UpdateColorInfo();
    }

    public void UpdateColorInfo()
    {
        if (_displayInfo == null) return;

        try
        {
            var aci = _displayInfo.GetAdvancedColorInfo();

            _currentColorKind    = aci.CurrentAdvancedColorKind;
            _isHdrActive         = aci.CurrentAdvancedColorKind == DisplayAdvancedColorKind.HighDynamicRange;
            _canStreamHdr        = aci.IsAdvancedColorKindAvailable(DisplayAdvancedColorKind.HighDynamicRange);
            _sdrWhiteLevelInNits = (float)Math.Max(80.0, aci.SdrWhiteLevelInNits);
            _maxLuminanceInNits  = (float)Math.Max(_sdrWhiteLevelInNits, aci.MaxLuminanceInNits);
            _minLuminanceInNits  = (float)Math.Max(0.0, aci.MinLuminanceInNits);
            _maxFullFrameLuminanceInNits = _maxLuminanceInNits;

            _supportsHdr10 = aci.CurrentAdvancedColorKind == DisplayAdvancedColorKind.HighDynamicRange
                          || aci.IsHdrMetadataFormatCurrentlySupported(DisplayHdrMetadataFormat.Hdr10)
                          || aci.IsHdrMetadataFormatCurrentlySupported(DisplayHdrMetadataFormat.Hdr10Plus)
                          || _canStreamHdr;

            _supportsWcg = aci.CurrentAdvancedColorKind == DisplayAdvancedColorKind.WideColorGamut;

            // Compute active display profile
            if (_isHdrActive && _maxLuminanceInNits >= 550f)
            {
                _activeProfile = DisplayProfileKind.TrueHdrOledOrMiniLed;
            }
            else if (_isHdrActive || _canStreamHdr || (_maxLuminanceInNits >= 350f && _supportsHdr10))
            {
                _activeProfile = DisplayProfileKind.EntryHdr;
            }
            else if (_supportsWcg)
            {
                _activeProfile = DisplayProfileKind.WideColorGamutSdr;
            }
            else
            {
                _activeProfile = DisplayProfileKind.StandardSdr;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[HDR Display] Profile: {_activeProfile}, Kind: {_currentColorKind}, " +
                $"Peak: {_maxLuminanceInNits:F0} nits, White: {_sdrWhiteLevelInNits:F0} nits, Min: {_minLuminanceInNits:F3} nits");

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
