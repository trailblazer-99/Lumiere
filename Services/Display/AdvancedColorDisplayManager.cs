using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Graphics.Display;

namespace FluentMediaPlayer.Services.Display;

public sealed class AdvancedColorDisplayManager
{
    private DisplayInformation? _displayInfo;
    private bool _isHdrActive;
    private bool _canStreamHdr;
    private float _sdrWhiteLevelInNits = 80f;

    public event EventHandler? AdvancedColorInfoChanged;

    public bool IsHdrActive => _isHdrActive;
    public bool CanStreamHdr => _canStreamHdr;
    public float SdrWhiteLevelInNits => _sdrWhiteLevelInNits;

    public AdvancedColorDisplayManager()
    {
    }

    public void InitializeForWindow(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
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

    private void UpdateColorInfo()
    {
        if (_displayInfo == null) return;

        try
        {
            var aci = _displayInfo.GetAdvancedColorInfo();
            
            _isHdrActive = aci.CurrentAdvancedColorKind == DisplayAdvancedColorKind.HighDynamicRange;
            _canStreamHdr = aci.IsAdvancedColorKindAvailable(DisplayAdvancedColorKind.HighDynamicRange);
            _sdrWhiteLevelInNits = (float)aci.SdrWhiteLevelInNits;

            System.Diagnostics.Debug.WriteLine($"[HDR Display] HDR Active: {_isHdrActive}, Stream: {_canStreamHdr}, SDR White Level: {_sdrWhiteLevelInNits}");

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
