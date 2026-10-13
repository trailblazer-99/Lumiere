using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Helpers;

public static class AppIconHelper
{
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

    private const int SM_CXICON = 11;
    private const int SM_CYICON = 12;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW", SetLastError = true)]
    private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_FLUSH = 0x1000;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] string ppszFileName);
    }

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetClassLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return new IntPtr(SetClassLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    private static IntPtr _currentBigHIcon = IntPtr.Zero;
    private static IntPtr _currentSmallHIcon = IntPtr.Zero;
    public static IntPtr CurrentBigHIcon => _currentBigHIcon;
    public static IntPtr CurrentSmallHIcon => _currentSmallHIcon;

    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, (IntPtr Big, IntPtr Small)> _iconHandleCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Key, double Hue)[] Palettes =
    [
        ("Blue", 206.0),
        ("Purple", 272.0),
        ("Teal", 184.0),
        ("Orange", 22.0),
        ("Red", 358.5),
        ("Pink", 323.0)
    ];

    private static readonly List<WeakReference<Image>> _registeredImages = new();

    private static Uri? _currentIconUri;
    public static Uri CurrentIconUri
    {
        get => _currentIconUri ??= new Uri($"ms-appx:///Assets/Icons/{GetCurrentVariantName()}.png");
        private set => _currentIconUri = value;
    }
    public static event EventHandler<Uri>? AppIconChanged;

    public static string GetCurrentVariantName()
    {
        var effectiveTheme = ThemeHelper.GetEffectiveElementTheme();
        string themeKey = effectiveTheme == ElementTheme.Light ? "Light" : "Dark";

        AppSettings? settings = null;
        try
        {
            if (App.Services != null)
            {
                settings = AppServices.Settings.Current;
            }
        }
        catch { }

        var accentOption = settings?.AccentColor ?? AccentColorOption.SystemDefault;
        string colorKey = GetAccentColorKey(accentOption);

        return $"Lumiere_{themeKey}_{colorKey}";
    }

    public static string? ResolveDiskPath(string fileName)
    {
        // 1. AppContext.BaseDirectory / Assets / Icons / fileName
        try
        {
            var p1 = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", fileName);
            if (File.Exists(p1)) return p1;
        }
        catch { }

        // 2. AppContext.BaseDirectory / AppX / Assets / Icons / fileName
        try
        {
            var p2 = Path.Combine(AppContext.BaseDirectory, "AppX", "Assets", "Icons", fileName);
            if (File.Exists(p2)) return p2;
        }
        catch { }

        // 3. Package installed location
        try
        {
            var p3 = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Icons", fileName);
            if (File.Exists(p3)) return p3;
        }
        catch { }

        // 4. ApplicationData / LocalFolder
        try
        {
            var p4 = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Assets", "Icons", fileName);
            if (File.Exists(p4)) return p4;
        }
        catch { }

        try
        {
            var p5 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumiereMediaPlayer", "Assets", "Icons", fileName);
            if (File.Exists(p5)) return p5;
        }
        catch { }

        return null;
    }

    public static ImageSource CreateCurrentBitmapImage(bool isSmall = false)
    {
        string variant = GetCurrentVariantName();
        string fileName = isSmall ? $"{variant}_small.png" : $"{variant}.png";
        string? diskPath = ResolveDiskPath(fileName);

        if (diskPath == null && isSmall)
        {
            fileName = $"{variant}.png";
            diskPath = ResolveDiskPath(fileName);
        }

        if (diskPath != null && File.Exists(diskPath))
        {
            var bitmap = new BitmapImage(new Uri(diskPath));
            if (!isSmall)
            {
                // Constrain large 1024 master decode size to 256px for smooth rendering and low memory footprint
                bitmap.DecodePixelWidth = 256;
                bitmap.DecodePixelType = DecodePixelType.Logical;
            }
            return bitmap;
        }

        var uriBitmap = new BitmapImage(new Uri($"ms-appx:///Assets/Icons/{fileName}"));
        if (!isSmall)
        {
            uriBitmap.DecodePixelWidth = 256;
            uriBitmap.DecodePixelType = DecodePixelType.Logical;
        }
        return uriBitmap;
    }

    public static void ApplyCurrentIcon(Image? imageControl)
    {
        if (imageControl == null) return;

        try
        {
            bool isSmall = imageControl.Width <= 64 || imageControl.Height <= 64 ||
                           (double.IsNaN(imageControl.Width) && imageControl.ActualWidth > 0 && imageControl.ActualWidth <= 64);
            var source = CreateCurrentBitmapImage(isSmall);
            imageControl.Source = source;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppIconHelper] ApplyCurrentIcon error: {ex.Message}");
        }
    }

    public static void RegisterBrandIcon(Image? imageControl)
    {
        if (imageControl == null) return;

        lock (_registeredImages)
        {
            // Prune dead references
            _registeredImages.RemoveAll(r => !r.TryGetTarget(out _));

            // Check if already registered
            bool alreadyRegistered = false;
            foreach (var r in _registeredImages)
            {
                if (r.TryGetTarget(out var target) && ReferenceEquals(target, imageControl))
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            if (!alreadyRegistered)
            {
                _registeredImages.Add(new WeakReference<Image>(imageControl));
            }
        }

        // Apply immediately
        ApplyCurrentIcon(imageControl);
    }

    public static void UpdateAppIcon(Window? window = null)
    {
        var targetWindow = window ?? App.MainWindowInstance;
        var dispatcher = targetWindow?.DispatcherQueue ?? App.MainWindowContent?.DispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => UpdateAppIcon(targetWindow));
            return;
        }

        try
        {
            string variantName = GetCurrentVariantName();
            CurrentIconUri = new Uri($"ms-appx:///Assets/Icons/{variantName}.png");

            // 1. Update Windows AppWindow icon and Win32 Taskbar & Window Class icons
            UpdateWindowIcon(variantName, targetWindow);

            // 2. Update all registered in-app brand images
            UpdateRegisteredImages();

            // 3. Fire public event for external listeners
            AppIconChanged?.Invoke(null, CurrentIconUri);

            // 4. Synchronize all shell assets (Start Menu, Taskbar, Desktop Shortcut, Search Menu) in background
            _ = Task.Run(() => SyncShellAssetsAndShortcuts(variantName));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppIconHelper] UpdateAppIcon error: {ex.Message}");
        }
    }

    public static void UpdateWindowIcon(string variantName, Window? window = null)
    {
        try
        {
            var targetWindow = window ?? App.MainWindowInstance;
            string icoFileName = $"{variantName}.ico";
            string? icoPath = ResolveDiskPath(icoFileName);

            if (icoPath == null || !File.Exists(icoPath))
            {
                icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.ico");
                if (!File.Exists(icoPath))
                {
                    try
                    {
                        var pPkg = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Square44x44Logo.ico");
                        if (File.Exists(pPkg)) icoPath = pPkg;
                    }
                    catch { }
                }
            }

            if (File.Exists(icoPath))
            {
                // WinUI 3 AppWindow API
                if (targetWindow?.AppWindow != null)
                {
                    try
                    {
                        targetWindow.AppWindow.SetIcon(icoPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AppIconHelper] AppWindow.SetIcon error: {ex.Message}");
                    }
                }

                // Win32 HWND direct message + Window Class Icon + Frame sync for Windows Explorer Taskbar
                if (targetWindow != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(targetWindow);
                    if (hwnd != IntPtr.Zero)
                    {
                        int cxBig = GetSystemMetrics(SM_CXICON);
                        if (cxBig <= 0) cxBig = 32;
                        int cyBig = GetSystemMetrics(SM_CYICON);
                        if (cyBig <= 0) cyBig = 32;

                        int cxSmall = GetSystemMetrics(SM_CXSMICON);
                        if (cxSmall <= 0) cxSmall = 16;
                        int cySmall = GetSystemMetrics(SM_CYSMICON);
                        if (cySmall <= 0) cySmall = 16;

                        string cacheKey = $"{variantName}_{cxBig}x{cyBig}_{cxSmall}x{cySmall}";
                        IntPtr hBig = IntPtr.Zero;
                        IntPtr hSmall = IntPtr.Zero;

                        lock (_cacheLock)
                        {
                            if (_iconHandleCache.TryGetValue(cacheKey, out var cached))
                            {
                                hBig = cached.Big;
                                hSmall = cached.Small;
                            }
                            else
                            {
                                hBig = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, cxBig, cyBig, LR_LOADFROMFILE);
                                hSmall = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, cxSmall, cySmall, LR_LOADFROMFILE);

                                if (hBig != IntPtr.Zero || hSmall != IntPtr.Zero)
                                {
                                    _iconHandleCache[cacheKey] = (hBig, hSmall);
                                }
                            }
                        }

                        if (hBig != IntPtr.Zero)
                        {
                            _currentBigHIcon = hBig;
                            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
                            SetClassLongPtr(hwnd, GCLP_HICON, hBig);
                        }

                        if (hSmall != IntPtr.Zero)
                        {
                            _currentSmallHIcon = hSmall;
                            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
                            SetClassLongPtr(hwnd, GCLP_HICONSM, hSmall);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppIconHelper] UpdateWindowIcon error: {ex.Message}");
        }
    }

    public static void RefreshTaskbarIcon(Window? window = null)
    {
        try
        {
            var targetWindow = window ?? App.MainWindowInstance;
            if (targetWindow == null) return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(targetWindow);
            if (hwnd == IntPtr.Zero) return;

            IntPtr hBig = _currentBigHIcon;
            IntPtr hSmall = _currentSmallHIcon;

            if (hBig == IntPtr.Zero || hSmall == IntPtr.Zero)
            {
                UpdateWindowIcon(GetCurrentVariantName(), targetWindow);
                return;
            }

            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
            SetClassLongPtr(hwnd, GCLP_HICON, hBig);

            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
            SetClassLongPtr(hwnd, GCLP_HICONSM, hSmall);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppIconHelper] RefreshTaskbarIcon error: {ex.Message}");
        }
    }

    private static void UpdateRegisteredImages()
    {
        lock (_registeredImages)
        {
            _registeredImages.RemoveAll(r => !r.TryGetTarget(out _));

            foreach (var r in _registeredImages)
            {
                if (r.TryGetTarget(out var img) && img != null)
                {
                    img.DispatcherQueue?.TryEnqueue(() =>
                    {
                        ApplyCurrentIcon(img);
                    });
                }
            }
        }
    }

    private static string GetAccentColorKey(AccentColorOption option)
    {
        return option switch
        {
            AccentColorOption.Blue => "Blue",
            AccentColorOption.Purple => "Purple",
            AccentColorOption.Teal => "Teal",
            AccentColorOption.Orange => "Orange",
            AccentColorOption.Red => "Red",
            AccentColorOption.Pink => "Pink",
            _ => GetClosestColorKey(ThemeHelper.GetAccentPalette(option).Default)
        };
    }

    private static string GetClosestColorKey(Color color)
    {
        double hue = CalculateHue(color);
        string bestKey = "Blue";
        double minDiff = 360.0;

        foreach (var (key, targetHue) in Palettes)
        {
            double diff = Math.Abs(hue - targetHue);
            if (diff > 180.0) diff = 360.0 - diff;
            if (diff < minDiff)
            {
                minDiff = diff;
                bestKey = key;
            }
        }

        return bestKey;
    }

    private static double CalculateHue(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double diff = max - min;

        if (diff < 0.0001) return 0.0;

        double h;
        if (Math.Abs(max - r) < 0.0001)
            h = (g - b) / diff + (g < b ? 6.0 : 0.0);
        else if (Math.Abs(max - g) < 0.0001)
            h = (b - r) / diff + 2.0;
        else
            h = (r - g) / diff + 4.0;

        return h * 60.0;
    }

    // ── Shell Asset & Shortcut Synchronization ────────────────────────

    public static void SyncShellAssetsAndShortcuts(string variantName)
    {
        try
        {
            string icoFileName = $"{variantName}.ico";
            string pngFileName = $"{variantName}.png";
            string smallPngFileName = $"{variantName}_small.png";

            string? icoPath = ResolveDiskPath(icoFileName);
            string? pngPath = ResolveDiskPath(pngFileName);
            string? smallPngPath = ResolveDiskPath(smallPngFileName);

            if (icoPath == null || !File.Exists(icoPath))
            {
                icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.ico");
                if (!File.Exists(icoPath))
                {
                    try
                    {
                        var pPkg = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Square44x44Logo.ico");
                        if (File.Exists(pPkg)) icoPath = pPkg;
                    }
                    catch { }
                }
            }

            if (!File.Exists(icoPath))
            {
                Debug.WriteLine($"[AppIconHelper] Cannot sync shell assets: {icoFileName} not found");
                return;
            }

            var frames = ExtractIcoFrames(icoPath);

            // 1. Gather candidate writable asset directories across app execution and application data
            var assetDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string baseDir = Path.Combine(AppContext.BaseDirectory, "Assets");
                if (Directory.Exists(baseDir)) assetDirs.Add(baseDir);
            }
            catch { }

            try
            {
                string localFolderAssets = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Assets");
                assetDirs.Add(localFolderAssets);
            }
            catch { }

            try
            {
                string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumiereMediaPlayer", "Assets");
                assetDirs.Add(localAppData);
            }
            catch { }

            // 2. Synchronize all icons and tiles into each asset directory
            foreach (var dir in assetDirs)
            {
                try
                {
                    SafeCopyFile(icoPath, Path.Combine(dir, "Square44x44Logo.ico"));
                    SafeCopyFile(icoPath, Path.Combine(dir, "LumiereIcon.ico"));

                    if (frames.TryGetValue(16, out var f16))
                    {
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-16_altform-unplated.png"), f16);
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-16_altform-lightunplated.png"), f16);
                    }
                    if (frames.TryGetValue(20, out var f20))
                    {
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-20_altform-unplated.png"), f20);
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-20_altform-lightunplated.png"), f20);
                    }
                    if (frames.TryGetValue(24, out var f24))
                    {
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-24_altform-unplated.png"), f24);
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-24_altform-lightunplated.png"), f24);
                    }
                    if (frames.TryGetValue(32, out var f32))
                    {
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-32_altform-unplated.png"), f32);
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-32_altform-lightunplated.png"), f32);
                    }
                    if (frames.TryGetValue(48, out var f48))
                    {
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-48_altform-unplated.png"), f48);
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-48_altform-lightunplated.png"), f48);
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.png"), f48);
                        SafeWriteBytes(Path.Combine(dir, "StoreLogo.png"), f48);
                        SafeWriteBytes(Path.Combine(dir, "LockScreenLogo.scale-200.png"), f48);
                    }
                    if (frames.TryGetValue(64, out var f64))
                    {
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.scale-200.png"), f64);
                    }
                    if (frames.TryGetValue(128, out var f128))
                    {
                        SafeWriteBytes(Path.Combine(dir, "Square150x150Logo.png"), f128);
                    }
                    if (frames.TryGetValue(256, out var f256))
                    {
                        SafeWriteBytes(Path.Combine(dir, "Square150x150Logo.scale-200.png"), f256);
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-256_altform-unplated.png"), f256);
                        SafeWriteBytes(Path.Combine(dir, "Square44x44Logo.targetsize-256_altform-lightunplated.png"), f256);
                        SafeWriteBytes(Path.Combine(dir, "LumiereIcon.png"), f256);
                    }

                    if (pngPath != null && File.Exists(pngPath))
                    {
                        SafeCopyFile(pngPath, Path.Combine(dir, "SplashScreen.png"));
                        SafeCopyFile(pngPath, Path.Combine(dir, "SplashScreen.scale-200.png"));
                        SafeCopyFile(pngPath, Path.Combine(dir, "Wide310x150Logo.png"));
                        SafeCopyFile(pngPath, Path.Combine(dir, "Wide310x150Logo.scale-200.png"));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AppIconHelper] Asset folder sync failed for {dir}: {ex.Message}");
                }
            }

            string? targetExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(targetExePath) || !File.Exists(targetExePath))
            {
                targetExePath = Path.Combine(AppContext.BaseDirectory, "LumiereMediaPlayer.exe");
            }

            if (File.Exists(targetExePath))
            {
                // Only CREATE new shortcuts if user has explicitly opted in
                bool createShortcuts = false;
                try
                {
                    createShortcuts = AppServices.Settings?.Current?.CreateShellShortcuts ?? false;
                }
                catch { }

                // User Desktop (e.g. OneDrive Desktop or regular Desktop)
                try
                {
                    string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if (!string.IsNullOrEmpty(userDesktop) && Directory.Exists(userDesktop))
                    {
                        string desktopLnk = Path.Combine(userDesktop, "Lumière Media Player.lnk");
                        if (createShortcuts)
                        {
                            UpdateOrCreateShortcut(desktopLnk, targetExePath, icoPath);
                        }
                        UpdateExistingShortcuts(userDesktop, targetExePath, icoPath);
                    }
                }
                catch { }

                // Local UserProfile Desktop (if distinct from redirected Desktop)
                try
                {
                    string altDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
                    if (Directory.Exists(altDesktop))
                    {
                        UpdateExistingShortcuts(altDesktop, targetExePath, icoPath);
                    }
                }
                catch { }

                // Common Desktop
                try
                {
                    string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                    if (!string.IsNullOrEmpty(commonDesktop) && Directory.Exists(commonDesktop))
                    {
                        UpdateExistingShortcuts(commonDesktop, targetExePath, icoPath);
                    }
                }
                catch { }

                // User Start Menu Programs
                try
                {
                    string startPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                    if (Directory.Exists(startPrograms))
                    {
                        if (createShortcuts)
                        {
                            string startMenuLnk = Path.Combine(startPrograms, "Lumière Media Player.lnk");
                            UpdateOrCreateShortcut(startMenuLnk, targetExePath, icoPath);
                        }
                        UpdateExistingShortcuts(startPrograms, targetExePath, icoPath);
                    }
                }
                catch { }

                // Common Start Menu Programs
                try
                {
                    string commonStart = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
                    if (Directory.Exists(commonStart))
                    {
                        UpdateExistingShortcuts(commonStart, targetExePath, icoPath);
                    }
                }
                catch { }

                // Taskbar Pinned Shortcuts
                try
                {
                    string taskbar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                    if (Directory.Exists(taskbar))
                    {
                        UpdateExistingShortcuts(taskbar, targetExePath, icoPath);
                    }
                }
                catch { }
            }

            // 4. Invalidate Windows Shell icon caches (Start Menu, Taskbar, Desktop, Search)
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            Debug.WriteLine($"[AppIconHelper] Successfully synchronized shell assets and shortcuts for variant '{variantName}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppIconHelper] SyncShellAssetsAndShortcuts error: {ex.Message}");
        }
    }

    private static Dictionary<int, byte[]> ExtractIcoFrames(string icoPath)
    {
        var frames = new Dictionary<int, byte[]>();
        try
        {
            if (!File.Exists(icoPath)) return frames;
            byte[] icoBytes = File.ReadAllBytes(icoPath);
            if (icoBytes.Length < 6) return frames;

            ushort count = BitConverter.ToUInt16(icoBytes, 4);
            for (int i = 0; i < count; i++)
            {
                int entryOffset = 6 + (i * 16);
                if (entryOffset + 16 > icoBytes.Length) break;

                int width = icoBytes[entryOffset];
                if (width == 0) width = 256;

                uint imgLen = BitConverter.ToUInt32(icoBytes, entryOffset + 8);
                uint imgOffset = BitConverter.ToUInt32(icoBytes, entryOffset + 12);

                if (imgOffset + imgLen <= icoBytes.Length)
                {
                    byte[] frame = new byte[imgLen];
                    Array.Copy(icoBytes, imgOffset, frame, 0, imgLen);
                    frames[width] = frame;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppIconHelper] ExtractIcoFrames error: {ex.Message}");
        }
        return frames;
    }

    private static void SafeWriteBytes(string targetPath, byte[] data)
    {
        try
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(targetPath))
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
            }

            File.WriteAllBytes(targetPath, data);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppIconHelper] SafeWriteBytes failed for {targetPath}: {ex.Message}");
        }
    }

    private static void SafeCopyFile(string sourcePath, string targetPath)
    {
        try
        {
            if (!File.Exists(sourcePath)) return;
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(targetPath))
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppIconHelper] SafeCopyFile failed for {targetPath}: {ex.Message}");
        }
    }

    private static void UpdateOrCreateShortcut(string shortcutPath, string targetExePath, string iconPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            var persistFile = (IPersistFile)link;

            if (File.Exists(shortcutPath))
            {
                try
                {
                    persistFile.Load(shortcutPath, 0);
                }
                catch { }
            }

            link.SetPath(targetExePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetExePath) ?? "");
            link.SetIconLocation(iconPath, 0);
            link.SetDescription("Lumière Media Player");

            persistFile.Save(shortcutPath, true);
            Debug.WriteLine($"[AppIconHelper] Updated shortcut at {shortcutPath} -> {iconPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppIconHelper] Failed to update shortcut at {shortcutPath}: {ex.Message}");
        }
    }

    private static void UpdateExistingShortcuts(string directory, string targetExePath, string iconPath)
    {
        try
        {
            if (!Directory.Exists(directory)) return;

            var lnkFiles = Directory.GetFiles(directory, "*.lnk", SearchOption.AllDirectories);
            foreach (var lnk in lnkFiles)
            {
                string fileName = Path.GetFileName(lnk);
                if (fileName.Contains("Lumiere", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("Lumière", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateOrCreateShortcut(lnk, targetExePath, iconPath);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppIconHelper] Error searching shortcuts in {directory}: {ex.Message}");
        }
    }
}
