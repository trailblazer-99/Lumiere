using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// A custom system backdrop that uses <see cref="DesktopAcrylicController"/> configured
/// with <see cref="DesktopAcrylicKind.Thin"/> to provide genuine, translucent frosted-glass
/// acrylic transparency behind the window.
/// </summary>
public sealed class CustomAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _acrylicController;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        try
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);
        }
        catch { }

        try
        {
            if (DesktopAcrylicController.IsSupported() && connectedTarget != null && xamlRoot != null)
            {
                // Safely clean up any existing controller before creating a new one
                if (_acrylicController != null)
                {
                    try { _acrylicController.RemoveSystemBackdropTarget(connectedTarget); } catch { }
                    try { _acrylicController.Dispose(); } catch { }
                    _acrylicController = null;
                }

                _acrylicController = new DesktopAcrylicController
                {
                    Kind = DesktopAcrylicKind.Thin
                };

                // Per Windows App SDK lifecycle contract, target must be attached BEFORE configuration
                _acrylicController.AddSystemBackdropTarget(connectedTarget);

                var config = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
                if (config != null)
                {
                    _acrylicController.SetSystemBackdropConfiguration(config);
                    UpdateControllerConfiguration(config);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomAcrylicBackdrop.OnTargetConnected] Handled: {ex.Message}");
        }
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // Do NOT call base.OnDefaultSystemBackdropConfigurationChanged:
        // In Windows App SDK, the base SystemBackdrop implementation throws ArgumentException
        // when custom controllers manage their own SystemBackdropConfiguration.
        try
        {
            if (_acrylicController != null && target != null && xamlRoot != null && xamlRoot.Content != null)
            {
                var config = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
                if (config != null)
                {
                    _acrylicController.SetSystemBackdropConfiguration(config);
                    UpdateControllerConfiguration(config);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomAcrylicBackdrop.OnDefaultSystemBackdropConfigurationChanged] Handled: {ex.Message}");
        }
    }

    private void UpdateControllerConfiguration(SystemBackdropConfiguration? config)
    {
        if (_acrylicController == null) return;

        try
        {
            bool isLight = false;
            if (config != null)
            {
                isLight = config.Theme == SystemBackdropTheme.Light;
            }
            else
            {
                isLight = ThemeHelper.GetEffectiveElementTheme() == ElementTheme.Light;
            }

            _acrylicController.Kind = DesktopAcrylicKind.Thin;
            if (isLight)
            {
                _acrylicController.TintColor = Windows.UI.Color.FromArgb(255, 245, 245, 247);
                _acrylicController.TintOpacity = 0.35f;
                _acrylicController.LuminosityOpacity = 0.50f;
                _acrylicController.FallbackColor = Windows.UI.Color.FromArgb(255, 243, 243, 243);
            }
            else
            {
                _acrylicController.TintColor = Windows.UI.Color.FromArgb(255, 20, 20, 24);
                _acrylicController.TintOpacity = 0.50f;
                _acrylicController.LuminosityOpacity = 0.65f;
                _acrylicController.FallbackColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomAcrylicBackdrop.UpdateControllerConfiguration] Handled: {ex.Message}");
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        try
        {
            if (_acrylicController != null)
            {
                if (disconnectedTarget != null)
                {
                    try
                    {
                        _acrylicController.RemoveSystemBackdropTarget(disconnectedTarget);
                    }
                    catch { }
                }

                try
                {
                    _acrylicController.Dispose();
                }
                catch { }

                _acrylicController = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomAcrylicBackdrop.OnTargetDisconnected] Handled: {ex.Message}");
        }

        try
        {
            base.OnTargetDisconnected(disconnectedTarget);
        }
        catch { }
    }
}
