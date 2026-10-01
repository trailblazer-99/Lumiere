using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Defines the lifecycle management contract for tracking and cleanly disposing external Apple TV
/// application instances and background helper daemons (e.g. AMPLibraryAgent, AppleTV, AMPDevices).
/// </summary>
public interface IAppleTvLifecycleService : IDisposable
{
    /// <summary>
    /// Kicks off background monitoring for an Apple TV session launched from Lumiere.
    /// When the user closes Apple TV, all associated background processes will be terminated.
    /// </summary>
    Task TrackAppleTvLaunchAsync();

    /// <summary>
    /// Non-blocking notification that an Apple TV launch was triggered.
    /// </summary>
    void NotifyAppleTvLaunched();

    /// <summary>
    /// Cleans up orphaned or closed Apple TV remnants and returns the total memory reclaimed in bytes.
    /// </summary>
    /// <param name="force">If true, terminates lingering background processes even if tracking was not explicitly started.</param>
    long CleanupAppleTvRemnants(bool force = false);

    /// <summary>
    /// Cleans up remnants only if Apple TV has no visible window and Apple Music is not running.
    /// </summary>
    long CleanupIfOrphaned();

    /// <summary>
    /// Checks if a tracked session was closed or remnants are orphaned, and cleans them up.
    /// </summary>
    void CheckAndCleanupIfClosed();

    /// <summary>
    /// Determines whether Apple TV currently has an active, visible, or minimized window.
    /// </summary>
    bool IsAppleTvRunningWithWindow();
}

/// <summary>
/// Monitors Apple TV launches initiated by Lumiere and cleans up orphaned background processes
/// (such as AMPLibraryAgent.exe and lingering AppleTV.exe instances) when the user closes the app,
/// reclaiming 250+ MB of system RAM.
/// </summary>
public sealed class AppleTvLifecycleService : IAppleTvLifecycleService
{
    private static AppleTvLifecycleService? _instance;
    private static readonly object _instanceLock = new();

    /// <summary>
    /// Static singleton accessor for global service locator and non-DI helper access.
    /// </summary>
    public static AppleTvLifecycleService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new AppleTvLifecycleService();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// All background processes spawned by the Apple TV Windows App and Apple Media Platform.
    /// </summary>
    public static readonly string[] AppleTvProcessNames =
    [
        "AMPLibraryAgent",
        "AppleTV",
        "AMPDevices",
        "RestartAgent",
        "SharedHelper",
        "WMFCapabilities",
        "appdefaults",
        "defaults",
        "AppleMobileDeviceProcess",
        "AppleMobileDeviceHelper"
    ];

    private readonly object _trackingLock = new();
    private readonly object _disposalLock = new();
    private CancellationTokenSource? _trackingCts;
    private volatile bool _isTrackingActive;
    private bool _isDisposed;

    #region Win32 Window Detection Interop

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private const int DWMWA_CLOAKED = 14;

    #endregion

    /// <summary>
    /// Determines whether the specified process has any active, visible, or minimized top-level window.
    /// </summary>
    public static bool HasVisibleOrMinimizedWindow(int processId)
    {
        bool found = false;
        try
        {
            EnumWindowsProc callback = (hWnd, _) =>
            {
                if (!IsWindow(hWnd)) return true;
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == processId)
                {
                    // Minimized to taskbar (iconic)
                    if (IsIconic(hWnd))
                    {
                        found = true;
                        return false;
                    }

                    // Visible and not cloaked by Windows DWM
                    if (IsWindowVisible(hWnd))
                    {
                        int cloaked = 0;
                        int hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
                        if (hr != 0 || cloaked == 0)
                        {
                            int textLen = GetWindowTextLength(hWnd);
                            if (textLen > 0)
                            {
                                found = true;
                                return false;
                            }
                        }
                    }
                }
                return true;
            };

            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppleTvLifecycle] Window check error: {ex.Message}");
        }
        return found;
    }

    /// <summary>
    /// Determines whether Apple TV currently has an active, visible, or minimized window on screen.
    /// </summary>
    public bool IsAppleTvRunningWithWindow()
    {
        try
        {
            var processes = Process.GetProcessesByName("AppleTV");
            try
            {
                if (processes.Length == 0) return false;

                foreach (var proc in processes)
                {
                    try
                    {
                        if (!proc.HasExited && HasVisibleOrMinimizedWindow(proc.Id))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Process state may change during check
                    }
                }
            }
            finally
            {
                foreach (var proc in processes)
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    /// <summary>
    /// Checks whether any AppleTV.exe process is currently present in the process table.
    /// </summary>
    public static bool IsAppleTvProcessAlive()
    {
        try
        {
            var processes = Process.GetProcessesByName("AppleTV");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether Apple Music or iTunes is running to prevent terminating shared AMP library services.
    /// </summary>
    public static bool IsAppleMusicOrItunesRunning()
    {
        try
        {
            var am = Process.GetProcessesByName("AppleMusic");
            try
            {
                if (am.Length > 0) return true;
            }
            finally
            {
                foreach (var p in am) p.Dispose();
            }
        }
        catch { }

        try
        {
            var it = Process.GetProcessesByName("iTunes");
            try
            {
                if (it.Length > 0) return true;
            }
            finally
            {
                foreach (var p in it) p.Dispose();
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Checks whether orphaned AMPLibraryAgent or related helper processes are present.
    /// </summary>
    public static bool HasOrphanedAppleProcesses()
    {
        if (IsAppleMusicOrItunesRunning()) return false;

        try
        {
            var amp = Process.GetProcessesByName("AMPLibraryAgent");
            try
            {
                return amp.Length > 0;
            }
            finally
            {
                foreach (var p in amp) p.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Asynchronously kicks off background monitoring for an Apple TV launch.
    /// </summary>
    public async Task TrackAppleTvLaunchAsync()
    {
        NotifyAppleTvLaunched();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Signals that an Apple TV link was launched. Starts the background monitoring loop.
    /// </summary>
    public void NotifyAppleTvLaunched()
    {
        lock (_trackingLock)
        {
            if (_isDisposed) return;

            try
            {
                _trackingCts?.Cancel();
                _trackingCts?.Dispose();
            }
            catch { }

            _trackingCts = new CancellationTokenSource();
            _isTrackingActive = true;
            var token = _trackingCts.Token;

            _ = Task.Run(async () => await MonitorSessionAsync(token));
        }
    }

    private async Task MonitorSessionAsync(CancellationToken token)
    {
        AntiGravityLogger.Log("[AppleTvLifecycle] Initiated Apple TV launch detection session.");
        bool processEverSeen = false;
        var startTime = DateTime.UtcNow;
        var spinUpTimeout = TimeSpan.FromSeconds(30);

        // Phase 1: Wait up to 30 seconds for Apple TV process or window to appear
        while (!token.IsCancellationRequested && (DateTime.UtcNow - startTime) < spinUpTimeout)
        {
            if (IsAppleTvRunningWithWindow() || IsAppleTvProcessAlive())
            {
                processEverSeen = true;
                AntiGravityLogger.Log("[AppleTvLifecycle] Apple TV process detected. Active window monitoring started.");
                break;
            }

            try
            {
                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!processEverSeen)
        {
            AntiGravityLogger.Log("[AppleTvLifecycle] Apple TV was not detected within spin-up window. Checking for orphaned agents...");
            CleanupIfOrphaned();
            _isTrackingActive = false;
            return;
        }

        // Phase 2: Active session monitoring
        int consecutiveClosedCount = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool isWindowOpen = IsAppleTvRunningWithWindow();
            bool isProcessAlive = IsAppleTvProcessAlive();

            if (!isWindowOpen || !isProcessAlive)
            {
                consecutiveClosedCount++;
                // Require 2 consecutive polling cycles (~4s) to ensure sustained closure
                if (consecutiveClosedCount >= 2)
                {
                    AntiGravityLogger.Log("[AppleTvLifecycle] Apple TV window closed or process exited. Initiating remnant disposal...");
                    CleanupAppleTvRemnants(force: false);
                    _isTrackingActive = false;
                    break;
                }
            }
            else
            {
                consecutiveClosedCount = 0;
            }
        }
    }

    /// <summary>
    /// Cleans up orphaned or closed Apple TV remnants and returns the total memory reclaimed in bytes.
    /// </summary>
    public long CleanupAppleTvRemnants(bool force = false)
    {
        lock (_disposalLock)
        {
            // Safety guard: never terminate if user still has an open Apple TV window unless forced
            if (!force && IsAppleTvRunningWithWindow())
            {
                return 0;
            }

            bool appleMusicRunning = IsAppleMusicOrItunesRunning();
            long totalReclaimedBytes = 0;
            int killedCount = 0;

            foreach (var processName in AppleTvProcessNames)
            {
                // If Apple Music or iTunes is running, protect shared library daemons
                if (appleMusicRunning && !string.Equals(processName, "AppleTV", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    try
                    {
                        foreach (var proc in processes)
                        {
                            try
                            {
                                if (proc.HasExited) continue;

                                // Guard against terminating AppleTV if window was restored
                                if (string.Equals(processName, "AppleTV", StringComparison.OrdinalIgnoreCase) && !force)
                                {
                                    if (HasVisibleOrMinimizedWindow(proc.Id))
                                    {
                                        continue;
                                    }
                                }

                                long workingSet = 0;
                                try { workingSet = proc.WorkingSet64; } catch { }

                                try
                                {
                                    proc.Kill(entireProcessTree: true);
                                    proc.WaitForExit(1500);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[AppleTvLifecycle] Error terminating {proc.ProcessName}: {ex.Message}");
                                }

                                totalReclaimedBytes += workingSet;
                                killedCount++;
                            }
                            catch { }
                        }
                    }
                    finally
                    {
                        foreach (var proc in processes)
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch { }
            }

            if (killedCount > 0)
            {
                double reclaimedMb = totalReclaimedBytes / (1024.0 * 1024.0);
                AntiGravityLogger.Log($"[AppleTvLifecycle] Terminated {killedCount} Apple TV remnant process(es). Reclaimed {reclaimedMb:F1} MB RAM.");
            }

            return totalReclaimedBytes;
        }
    }

    /// <summary>
    /// Cleans up remnants only if Apple TV has no visible window and Apple Music is not running.
    /// </summary>
    public long CleanupIfOrphaned()
    {
        if (IsAppleTvRunningWithWindow() || IsAppleMusicOrItunesRunning())
        {
            return 0;
        }

        return CleanupAppleTvRemnants(force: false);
    }

    /// <summary>
    /// Checks if a tracked session was closed or remnants are orphaned, and cleans them up.
    /// </summary>
    public void CheckAndCleanupIfClosed()
    {
        if (_isTrackingActive || HasOrphanedAppleProcesses())
        {
            if (!IsAppleTvRunningWithWindow())
            {
                CleanupAppleTvRemnants(force: false);
                _isTrackingActive = false;
            }
        }
    }

    /// <summary>
    /// Releases background monitoring resources.
    /// </summary>
    public void Dispose()
    {
        lock (_trackingLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _trackingCts?.Cancel();
                _trackingCts?.Dispose();
                _trackingCts = null;
            }
            catch { }

            _isTrackingActive = false;
        }
        GC.SuppressFinalize(this);
    }
}
