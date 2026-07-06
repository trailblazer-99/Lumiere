using FluentMediaPlayer.Helpers;
using FluentMediaPlayer.Services;
using Microsoft.UI.Xaml;

namespace FluentMediaPlayer;

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
        var crashLogDir = System.IO.Path.Combine(appData, "FluentMediaPlayer");
        System.IO.Directory.CreateDirectory(crashLogDir);
        var crashLogPath = System.IO.Path.Combine(crashLogDir, "crash.txt");

        this.UnhandledException += (s, e) => {
            var exceptionStr = e.Exception?.ToString() ?? "No Exception Object";
            System.IO.File.AppendAllText(crashLogPath, "UI: " + exceptionStr + "\n" + e.Message + "\n");
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            var exceptionStr = e.ExceptionObject?.ToString() ?? "No Exception Object";
            System.IO.File.AppendAllText(crashLogPath, "AppDomain: " + exceptionStr + "\n");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => {
            var exceptionStr = e.Exception?.ToString() ?? "No Exception Object";
            System.IO.File.AppendAllText(crashLogPath, "Task: " + exceptionStr + "\n");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Settings.Load();
        _ = AppServices.History.LoadHistoryAsync();
        _ = AppServices.WatchmodeSync.SyncLibraryAsync();
        MainDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        
        // Load the persistent media library in the background
        _ = FluentMediaPlayer.Services.SampleMediaLibrary.LoadLibraryAsync();
        
        var mainWindow = new MainWindow();
        _window = mainWindow;
        MainWindowInstance = mainWindow;
        MainWindowContent = (FrameworkElement)_window.Content;
        ThemeHelper.ApplyTheme(MainWindowContent, AppServices.Settings.Current.Theme);
        ThemeHelper.ApplyAccentColor(AppServices.Settings.Current.AccentColor);
        AccessibilityHelper.Apply(AppServices.Settings.Current);
        mainWindow.ApplyBackdrop(AppServices.Settings.Current.BackdropType);
        _window.Activate();
    }
}
