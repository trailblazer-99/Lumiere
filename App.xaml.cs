using System;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Services;
using Microsoft.UI.Xaml;

namespace LumiereMediaPlayer;

public partial class App : Application
{
    public static FrameworkElement MainWindowContent { get; private set; } = null!;
    public static MainWindow? MainWindowInstance { get; private set; }
    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcher { get; private set; }

    private Window? _window;

    public App()
    {
        InitializeComponent();
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        var crashLogDir = System.IO.Path.Combine(appData, "LumiereMediaPlayer");
        System.IO.Directory.CreateDirectory(crashLogDir);
        var crashLogPath = System.IO.Path.Combine(crashLogDir, "crash.txt");

        this.UnhandledException += (s, e) => {
            e.Handled = true;
            var exceptionStr = e.Exception?.ToString() ?? "No Exception Object";
            try { System.IO.File.AppendAllText(crashLogPath, "UI: " + exceptionStr + "\n" + e.Message + "\n"); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            var exceptionStr = e.ExceptionObject?.ToString() ?? "No Exception Object";
            try { System.IO.File.AppendAllText(crashLogPath, "AppDomain: " + exceptionStr + "\n"); } catch { }
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => {
            var exceptionStr = e.Exception?.ToString() ?? "No Exception Object";
            try { System.IO.File.AppendAllText(crashLogPath, "Task: " + exceptionStr + "\n"); } catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            try { AppServices.Settings.Load(); } catch { }
            _ = AppServices.History.LoadHistoryAsync();
            _ = AppServices.WatchmodeSync.SyncLibraryAsync();
            MainDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            
            // Load the persistent media library in the background
            _ = LumiereMediaPlayer.Services.SampleMediaLibrary.LoadLibraryAsync();
            
            var mainWindow = new MainWindow();
            _window = mainWindow;
            MainWindowInstance = mainWindow;
            MainWindowContent = (FrameworkElement)_window.Content;

            try { ThemeHelper.ApplyTheme(MainWindowContent, AppServices.Settings.Current.Theme); } catch { }
            try { ThemeHelper.ApplyAccentColor(AppServices.Settings.Current.AccentColor); } catch { }
            try { AccessibilityHelper.Apply(AppServices.Settings.Current); } catch { }
            try { mainWindow.ApplyBackdrop(AppServices.Settings.Current.BackdropType); } catch { }

            _window.Activate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App.OnLaunched] error: {ex}");
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var crashLogPath = System.IO.Path.Combine(appData, "LumiereMediaPlayer", "crash.txt");
            try { System.IO.File.AppendAllText(crashLogPath, "OnLaunched: " + ex + "\n"); } catch { }

            try { _window?.Activate(); } catch { }
        }
    }
}
