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
    public static MainWindow? MainWindowInstance { get; private set; }
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
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        var crashLogDir = System.IO.Path.Combine(appData, "LumiereMediaPlayer");
        System.IO.Directory.CreateDirectory(crashLogDir);
        var crashLogPath = System.IO.Path.Combine(crashLogDir, "crash.txt");

        this.UnhandledException += (s, e) =>
        {
            e.Handled = true;
            var exceptionStr = e.Exception?.ToString() ?? "No Exception Object";
            _logger?.LogCritical(e.Exception, "Unhandled UI exception: {Message}", e.Message);
            try { System.IO.File.AppendAllText(crashLogPath, "UI: " + exceptionStr + "\n" + e.Message + "\n"); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var exceptionStr = e.ExceptionObject?.ToString() ?? "No Exception Object";
            _logger?.LogCritical("Unhandled AppDomain exception: {Exception}", exceptionStr);
            try { System.IO.File.AppendAllText(crashLogPath, "AppDomain: " + exceptionStr + "\n"); } catch { }
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            var exceptionStr = e.Exception?.ToString() ?? "No Exception Object";
            _logger?.LogWarning(e.Exception, "Unobserved task exception");
            try { System.IO.File.AppendAllText(crashLogPath, "Task: " + exceptionStr + "\n"); } catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            var mainWindow = new MainWindow();
            _window = mainWindow;
            MainWindowInstance = mainWindow;
            MainWindowContent = (FrameworkElement)_window.Content;

            try { ThemeHelper.ApplyTheme(MainWindowContent, AppServices.Settings.Current.Theme); } catch (Exception ex) { _logger?.LogWarning(ex, "Failed to apply theme"); }
            try { ThemeHelper.ApplyAccentColor(AppServices.Settings.Current.AccentColor); } catch (Exception ex) { _logger?.LogWarning(ex, "Failed to apply accent color"); }
            try { AccessibilityHelper.Apply(AppServices.Settings.Current); } catch (Exception ex) { _logger?.LogWarning(ex, "Failed to apply accessibility settings"); }
            try { mainWindow.ApplyBackdrop(AppServices.Settings.Current.BackdropType); } catch (Exception ex) { _logger?.LogWarning(ex, "Failed to apply backdrop"); }

            _window.Activate();

            // Background initialization after window is visible on screen
            _ = Task.Run(async () =>
            {
                try
                {
                    // Fast local disk operations
                    await AppServices.History.LoadHistoryAsync();
                    await LumiereMediaPlayer.Services.SampleMediaLibrary.LoadLibraryAsync();
                    AudioPipelineHelper.CleanupTempTranscodedFiles();

                    // Non-critical network sync deferred slightly to prevent network/socket contention
                    await Task.Delay(1500);
                    await AppServices.WatchmodeSync.SyncLibraryAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Background initialization failed");
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "OnLaunched failed");
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var crashLogPath = System.IO.Path.Combine(appData, "LumiereMediaPlayer", "crash.txt");
            try { System.IO.File.AppendAllText(crashLogPath, "OnLaunched: " + ex + "\n"); } catch { }

            try { _window?.Activate(); } catch { }
        }
    }
}
