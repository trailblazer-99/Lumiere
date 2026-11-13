using System;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace LumiereMediaPlayer;

public partial class App : Application
{
    /// <summary>Gets the application-wide DI service provider.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public static FrameworkElement MainWindowContent { get; private set; } = null!;
    public static MainWindow? MainWindowInstance { get; internal set; }
    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcher { get; private set; }

    private Window? _window;
    private ILogger<App>? _logger;

    public App()
    {
        InitializeComponent();

        // ── Build the DI container ──────────────────────────────────
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLumiereServices();
        Services = serviceCollection.BuildServiceProvider();

        _logger = Services.GetService<ILogger<App>>();

        // First-chance COMException diagnostic logger
        int firstChanceComCount = 0;
        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            if (e.Exception is System.Runtime.InteropServices.COMException comEx)
            {
                int hr = comEx.HResult;
                // Suppress expected internal COM queries (0x80004002 E_NOINTERFACE) and XAML resource mutation busy/fail (0x800F0902)
                if (hr == unchecked((int)0x80004002) || hr == unchecked((int)0x800F0902))
                {
                    return;
                }

                int count = System.Threading.Interlocked.Increment(ref firstChanceComCount);
                if (count <= 30)
                {
                    var threadStack = new System.Diagnostics.StackTrace(false);
                    var details = $"[FIRST-CHANCE COMException #{count}] HResult: 0x{comEx.HResult:X8}, Message: {comEx.Message}\nSource: {comEx.Source}\nTargetSite: {comEx.TargetSite}\nThreadStack:\n{threadStack}\n";
                    System.Diagnostics.Debug.WriteLine(details);
                    var logText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {details}\n";
                    Task.Run(() =>
                    {
                        try
                        {
                            var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LumiereMediaPlayer");
                            var comLogPath = System.IO.Path.Combine(dir, "com_exceptions.log");
                            lock (_comLogLock)
                            {
                                System.IO.Directory.CreateDirectory(dir);
                                if (System.IO.File.Exists(comLogPath) && new System.IO.FileInfo(comLogPath).Length >= 2 * 1024 * 1024)
                                {
                                    System.IO.File.Move(comLogPath, comLogPath + ".old", overwrite: true);
                                }
                                System.IO.File.AppendAllText(comLogPath, logText);
                            }
                        }
                        catch { }
                    });
                }
            }
        };

        // ── Unhandled exception handlers ────────────────────────────
        this.UnhandledException += (s, e) =>
        {
            var ex = e.Exception;
            var details = ex != null
                ? $"Type: {ex.GetType().FullName}\nHResult: 0x{ex.HResult:X8}\nMessage: {ex.Message}\nStackTrace:\n{ex.StackTrace}\nInnerException: {ex.InnerException}"
                : "No Exception Object";

            _logger?.LogCritical(ex, "Unhandled UI exception: {Message}", e.Message);
            LogCrash("UI", e.Message, details);

            // Guard against masking catastrophic failures (unrecoverable layout cycle, heap corruption, access violation, OOM)
            // that cause permanent black-screen "zombie" states where the window stops drawing and hangs.
            bool isCatastrophic = ex is OutOfMemoryException
                || ex is AccessViolationException
                || ex is LayoutCycleException
                || (ex != null && (ex.HResult == unchecked((int)0x80004005) || ex.HResult == unchecked((int)0x88980800))
                    && ex.Message?.Contains("layout cycle", StringComparison.OrdinalIgnoreCase) == true)
                || (ex != null && ((uint)ex.HResult == 0xC0000374 || ex.Message?.Contains("heap corruption", StringComparison.OrdinalIgnoreCase) == true));

            if (isCatastrophic)
            {
                e.Handled = false;
                TerminateProcessCleanly();
                return;
            }

            // For transient recoverable COMExceptions (RPC disconnected, E_UNEXPECTED, E_FAIL) or UI exceptions,
            // allow the WinUI compositor to recover gracefully on subsequent dispatch frames.
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            var details = ex != null
                ? $"Type: {ex.GetType().FullName}\nHResult: 0x{ex.HResult:X8}\nMessage: {ex.Message}\nStackTrace:\n{ex.StackTrace}\nInnerException: {ex.InnerException}"
                : (e.ExceptionObject?.ToString() ?? "No Exception Object");

            _logger?.LogCritical("Unhandled AppDomain exception: {Exception}", details);
            LogCrash("AppDomain", "Unhandled Domain Exception", details);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            e.SetObserved();
            var ex = e.Exception;
            var details = ex != null
                ? $"Type: {ex.GetType().FullName}\nHResult: 0x{ex.HResult:X8}\nMessage: {ex.Message}\nStackTrace:\n{ex.StackTrace}\nInnerException: {ex.InnerException}"
                : "No Exception Object";

            _logger?.LogWarning(ex, "Unobserved task exception");
            LogCrash("Task", "Unobserved Task Exception", details);
        };
    }

    private static readonly object _crashLogLock = new();
    private static readonly object _comLogLock = new();
    private static readonly object _startupCtsLock = new();
    private static System.Threading.CancellationTokenSource? _startupCts = new();

    public static void CancelBackgroundTasks()
    {
        System.Threading.CancellationTokenSource? ctsToDispose;
        lock (_startupCtsLock)
        {
            ctsToDispose = _startupCts;
            _startupCts = null;
        }

        try
        {
            ctsToDispose?.Cancel();
            ctsToDispose?.Dispose();
        }
        catch { }
    }

    public static void ClearWindowReferences()
    {
        CancelBackgroundTasks();
        MainWindowInstance = null;
        MainWindowContent = null!;
    }

    /// <summary>
    /// Gracefully initiates WinUI application shutdown and guarantees swift process termination
    /// after a brief grace period so no background tasks, COM objects, or zombie processes linger.
    /// </summary>
    public static void TerminateProcessCleanly()
    {
        try
        {
            if (MainDispatcher != null && !MainDispatcher.HasThreadAccess)
            {
                MainDispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try { Current?.Exit(); } catch { }
                });
            }
            else
            {
                try { Current?.Exit(); } catch { }
            }
        }
        catch { }

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(850).ConfigureAwait(false);
            }
            catch { }

            try
            {
                Environment.Exit(0);
            }
            catch { }
        });
    }

    public static void LogCrash(string category, string? message, string details)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{category}] {message}\n{details}\n\n";
        System.Diagnostics.Debug.WriteLine(logEntry);

        // Offload disk I/O to background task so crashes or exceptions do not block UI thread message pump
        Task.Run(() =>
        {
            try
            {
                lock (_crashLogLock)
                {
                    string[] candidateDirs = [
                        System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "LumiereMediaPlayer"),
                        System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "LumiereMediaPlayer")
                    ];

                    foreach (var dir in candidateDirs)
                    {
                        try
                        {
                            System.IO.Directory.CreateDirectory(dir);
                            var filePath = System.IO.Path.Combine(dir, "crash.txt");
                            var fileInfo = new System.IO.FileInfo(filePath);
                            if (fileInfo.Exists && fileInfo.Length > 2 * 1024 * 1024)
                            {
                                System.IO.File.Move(filePath, System.IO.Path.Combine(dir, "crash.bak.txt"), true);
                            }
                            System.IO.File.AppendAllText(filePath, logEntry);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        });
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // Kick off history load immediately in parallel so it's ready when HomePage renders
            var historyLoadTask = AppServices.History.LoadHistoryAsync();

            var mainWindow = new MainWindow();
            _window = mainWindow;
            MainWindowInstance = mainWindow;
            MainWindowContent = (FrameworkElement)_window.Content;

            // Activate window immediately so UI renders without synchronous delays on the critical path
            _window.Activate();

            // Apply accessibility settings lazily after first window activation to avoid blocking first frame
            MainDispatcher?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                try { AccessibilityHelper.Apply(AppServices.Settings.Current); } catch (Exception ex) { _logger?.LogWarning(ex, "Failed to apply accessibility settings"); }
            });

            System.Threading.CancellationToken token;
            lock (_startupCtsLock)
            {
                token = _startupCts?.Token ?? System.Threading.CancellationToken.None;
            }

            // Background initialization after window is visible on screen
            _ = Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;

                    // Fast local disk operations
                    await historyLoadTask;
                    if (token.IsCancellationRequested) return;

                    await LumiereMediaPlayer.Services.MediaLibraryService.LoadLibraryAsync();
                    if (token.IsCancellationRequested) return;

                    AudioPipelineHelper.CleanupTempTranscodedFiles();

                    // Non-critical network sync deferred slightly to prevent network/socket contention
                    await Task.Delay(1500, token);
                    if (token.IsCancellationRequested) return;

                    await AppServices.WatchmodeSync.SyncLibraryAsync();
                    if (token.IsCancellationRequested) return;

                    if (AppServices.Settings.Current.AutomaticLibraryScan)
                    {
                        await Task.Delay(2500, token);
                        if (token.IsCancellationRequested) return;

                        await LumiereMediaPlayer.Services.MediaLibraryService.ScanAllLibraryFoldersAsync(token);
                    }

                    // User-controlled background update check: only runs if explicitly toggled on by user and not on authoring device
                    if (AppServices.Settings.Current.AutoCheckForUpdates && !UpdateService.IsDevelopmentDevice)
                    {
                        await Task.Delay(4000, token);
                        if (!token.IsCancellationRequested)
                        {
                            var updateInfo = await UpdateService.CheckForUpdatesAsync();
                            if (updateInfo.IsUpdateAvailable && !updateInfo.IsDevelopmentDevice)
                            {
                                MainDispatcher?.TryEnqueue(() =>
                                {
                                    mainWindow.ShowUpdateNotification(updateInfo);
                                    mainWindow.ShowBottomToast(
                                        "Update available",
                                        $"v{updateInfo.LatestVersion} is available to install",
                                        "\uE896");
                                });
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Clean cancellation on app exit
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Background initialization failed");
                }
            }, token);
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "OnLaunched failed");
            LogCrash("OnLaunched", ex.Message, ex.ToString());
            // Do not call Activate() on a corrupt/failed window instance
        }
    }
}
