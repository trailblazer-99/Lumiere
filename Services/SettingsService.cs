using LumiereMediaPlayer.Models;
using System.Threading.Tasks;
using Windows.Storage;

namespace LumiereMediaPlayer.Services;

public sealed class SettingsService
{
    private const string ThemeKey = "Theme";
    private const string FoldersKey = "LibraryFolders";

    // Playback
    private const string AutoplayOnLaunchKey = "AutoplayOnLaunch";
    private const string ResumePlaybackPositionKey = "ResumePlaybackPosition";
    private const string SkipForwardIntervalKey = "SkipForwardInterval";
    private const string SkipBackwardIntervalKey = "SkipBackwardInterval";
    private const string AutoAdvanceToNextTrackKey = "AutoAdvanceToNextTrack";
    private const string RememberLastPlayedTrackKey = "RememberLastPlayedTrack";
    private const string CrossfadeEnabledKey = "CrossfadeEnabled";
    private const string CrossfadeDurationKey = "CrossfadeDuration";

    // Audio
    private const string EqualizerPresetKey = "EqualizerPreset";
    private const string DefaultVolumeKey = "DefaultVolume";

    // Video
    private const string DefaultAspectRatioKey = "DefaultAspectRatio";

    // Appearance
    private const string BackdropTypeKey = "BackdropType";
    private const string AccentColorKey = "AccentColor";
    private const string AlwaysShowTransportBarKey = "AlwaysShowTransportBar";

    // Controls & Interface
    private const string ShowOpenFilesOnHomeKey = "ShowOpenFilesOnHome";
    private const string OpenFilePositionCornerKey = "OpenFilePositionCorner";

    // Window State
    private const string WindowWidthKey = "WindowWidth";
    private const string WindowHeightKey = "WindowHeight";
    private const string WindowIsMaximizedKey = "WindowIsMaximized";

    // Library
    private const string AutomaticLibraryScanKey = "AutomaticLibraryScan";

    // Privacy
    private const string RememberPlaybackPositionPerTrackKey = "RememberPlaybackPositionPerTrack";

    // Accessibility
    private const string HighContrastModeKey = "HighContrastMode";
    private const string TextScaleKey = "TextScale";
    private const string ReduceMotionKey = "ReduceMotion";
    private const string ScreenReaderOptimizationKey = "ScreenReaderOptimization";
    private const string CaptionsAlwaysOnKey = "CaptionsAlwaysOn";
    private const string VisualNotificationsForSoundKey = "VisualNotificationsForSound";
    private const string KeyboardNavigationHighlightKey = "KeyboardNavigationHighlight";
    private const string FocusIndicatorThicknessKey = "FocusIndicatorThickness";
    private const string AutoReadControlsKey = "AutoReadControls";
    private const string LargerClickTargetsKey = "LargerClickTargets";
    private const string ColorBlindModeKey = "ColorBlindMode";

    // AI Features
    private const string AiLyricsTranslationEnabledKey = "AiLyricsTranslationEnabled";
    private const string AiTranslationTargetLanguageKey = "AiTranslationTargetLanguage";
    private const string AiSemanticSearchEnabledKey = "AiSemanticSearchEnabled";
    private const string GeminiApiKeyKey = "GeminiApiKey";
    private const string UseLocalAiKey = "UseLocalAi";
    private const string OllamaModelNameKey = "OllamaModelName";
    private const string AiEqualizerMatcherEnabledKey = "AiEqualizerMatcherEnabled";
    private const string VoiceClarityEnabledKey = "VoiceClarityEnabled";
    private const string NightModeEnabledKey = "NightModeEnabled";

    // Premium General Features Keys
    private const string SleepTimerMinutesKey = "SleepTimerMinutes";
    private const string SleepAtEndOfTrackKey = "SleepAtEndOfTrack";
    private const string CustomEqualizerGainsKey = "CustomEqualizerGains";
    private const string SelectedReverbPresetKey = "SelectedReverbPreset";

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? SettingsChanged;

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        Windows.Foundation.Collections.IPropertySet? values = null;
        try
        {
            values = ApplicationData.Current.LocalSettings.Values;
        }
        catch (InvalidOperationException)
        {
            // Unpackaged app fallback
            values = new Windows.Foundation.Collections.PropertySet();
        }

        var settingsValues = values ?? new Windows.Foundation.Collections.PropertySet();

        Current = new AppSettings
        {
            Theme = ParseEnum(settingsValues, ThemeKey, AppThemeOption.Default),
            LibraryFolders = settingsValues.TryGetValue(FoldersKey, out var fj) && fj is string fjStr
                ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(fjStr) ?? []
                : [],

            // Playback
            AutoplayOnLaunch = ReadBool(settingsValues, AutoplayOnLaunchKey, true),
            ResumePlaybackPosition = ReadBool(settingsValues, ResumePlaybackPositionKey, true),
            SkipForwardInterval = ReadInt(settingsValues, SkipForwardIntervalKey, 30),
            SkipBackwardInterval = ReadInt(settingsValues, SkipBackwardIntervalKey, 10),
            AutoAdvanceToNextTrack = ReadBool(settingsValues, AutoAdvanceToNextTrackKey, true),
            RememberLastPlayedTrack = ReadBool(settingsValues, RememberLastPlayedTrackKey, true),
            CrossfadeEnabled = ReadBool(settingsValues, CrossfadeEnabledKey, false),
            CrossfadeDuration = ReadInt(settingsValues, CrossfadeDurationKey, 3),

            // Audio
            Equalizer = ParseEnum(settingsValues, EqualizerPresetKey, EqualizerPreset.Flat),
            DefaultVolume = ReadDouble(settingsValues, DefaultVolumeKey, 100.0),

            // Video
            DefaultAspectRatio = ParseEnum(settingsValues, DefaultAspectRatioKey, AspectRatioOption.Auto),

            // Appearance
            BackdropType = ParseEnum(settingsValues, BackdropTypeKey, AppThemeBackdrop.Mica),
            AccentColor = ParseEnum(settingsValues, AccentColorKey, AccentColorOption.SystemDefault),
            AlwaysShowTransportBar = ReadBool(settingsValues, AlwaysShowTransportBarKey, false),

            // Controls & Interface
            ShowOpenFilesOnHome = ReadBool(settingsValues, ShowOpenFilesOnHomeKey, true),
            OpenFilePositionCorner = ParseEnum(settingsValues, OpenFilePositionCornerKey, OpenFileCorner.TopRight),

            // Window State
            WindowWidth = ReadDouble(settingsValues, WindowWidthKey, 1200.0),
            WindowHeight = ReadDouble(settingsValues, WindowHeightKey, 800.0),
            WindowIsMaximized = ReadBool(settingsValues, WindowIsMaximizedKey, false),

            // Library
            AutomaticLibraryScan = ReadBool(settingsValues, AutomaticLibraryScanKey, true),

            // Privacy
            RememberPlaybackPositionPerTrack = ReadBool(settingsValues, RememberPlaybackPositionPerTrackKey, true),

            // Accessibility
            HighContrastMode = ReadBool(settingsValues, HighContrastModeKey, false),
            TextScale = ReadDouble(settingsValues, TextScaleKey, 1.0),
            ReduceMotion = ReadBool(settingsValues, ReduceMotionKey, false),
            ScreenReaderOptimization = ReadBool(settingsValues, ScreenReaderOptimizationKey, false),
            CaptionsAlwaysOn = ReadBool(settingsValues, CaptionsAlwaysOnKey, false),
            VisualNotificationsForSound = ReadBool(settingsValues, VisualNotificationsForSoundKey, false),
            KeyboardNavigationHighlight = ReadBool(settingsValues, KeyboardNavigationHighlightKey, true),
            FocusIndicatorThickness = ReadInt(settingsValues, FocusIndicatorThicknessKey, 2),
            AutoReadControls = ReadBool(settingsValues, AutoReadControlsKey, false),
            LargerClickTargets = ReadBool(settingsValues, LargerClickTargetsKey, false),
            ColorBlindMode = ParseEnum(settingsValues, ColorBlindModeKey, ColorBlindMode.Off),

            // AI Features
            AiLyricsTranslationEnabled = ReadBool(settingsValues, AiLyricsTranslationEnabledKey, false),
            AiTranslationTargetLanguage = settingsValues.TryGetValue(AiTranslationTargetLanguageKey, out var aiLang) && aiLang is string sAiLang ? sAiLang : "Hindi",
            AiSemanticSearchEnabled = ReadBool(settingsValues, AiSemanticSearchEnabledKey, false),
            GeminiApiKey = Helpers.SecureStorageHelper.GetSecret("GeminiApiKey"),
            UseLocalAi = ReadBool(settingsValues, UseLocalAiKey, false),
            OllamaModelName = settingsValues.TryGetValue(OllamaModelNameKey, out var oModel) && oModel is string sOModel ? sOModel : "llama3.2",
            AiEqualizerMatcherEnabled = ReadBool(settingsValues, AiEqualizerMatcherEnabledKey, false),
            VoiceClarityEnabled = ReadBool(settingsValues, VoiceClarityEnabledKey, false),
            NightModeEnabled = ReadBool(settingsValues, NightModeEnabledKey, false),

            // Premium Features
            SleepTimerMinutes = ReadInt(settingsValues, SleepTimerMinutesKey, 0),
            SleepAtEndOfTrack = ReadBool(settingsValues, SleepAtEndOfTrackKey, false),
            CustomEqualizerGains = settingsValues.TryGetValue(CustomEqualizerGainsKey, out var eqGains) && eqGains is string sEqGains ? sEqGains : "0,0,0,0,0,0,0,0,0,0",
            SelectedReverbPreset = settingsValues.TryGetValue(SelectedReverbPresetKey, out var reverb) && reverb is string sReverb ? sReverb : "None",
        };
    }

    private Microsoft.UI.Xaml.DispatcherTimer? _saveDebounceTimer;

    public void Save()
    {
        if (App.MainDispatcher != null && App.MainDispatcher.HasThreadAccess)
        {
            if (_saveDebounceTimer == null)
            {
                _saveDebounceTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveDebounceTimer.Tick += (s, e) =>
                {
                    _saveDebounceTimer.Stop();
                    ExecuteSave();
                };
            }
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }
        else
        {
            ExecuteSave();
        }
    }

    private void ExecuteSave()
    {
        var s = ApplicationData.Current.LocalSettings;

        s.Values[ThemeKey] = Current.Theme.ToString();
        s.Values[FoldersKey] = System.Text.Json.JsonSerializer.Serialize(Current.LibraryFolders);

        // Playback
        s.Values[AutoplayOnLaunchKey] = Current.AutoplayOnLaunch;
        s.Values[ResumePlaybackPositionKey] = Current.ResumePlaybackPosition;
        s.Values[SkipForwardIntervalKey] = Current.SkipForwardInterval;
        s.Values[SkipBackwardIntervalKey] = Current.SkipBackwardInterval;
        s.Values[AutoAdvanceToNextTrackKey] = Current.AutoAdvanceToNextTrack;
        s.Values[RememberLastPlayedTrackKey] = Current.RememberLastPlayedTrack;
        s.Values[CrossfadeEnabledKey] = Current.CrossfadeEnabled;
        s.Values[CrossfadeDurationKey] = Current.CrossfadeDuration;

        // Audio
        s.Values[EqualizerPresetKey] = Current.Equalizer.ToString();
        s.Values[DefaultVolumeKey] = Current.DefaultVolume;

        // Video
        s.Values[DefaultAspectRatioKey] = Current.DefaultAspectRatio.ToString();

        // Appearance
        s.Values[BackdropTypeKey] = Current.BackdropType.ToString();
        s.Values[AccentColorKey] = Current.AccentColor.ToString();
        s.Values[AlwaysShowTransportBarKey] = Current.AlwaysShowTransportBar;

        // Controls & Interface
        s.Values[ShowOpenFilesOnHomeKey] = Current.ShowOpenFilesOnHome;
        s.Values[OpenFilePositionCornerKey] = Current.OpenFilePositionCorner.ToString();

        // Library
        s.Values[AutomaticLibraryScanKey] = Current.AutomaticLibraryScan;

        // Privacy
        s.Values[RememberPlaybackPositionPerTrackKey] = Current.RememberPlaybackPositionPerTrack;

        // Accessibility
        s.Values[HighContrastModeKey] = Current.HighContrastMode;
        s.Values[TextScaleKey] = Current.TextScale;
        s.Values[ReduceMotionKey] = Current.ReduceMotion;
        s.Values[ScreenReaderOptimizationKey] = Current.ScreenReaderOptimization;
        s.Values[CaptionsAlwaysOnKey] = Current.CaptionsAlwaysOn;
        s.Values[VisualNotificationsForSoundKey] = Current.VisualNotificationsForSound;
        s.Values[KeyboardNavigationHighlightKey] = Current.KeyboardNavigationHighlight;
        s.Values[FocusIndicatorThicknessKey] = Current.FocusIndicatorThickness;
        s.Values[AutoReadControlsKey] = Current.AutoReadControls;
        s.Values[LargerClickTargetsKey] = Current.LargerClickTargets;
        s.Values[ColorBlindModeKey] = Current.ColorBlindMode.ToString();

        // AI Features
        s.Values[AiLyricsTranslationEnabledKey] = Current.AiLyricsTranslationEnabled;
        s.Values[AiTranslationTargetLanguageKey] = Current.AiTranslationTargetLanguage;
        s.Values[AiSemanticSearchEnabledKey] = Current.AiSemanticSearchEnabled;
        Helpers.SecureStorageHelper.SaveSecret("GeminiApiKey", Current.GeminiApiKey);
        s.Values.Remove(GeminiApiKeyKey); // Never persist plaintext in LocalSettings
        s.Values[UseLocalAiKey] = Current.UseLocalAi;
        s.Values[OllamaModelNameKey] = Current.OllamaModelName;
        s.Values[AiEqualizerMatcherEnabledKey] = Current.AiEqualizerMatcherEnabled;
        s.Values[VoiceClarityEnabledKey] = Current.VoiceClarityEnabled;
        s.Values[NightModeEnabledKey] = Current.NightModeEnabled;

        // Premium Features
        s.Values[SleepTimerMinutesKey] = Current.SleepTimerMinutes;
        s.Values[SleepAtEndOfTrackKey] = Current.SleepAtEndOfTrack;
        s.Values[CustomEqualizerGainsKey] = Current.CustomEqualizerGains;
        s.Values[SelectedReverbPresetKey] = Current.SelectedReverbPreset;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTheme(AppThemeOption theme)
    {
        Current.Theme = theme;
        Save();
    }

    public void AddLibraryFolder(string path)
    {
        if (!Current.LibraryFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            Current.LibraryFolders.Add(path);
            Save();
            _ = Task.Run(async () =>
            {
                try
                {
                    var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
                    await SampleMediaLibrary.ScanFolderAsync(folder);
                }
                catch { /* folder may not exist or be accessible */ }
            });
        }
    }

    public void RemoveLibraryFolder(string path)
    {
        Current.LibraryFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public void ResetSettings()
    {
        var s = ApplicationData.Current.LocalSettings;

        // Remove all known keys
        string[] allKeys =
        [
            ThemeKey, FoldersKey,
            AutoplayOnLaunchKey, ResumePlaybackPositionKey, SkipForwardIntervalKey, SkipBackwardIntervalKey,
            AutoAdvanceToNextTrackKey, RememberLastPlayedTrackKey,
            CrossfadeEnabledKey, CrossfadeDurationKey,
            EqualizerPresetKey, DefaultVolumeKey,
            DefaultAspectRatioKey,
            BackdropTypeKey,
            AccentColorKey, AlwaysShowTransportBarKey,
            ShowOpenFilesOnHomeKey, OpenFilePositionCornerKey,
            AutomaticLibraryScanKey,
            RememberPlaybackPositionPerTrackKey,
            HighContrastModeKey, TextScaleKey, ReduceMotionKey,
            ScreenReaderOptimizationKey, CaptionsAlwaysOnKey, VisualNotificationsForSoundKey,
            KeyboardNavigationHighlightKey, FocusIndicatorThicknessKey, AutoReadControlsKey,
            LargerClickTargetsKey, ColorBlindModeKey,
            AiLyricsTranslationEnabledKey, AiTranslationTargetLanguageKey, AiSemanticSearchEnabledKey, GeminiApiKeyKey,
            UseLocalAiKey, OllamaModelNameKey,
            AiEqualizerMatcherEnabledKey, VoiceClarityEnabledKey, NightModeEnabledKey,
            SleepTimerMinutesKey, SleepAtEndOfTrackKey, CustomEqualizerGainsKey, SelectedReverbPresetKey
        ];

        foreach (var key in allKeys)
        {
            s.Values.Remove(key);
        }

        Helpers.SecureStorageHelper.DeleteSecret("GeminiApiKey");

        Load();
        Save();
        SampleMediaLibrary.ClearLibrary();
    }

    public async System.Threading.Tasks.Task ResetPlaybackHistoryAndCacheAsync()
    {
        try
        {
            var tempFolder = ApplicationData.Current.TemporaryFolder;
            var files = await tempFolder.GetFilesAsync();
            foreach (var file in files)
            {
                try { await file.DeleteAsync(); } catch { }
            }
            var folders = await tempFolder.GetFoldersAsync();
            foreach (var folder in folders)
            {
                try { await folder.DeleteAsync(); } catch { }
            }
        }
        catch { }

        try
        {
            var cacheFolder = ApplicationData.Current.LocalCacheFolder;
            var files = await cacheFolder.GetFilesAsync();
            foreach (var file in files)
            {
                try { await file.DeleteAsync(); } catch { }
            }
            var folders = await cacheFolder.GetFoldersAsync();
            foreach (var folder in folders)
            {
                try { await folder.DeleteAsync(); } catch { }
            }
        }
        catch { }

        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            var keysToRemove = new System.Collections.Generic.List<string>();
            foreach (var pair in localSettings.Values)
            {
                if (pair.Key.StartsWith("TrackPos_"))
                {
                    keysToRemove.Add(pair.Key);
                }
            }
            foreach (var key in keysToRemove)
            {
                localSettings.Values.Remove(key);
            }
        }
        catch { }

        SampleMediaLibrary.ClearLibrary();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Helper methods ─────────────────────────────────────────────────

    private static bool ReadBool(Windows.Foundation.Collections.IPropertySet s, string key, bool defaultValue) =>
        s.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;

    private static int ReadInt(Windows.Foundation.Collections.IPropertySet s, string key, int defaultValue) =>
        s.TryGetValue(key, out var v) && (v is int i || (v is double d && (i = (int)d) == i)) ? (v is int val ? val : (int)(double)v) : defaultValue;

    private static double ReadDouble(Windows.Foundation.Collections.IPropertySet s, string key, double defaultValue) =>
        s.TryGetValue(key, out var v) && v is double d ? d : defaultValue;

    private static T ParseEnum<T>(Windows.Foundation.Collections.IPropertySet s, string key, T defaultValue) where T : struct, Enum
    {
        if (s.TryGetValue(key, out var v) && v is string str && Enum.TryParse<T>(str, out var result))
            return result;
        return defaultValue;
    }
}
