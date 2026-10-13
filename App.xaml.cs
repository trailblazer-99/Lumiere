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

        // ── Unhandled exception handlers ────────────────────────────
        this.UnhandledException += (s, e) =>
        {
            var ex = e.Exception;
            var details = ex != null
                ? $"Type: {ex.GetType().FullName}\nHResult: 0x{ex.HResult:X8}\nMessage: {ex.Message}\nStackTrace:\n{ex.StackTrace}\nInnerException: {ex.InnerException}"
                : "No Exception Object";

            _logger?.LogCritical(ex, "Unhandled UI exception: {Message}", e.Message);
            LogCrash("UI", e.Message, details);

            // Do not handle catastrophic COM or corrupted-state errors to avoid zombie compositor freezes
            if (ex is OutOfMemoryException or AccessViolationException or System.Runtime.InteropServices.SEHException)
            {
                e.Handled = false;
                return;
            }
            if (ex is System.Runtime.InteropServices.COMException comEx &&
                ((uint)comEx.HResult is 0x8000FFFF /* E_UNEXPECTED */ or 0x887A0005 /* DXGI_ERROR_DEVICE_REMOVED */))
            {
                e.Handled = false;
                return;
            }
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
    private static System.Threading.CancellationTokenSource? _startupCts = new();

    public static void CancelBackgroundTasks()
    {
        try
        {
            _startupCts?.Cancel();
            _startupCts?.Dispose();
            _startupCts = null;
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

    private static void LogCrash(string category, string? message, string details)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{category}] {message}\n{details}\n\n";
        System.Diagnostics.Debug.WriteLine(logEntry);

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

            var token = _startupCts?.Token ?? System.Threading.CancellationToken.None;

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
