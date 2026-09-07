using System;
using System.Collections.Generic;
using Microsoft.Win32;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Helpers
{
    public class CaptionThemeInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
        public bool IsCustom { get; set; }
    }

    public static class WindowsCaptionHelper
    {
        private const string HKLM_THEMES_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ClosedCaptioning\Theme";
        private const string HKCU_THEMES_PATH = @"Software\Microsoft\Windows\CurrentVersion\ClosedCaptioning\Theme";
        private const string HKCU_CC_PATH = @"Software\Microsoft\Windows\CurrentVersion\ClosedCaptioning";

        public static readonly (string Id, string Name)[] StandardThemes = new[]
        {
            ("{642F4BD2-475F-4802-9B13-95261896CB1C}", "Default"),
            ("{ADDF2B19-ED5E-4778-AFB2-3AE1C8EC8DB8}", "White on black"),
            ("{BA209C4E-1A6C-49A2-8AE2-A02476F598BF}", "Small caps"),
            ("{C9FC6A2C-D04B-41BB-BC5A-B764515C29FD}", "Large text"),
            ("{DF834234-A0EF-4E2A-BB87-C40E5D1CFC8C}", "Yellow on blue")
        };

        public static List<CaptionThemeInfo> GetWindowsCaptionThemes()
        {
            var list = new List<CaptionThemeInfo>();
            string currentSelectedId = GetCurrentSelectedThemeId();

            // 1. Standard Built-in Windows Themes
            foreach (var (id, name) in StandardThemes)
            {
                bool isSelected = string.Equals(id, currentSelectedId, StringComparison.OrdinalIgnoreCase);
                list.Add(new CaptionThemeInfo
                {
                    Id = id,
                    Name = name,
                    IsSelected = isSelected,
                    IsCustom = false
                });
            }

            // 2. Custom User-Created Themes from Windows Settings
            try
            {
                using var customKey = Registry.CurrentUser.OpenSubKey(HKCU_THEMES_PATH);
                if (customKey != null)
                {
                    foreach (var subKeyName in customKey.GetSubKeyNames())
                    {
                        using var themeSubKey = customKey.OpenSubKey(subKeyName);
                        if (themeSubKey != null)
                        {
                            string themeName = themeSubKey.GetValue("ThemeName")?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(themeName))
                            {
                                bool isSelected = string.Equals(subKeyName, currentSelectedId, StringComparison.OrdinalIgnoreCase);
                                list.Add(new CaptionThemeInfo
                                {
                                    Id = subKeyName,
                                    Name = themeName,
                                    IsSelected = isSelected,
                                    IsCustom = true
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsCaptionHelper] Error enumerating custom themes: {ex.Message}");
            }

            // If nothing is selected, default to the first one
            if (!list.Exists(t => t.IsSelected) && list.Count > 0)
            {
                list[0].IsSelected = true;
            }

            return list;
        }

        public static string GetCurrentSelectedThemeId()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(HKCU_CC_PATH);
                if (key != null)
                {
                    var val = key.GetValue("CurrentSelectedTheme")?.ToString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        return val;
                    }
                }
            }
            catch { }

            return StandardThemes[0].Id;
        }

        public static bool SetWindowsCaptionTheme(string themeId)
        {
            if (string.IsNullOrWhiteSpace(themeId)) return false;

            try
            {
                using var ccKey = Registry.CurrentUser.OpenSubKey(HKCU_CC_PATH, true);
                if (ccKey == null) return false;

                // Set CurrentSelectedTheme
                ccKey.SetValue("CurrentSelectedTheme", themeId, RegistryValueKind.String);

                // Find the source theme properties (either in HKCU or HKLM)
                RegistryKey? sourceKey = null;
                try
                {
                    sourceKey = Registry.CurrentUser.OpenSubKey($@"{HKCU_THEMES_PATH}\{themeId}");
                    if (sourceKey == null)
                    {
                        sourceKey = Registry.LocalMachine.OpenSubKey($@"{HKLM_THEMES_PATH}\{themeId}");
                    }

                    if (sourceKey != null)
                    {
                        CopyProperty(sourceKey, ccKey, "CaptionColor", "CaptionColor");
                        CopyProperty(sourceKey, ccKey, "CaptionTransparency", "CaptionOpacity");
                        CopyProperty(sourceKey, ccKey, "CaptionSize", "CaptionSize");
                        CopyProperty(sourceKey, ccKey, "CaptionStyle", "CaptionFontStyle");
                        CopyProperty(sourceKey, ccKey, "CaptionEffects", "CaptionEdgeEffect");
                        CopyProperty(sourceKey, ccKey, "BackgroundColor", "BackgroundColor");
                        CopyProperty(sourceKey, ccKey, "BackgroundTransparency", "BackgroundOpacity");
                        CopyProperty(sourceKey, ccKey, "WindowColor", "RegionColor");
                        CopyProperty(sourceKey, ccKey, "WindowTransparency", "RegionOpacity");
                    }
                }
                finally
                {
                    sourceKey?.Dispose();
                }

                // Broadcast setting change so Windows OS and MediaPlayerElement reload caption styling
                BroadcastCaptionSettingChange();

                // Refresh active subtitles presentation without destroying the track selection
                RefreshCurrentSubtitlesPresentation();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsCaptionHelper] SetWindowsCaptionTheme failed: {ex.Message}");
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string? lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        private const int HWND_BROADCAST = 0xffff;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public static void BroadcastCaptionSettingChange()
        {
            try
            {
                SendMessageTimeout(
                    (IntPtr)HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    UIntPtr.Zero,
                    null,
                    SMTO_ABORTIFHUNG,
                    1000,
                    out _);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsCaptionHelper] BroadcastCaptionSettingChange error: {ex.Message}");
            }
        }

        private static void CopyProperty(RegistryKey src, RegistryKey dst, string srcProp, string dstProp)
        {
            var val = src.GetValue(srcProp);
            if (val != null)
            {
                dst.SetValue(dstProp, val);
            }
        }

        public static void RefreshCurrentSubtitlesPresentation()
        {
            try
            {
                var playback = AppServices.PlaybackViewModel?.Session;
                if (playback != null)
                {
                    int activeIndex = playback.GetActiveSubtitleTrackIndex();
                    if (activeIndex >= 0)
                    {
                        playback.SetSubtitleTrack(activeIndex);
                    }
                }
            }
            catch { }
        }

        public static async void OpenWindowsCaptionSettings()
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:easeofaccess-closedcaptioning"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsCaptionHelper] Failed to open Windows caption settings: {ex.Message}");
            }
        }
    }
}
