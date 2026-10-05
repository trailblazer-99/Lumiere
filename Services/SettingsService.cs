using LumiereMediaPlayer.Models;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace LumiereMediaPlayer.Services;

public sealed class SettingsService : ISettingsService
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
    private const string EnableSoundEffectsKey = "EnableSoundEffects";

    // Video
    private const string DefaultAspectRatioKey = "DefaultAspectRatio";
    private const string EnableHoverVideoPreviewKey = "EnableHoverVideoPreview";

    // HDR & Color Pipeline
    private const string HdrModeKey = "HdrMode";
    private const string AutoBoostHdrBrightnessKey = "AutoBoostHdrBrightness";
    private const string ToneMappingModeKey = "ToneMappingMode";
    private const string PeakBrightnessNitsKey = "PeakBrightnessNits";
    private const string ShowHdrBadgeKey = "ShowHdrBadge";

    // Appearance
    private const string BackdropTypeKey = "BackdropType";
    private const string AccentColorKey = "AccentColor";
    private const string AlwaysShowTransportBarKey = "AlwaysShowTransportBar";
    private const string AcrylicTransportBarKey = "AcrylicTransportBar";
    private const string AutoHideTransportBarInStreamingKey = "AutoHideTransportBarInStreaming";

    // Controls & Interface
    private const string ShowOpenFilesOnHomeKey = "ShowOpenFilesOnHome";
    private const string OpenFilePositionCornerKey = "OpenFilePositionCorner";

    // Window State
    private const string WindowWidthKey = "WindowWidth";
    private const string WindowHeightKey = "WindowHeight";
    private const string WindowIsMaximizedKey = "WindowIsMaximized";
    private const string WindowPositionXKey = "WindowPositionX";
    private const string WindowPositionYKey = "WindowPositionY";

    // Library
    private const string AutomaticLibraryScanKey = "AutomaticLibraryScan";

    // Privacy & Security
    private const string RememberPlaybackPositionPerTrackKey = "RememberPlaybackPositionPerTrack";
    private const string EnableAppLockKey = "EnableAppLock";
    private const string AppLockWhenMinimizedKey = "AppLockWhenMinimized";

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

    private static readonly string FallbackSettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumiereMediaPlayer");
    private static readonly string FallbackSettingsPath = Path.Combine(FallbackSettingsDirectory, "settings.json");
    private bool _useFileFallback;

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
            _useFileFallback = true;
            values = LoadFromFileFallback();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService.Load] Fallback to file: {ex.Message}");
            _useFileFallback = true;
            values = LoadFromFileFallback();
        }

        var settingsValues = values ?? new Windows.Foundation.Collections.PropertySet();

        Current = new AppSettings
        {
            Theme = ParseEnum(settingsValues, ThemeKey, AppThemeOption.Default),
            LibraryFolders = settingsValues.TryGetValue(FoldersKey, out var fj) && fj is string fjStr
                ? DeserializeFolders(fjStr)
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
            EnableSoundEffects = ReadBool(settingsValues, EnableSoundEffectsKey, true),

            // Video
            DefaultAspectRatio = ParseEnum(settingsValues, DefaultAspectRatioKey, AspectRatioOption.Auto),
            EnableHoverVideoPreview = ReadBool(settingsValues, EnableHoverVideoPreviewKey, true),

            // HDR & Color Pipeline
            HdrMode = ParseEnum(settingsValues, HdrModeKey, HdrMode.Auto),
            AutoBoostHdrBrightness = ReadBool(settingsValues, AutoBoostHdrBrightnessKey, true),
            ToneMappingMode = ParseEnum(settingsValues, ToneMappingModeKey, ToneMappingMode.DisplayAdaptive),
            PeakBrightnessNits = ReadInt(settingsValues, PeakBrightnessNitsKey, 1000),
            ShowHdrBadge = ReadBool(settingsValues, ShowHdrBadgeKey, true),

            // Appearance
            BackdropType = ParseEnum(settingsValues, BackdropTypeKey, AppThemeBackdrop.Mica),
            AccentColor = ParseEnum(settingsValues, AccentColorKey, AccentColorOption.SystemDefault),
            AlwaysShowTransportBar = ReadBool(settingsValues, AlwaysShowTransportBarKey, false),
            AcrylicTransportBar = ReadBool(settingsValues, AcrylicTransportBarKey, false),
            AutoHideTransportBarInStreaming = ReadBool(settingsValues, AutoHideTransportBarInStreamingKey, true),

            // Controls & Interface
            ShowOpenFilesOnHome = ReadBool(settingsValues, ShowOpenFilesOnHomeKey, true),
            OpenFilePositionCorner = ParseEnum(settingsValues, OpenFilePositionCornerKey, OpenFileCorner.TopRight),

            // Window State
            WindowWidth = ReadDouble(settingsValues, WindowWidthKey, 1200.0),
            WindowHeight = ReadDouble(settingsValues, WindowHeightKey, 800.0),
            WindowIsMaximized = ReadBool(settingsValues, WindowIsMaximizedKey, false),
            WindowPositionX = ReadInt(settingsValues, WindowPositionXKey, -1),
            WindowPositionY = ReadInt(settingsValues, WindowPositionYKey, -1),

            // Library
            AutomaticLibraryScan = ReadBool(settingsValues, AutomaticLibraryScanKey, true),

            // Privacy & Security
            RememberPlaybackPositionPerTrack = ReadBool(settingsValues, RememberPlaybackPositionPerTrackKey, true),
            EnableAppLock = ReadBool(settingsValues, EnableAppLockKey, false),
            AppLockWhenMinimized = ReadBool(settingsValues, AppLockWhenMinimizedKey, false),

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

    public void SaveImmediate()
    {
        _saveDebounceTimer?.Stop();
        ExecuteSave();
    }

    private Windows.Foundation.Collections.IPropertySet LoadFromFileFallback()
    {
        var propSet = new Windows.Foundation.Collections.PropertySet();
        try
        {
            if (File.Exists(FallbackSettingsPath))
            {
                var json = File.ReadAllText(FallbackSettingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    switch (prop.Value.ValueKind)
                    {
                        case System.Text.Json.JsonValueKind.True:
                            propSet[prop.Name] = true;
                            break;
                        case System.Text.Json.JsonValueKind.False:
                            propSet[prop.Name] = false;
                            break;
                        case System.Text.Json.JsonValueKind.String:
                            propSet[prop.Name] = prop.Value.GetString() ?? string.Empty;
                            break;
                        case System.Text.Json.JsonValueKind.Number:
                            if (prop.Value.TryGetInt32(out int iVal))
                            {
                                propSet[prop.Name] = iVal;
                            }
                            else if (prop.Value.TryGetDouble(out double dVal))
                            {
                                propSet[prop.Name] = dVal;
                            }
                            break;
                        default:
                            propSet[prop.Name] = prop.Value.GetRawText();
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService.LoadFromFileFallback] Error: {ex.Message}");
        }

        return propSet;
    }

    private void ExecuteSave()
    {
        Helpers.SecureStorageHelper.SaveSecret("GeminiApiKey", Current.GeminiApiKey);

        if (_useFileFallback)
        {
            SaveToFileFallback();
        }
        else
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                if (s?.Values != null)
                {
                    SaveToPropertySet(s.Values);
                }
                else
                {
                    _useFileFallback = true;
                    SaveToFileFallback();
                }
            }
            catch (InvalidOperationException)
            {
                _useFileFallback = true;
                SaveToFileFallback();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService.ExecuteSave] Error: {ex.Message}");
                _useFileFallback = true;
                SaveToFileFallback();
            }
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveToPropertySet(Windows.Foundation.Collections.IPropertySet values)
    {
        values[ThemeKey] = Current.Theme.ToString();
        values[FoldersKey] = System.Text.Json.JsonSerializer.Serialize(Current.LibraryFolders);

        // Playback
        values[AutoplayOnLaunchKey] = Current.AutoplayOnLaunch;
        values[ResumePlaybackPositionKey] = Current.ResumePlaybackPosition;
        values[SkipForwardIntervalKey] = Current.SkipForwardInterval;
        values[SkipBackwardIntervalKey] = Current.SkipBackwardInterval;
        values[AutoAdvanceToNextTrackKey] = Current.AutoAdvanceToNextTrack;
        values[RememberLastPlayedTrackKey] = Current.RememberLastPlayedTrack;
        values[CrossfadeEnabledKey] = Current.CrossfadeEnabled;
        values[CrossfadeDurationKey] = Current.CrossfadeDuration;

        // Audio
        values[EqualizerPresetKey] = Current.Equalizer.ToString();
        values[DefaultVolumeKey] = Current.DefaultVolume;
        values[EnableSoundEffectsKey] = Current.EnableSoundEffects;

        // Video
        values[DefaultAspectRatioKey] = Current.DefaultAspectRatio.ToString();
        values[EnableHoverVideoPreviewKey] = Current.EnableHoverVideoPreview;

        // HDR & Color Pipeline
        values[HdrModeKey] = Current.HdrMode.ToString();
        values[AutoBoostHdrBrightnessKey] = Current.AutoBoostHdrBrightness;
        values[ToneMappingModeKey] = Current.ToneMappingMode.ToString();
        values[PeakBrightnessNitsKey] = Current.PeakBrightnessNits;
        values[ShowHdrBadgeKey] = Current.ShowHdrBadge;

        // Appearance
        values[BackdropTypeKey] = Current.BackdropType.ToString();
        values[AccentColorKey] = Current.AccentColor.ToString();
        values[AlwaysShowTransportBarKey] = Current.AlwaysShowTransportBar;
        values[AcrylicTransportBarKey] = Current.AcrylicTransportBar;
        values[AutoHideTransportBarInStreamingKey] = Current.AutoHideTransportBarInStreaming;

        // Controls & Interface
        values[ShowOpenFilesOnHomeKey] = Current.ShowOpenFilesOnHome;
        values[OpenFilePositionCornerKey] = Current.OpenFilePositionCorner.ToString();

        // Window State
        values[WindowWidthKey] = Current.WindowWidth;
        values[WindowHeightKey] = Current.WindowHeight;
        values[WindowIsMaximizedKey] = Current.WindowIsMaximized;
        values[WindowPositionXKey] = Current.WindowPositionX;
        values[WindowPositionYKey] = Current.WindowPositionY;

        // Library
        values[AutomaticLibraryScanKey] = Current.AutomaticLibraryScan;

        // Privacy & Security
        values[RememberPlaybackPositionPerTrackKey] = Current.RememberPlaybackPositionPerTrack;
        values[EnableAppLockKey] = Current.EnableAppLock;
        values[AppLockWhenMinimizedKey] = Current.AppLockWhenMinimized;

        // Accessibility
        values[HighContrastModeKey] = Current.HighContrastMode;
        values[TextScaleKey] = Current.TextScale;
        values[ReduceMotionKey] = Current.ReduceMotion;
        values[ScreenReaderOptimizationKey] = Current.ScreenReaderOptimization;
        values[CaptionsAlwaysOnKey] = Current.CaptionsAlwaysOn;
        values[VisualNotificationsForSoundKey] = Current.VisualNotificationsForSound;
        values[KeyboardNavigationHighlightKey] = Current.KeyboardNavigationHighlight;
        values[FocusIndicatorThicknessKey] = Current.FocusIndicatorThickness;
        values[AutoReadControlsKey] = Current.AutoReadControls;
        values[LargerClickTargetsKey] = Current.LargerClickTargets;
        values[ColorBlindModeKey] = Current.ColorBlindMode.ToString();

        // AI Features
        values[AiLyricsTranslationEnabledKey] = Current.AiLyricsTranslationEnabled;
        values[AiTranslationTargetLanguageKey] = Current.AiTranslationTargetLanguage;
        values[AiSemanticSearchEnabledKey] = Current.AiSemanticSearchEnabled;
        values.Remove(GeminiApiKeyKey); // Never persist plaintext in LocalSettings
        values[UseLocalAiKey] = Current.UseLocalAi;
        values[OllamaModelNameKey] = Current.OllamaModelName;
        values[AiEqualizerMatcherEnabledKey] = Current.AiEqualizerMatcherEnabled;
        values[VoiceClarityEnabledKey] = Current.VoiceClarityEnabled;
        values[NightModeEnabledKey] = Current.NightModeEnabled;

        // Premium Features
        values[SleepTimerMinutesKey] = Current.SleepTimerMinutes;
        values[SleepAtEndOfTrackKey] = Current.SleepAtEndOfTrack;
        values[CustomEqualizerGainsKey] = Current.CustomEqualizerGains;
        values[SelectedReverbPresetKey] = Current.SelectedReverbPreset;
    }

    private void SaveToFileFallback()
    {
        try
        {
            var dict = new Dictionary<string, object>
            {
                [ThemeKey] = Current.Theme.ToString(),
                [FoldersKey] = System.Text.Json.JsonSerializer.Serialize(Current.LibraryFolders),

                // Playback
                [AutoplayOnLaunchKey] = Current.AutoplayOnLaunch,
                [ResumePlaybackPositionKey] = Current.ResumePlaybackPosition,
                [SkipForwardIntervalKey] = Current.SkipForwardInterval,
                [SkipBackwardIntervalKey] = Current.SkipBackwardInterval,
                [AutoAdvanceToNextTrackKey] = Current.AutoAdvanceToNextTrack,
                [RememberLastPlayedTrackKey] = Current.RememberLastPlayedTrack,
                [CrossfadeEnabledKey] = Current.CrossfadeEnabled,
                [CrossfadeDurationKey] = Current.CrossfadeDuration,

                // Audio
                [EqualizerPresetKey] = Current.Equalizer.ToString(),
                [DefaultVolumeKey] = Current.DefaultVolume,
                [EnableSoundEffectsKey] = Current.EnableSoundEffects,

                // Video
                [DefaultAspectRatioKey] = Current.DefaultAspectRatio.ToString(),
                [EnableHoverVideoPreviewKey] = Current.EnableHoverVideoPreview,

                // HDR & Color Pipeline
                [HdrModeKey] = Current.HdrMode.ToString(),
                [AutoBoostHdrBrightnessKey] = Current.AutoBoostHdrBrightness,
                [ToneMappingModeKey] = Current.ToneMappingMode.ToString(),
                [PeakBrightnessNitsKey] = Current.PeakBrightnessNits,
                [ShowHdrBadgeKey] = Current.ShowHdrBadge,

                // Appearance
                [BackdropTypeKey] = Current.BackdropType.ToString(),
                [AccentColorKey] = Current.AccentColor.ToString(),
                [AlwaysShowTransportBarKey] = Current.AlwaysShowTransportBar,
                [AcrylicTransportBarKey] = Current.AcrylicTransportBar,
                [AutoHideTransportBarInStreamingKey] = Current.AutoHideTransportBarInStreaming,

                // Controls & Interface
                [ShowOpenFilesOnHomeKey] = Current.ShowOpenFilesOnHome,
                [OpenFilePositionCornerKey] = Current.OpenFilePositionCorner.ToString(),

                // Window State
                [WindowWidthKey] = Current.WindowWidth,
                [WindowHeightKey] = Current.WindowHeight,
                [WindowIsMaximizedKey] = Current.WindowIsMaximized,
                [WindowPositionXKey] = Current.WindowPositionX,
                [WindowPositionYKey] = Current.WindowPositionY,

                // Library
                [AutomaticLibraryScanKey] = Current.AutomaticLibraryScan,

                // Privacy & Security
                [RememberPlaybackPositionPerTrackKey] = Current.RememberPlaybackPositionPerTrack,
                [EnableAppLockKey] = Current.EnableAppLock,
                [AppLockWhenMinimizedKey] = Current.AppLockWhenMinimized,

                // Accessibility
                [HighContrastModeKey] = Current.HighContrastMode,
                [TextScaleKey] = Current.TextScale,
                [ReduceMotionKey] = Current.ReduceMotion,
                [ScreenReaderOptimizationKey] = Current.ScreenReaderOptimization,
                [CaptionsAlwaysOnKey] = Current.CaptionsAlwaysOn,
                [VisualNotificationsForSoundKey] = Current.VisualNotificationsForSound,
                [KeyboardNavigationHighlightKey] = Current.KeyboardNavigationHighlight,
                [FocusIndicatorThicknessKey] = Current.FocusIndicatorThickness,
                [AutoReadControlsKey] = Current.AutoReadControls,
                [LargerClickTargetsKey] = Current.LargerClickTargets,
                [ColorBlindModeKey] = Current.ColorBlindMode.ToString(),

                // AI Features
                [AiLyricsTranslationEnabledKey] = Current.AiLyricsTranslationEnabled,
                [AiTranslationTargetLanguageKey] = Current.AiTranslationTargetLanguage,
                [AiSemanticSearchEnabledKey] = Current.AiSemanticSearchEnabled,
                [UseLocalAiKey] = Current.UseLocalAi,
                [OllamaModelNameKey] = Current.OllamaModelName,
                [AiEqualizerMatcherEnabledKey] = Current.AiEqualizerMatcherEnabled,
                [VoiceClarityEnabledKey] = Current.VoiceClarityEnabled,
                [NightModeEnabledKey] = Current.NightModeEnabled,

                // Premium Features
                [SleepTimerMinutesKey] = Current.SleepTimerMinutes,
                [SleepAtEndOfTrackKey] = Current.SleepAtEndOfTrack,
                [CustomEqualizerGainsKey] = Current.CustomEqualizerGains,
                [SelectedReverbPresetKey] = Current.SelectedReverbPreset
            };

            Directory.CreateDirectory(FallbackSettingsDirectory);
            var json = System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FallbackSettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService.SaveToFileFallback] Error: {ex.Message}");
        }
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
                    await MediaLibraryService.ScanFolderAsync(folder);
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
        try
        {
            var s = ApplicationData.Current.LocalSettings;
            if (s?.Values != null)
            {
                // Remove all known keys
                string[] allKeys =
                [
                    ThemeKey, FoldersKey,
                    AutoplayOnLaunchKey, ResumePlaybackPositionKey, SkipForwardIntervalKey, SkipBackwardIntervalKey,
                    AutoAdvanceToNextTrackKey, RememberLastPlayedTrackKey,
                    CrossfadeEnabledKey, CrossfadeDurationKey,
                    EqualizerPresetKey, DefaultVolumeKey,
                    DefaultAspectRatioKey, EnableHoverVideoPreviewKey,
                    HdrModeKey, AutoBoostHdrBrightnessKey, ToneMappingModeKey, PeakBrightnessNitsKey, ShowHdrBadgeKey,
                    BackdropTypeKey,
                    AccentColorKey, AlwaysShowTransportBarKey, AcrylicTransportBarKey, AutoHideTransportBarInStreamingKey,
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
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService.ResetSettings] Error: {ex.Message}");
        }

        Helpers.SecureStorageHelper.DeleteSecret("GeminiApiKey");

        if (File.Exists(FallbackSettingsPath))
        {
            try { File.Delete(FallbackSettingsPath); } catch { }
        }

        Load();
        Save();
        MediaLibraryService.ClearLibrary();
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

        try
        {
            if (AppServices.HistoryService != null)
            {
                await AppServices.HistoryService.ClearHistoryAsync();
            }
        }
        catch { }

        try
        {
            Windows.Storage.AccessCache.StorageApplicationPermissions.MostRecentlyUsedList.Clear();
        }
        catch { }

        MediaLibraryService.ClearLibrary();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Helper methods ─────────────────────────────────────────────────

    private static bool ReadBool(Windows.Foundation.Collections.IPropertySet s, string key, bool defaultValue) =>
        s.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;

    private static int ReadInt(Windows.Foundation.Collections.IPropertySet s, string key, int defaultValue) =>
        s.TryGetValue(key, out var v) && (v is int i || (v is double d && (i = (int)d) == i)) ? (v is int val ? val : (int)(double)v) : defaultValue;

    private static double ReadDouble(Windows.Foundation.Collections.IPropertySet s, string key, double defaultValue)
    {
        if (!s.TryGetValue(key, out var v) || v is null) return defaultValue;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is float f) return f;
        if (v is long l) return l;
        if (v is string str && double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return defaultValue;
    }

    private static T ParseEnum<T>(Windows.Foundation.Collections.IPropertySet s, string key, T defaultValue) where T : struct, Enum
    {
        if (s.TryGetValue(key, out var v) && v is string str && Enum.TryParse<T>(str, out var result))
            return result;
        return defaultValue;
    }

    private static List<string> DeserializeFolders(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService.DeserializeFolders] JSON error: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService.DeserializeFolders] Unexpected error: {ex.Message}");
            return [];
        }
    }
}
