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
    private const string DefaultPlaybackSpeedKey = "DefaultPlaybackSpeed";
    private const string AutoAdvanceToNextTrackKey = "AutoAdvanceToNextTrack";
    private const string RememberLastPlayedTrackKey = "RememberLastPlayedTrack";
    private const string CrossfadeEnabledKey = "CrossfadeEnabled";
    private const string CrossfadeDurationKey = "CrossfadeDuration";
    private const string GaplessPlaybackKey = "GaplessPlayback";

    // Audio
    private const string EqualizerPresetKey = "EqualizerPreset";
    private const string VolumeNormalizationKey = "VolumeNormalization";
    private const string DefaultVolumeKey = "DefaultVolume";
    private const string MonoAudioKey = "MonoAudio";
    private const string BassBoostLevelKey = "BassBoostLevel";
    private const string AudioBalanceKey = "AudioBalance";

    // Video
    private const string DefaultAspectRatioKey = "DefaultAspectRatio";
    private const string DefaultSubtitleLanguageKey = "DefaultSubtitleLanguage";
    private const string SubtitleFontSizeKey = "SubtitleFontSize";
    private const string SubtitleBackgroundOpacityKey = "SubtitleBackgroundOpacity";
    private const string HardwareAccelerationKey = "HardwareAcceleration";
    private const string AutoRotateVideoKey = "AutoRotateVideo";

    // Appearance
    private const string BackdropTypeKey = "BackdropType";
    private const string ShowMediaCardGlowKey = "ShowMediaCardGlow";
    private const string ShowTimelinePreviewKey = "ShowTimelinePreview";
    private const string AccentColorKey = "AccentColor";
    private const string CompactDensityModeKey = "CompactDensityMode";
    private const string ShowAlbumArtInTransportBarKey = "ShowAlbumArtInTransportBar";
    private const string AnimatedTransitionsKey = "AnimatedTransitions";
    private const string AlwaysShowTransportBarKey = "AlwaysShowTransportBar";

    // Controls & Interface
    private const string ShowShuffleButtonKey = "ShowShuffleButton";
    private const string ShowRepeatButtonKey = "ShowRepeatButton";
    private const string ShowSubtitlesButtonKey = "ShowSubtitlesButton";
    private const string ShowFullscreenButtonKey = "ShowFullscreenButton";
    private const string ShowPipButtonKey = "ShowPipButton";
    private const string ShowQueueInMoreMenuKey = "ShowQueueInMoreMenu";
    private const string ShowSpeedInMoreMenuKey = "ShowSpeedInMoreMenu";
    private const string ShowOpenFilesOnHomeKey = "ShowOpenFilesOnHome";
    private const string OpenFilePositionCornerKey = "OpenFilePositionCorner";

    // Window State
    private const string WindowWidthKey = "WindowWidth";
    private const string WindowHeightKey = "WindowHeight";
    private const string WindowIsMaximizedKey = "WindowIsMaximized";

    // Library
    private const string AutomaticLibraryScanKey = "AutomaticLibraryScan";
    private const string LibrarySortOrderKey = "LibrarySortOrder";
    private const string ShowHiddenFilesKey = "ShowHiddenFiles";
    private const string AutoImportNewFilesKey = "AutoImportNewFiles";

    // Privacy
    private const string RememberRecentlyPlayedKey = "RememberRecentlyPlayed";
    private const string RememberPlaybackPositionPerTrackKey = "RememberPlaybackPositionPerTrack";

    // Accessibility
    private const string HighContrastModeKey = "HighContrastMode";
    private const string LargeTextModeKey = "LargeTextMode";
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
            DefaultPlaybackSpeed = ReadDouble(settingsValues, DefaultPlaybackSpeedKey, 1.0),
            AutoAdvanceToNextTrack = ReadBool(settingsValues, AutoAdvanceToNextTrackKey, true),
            RememberLastPlayedTrack = ReadBool(settingsValues, RememberLastPlayedTrackKey, true),
            CrossfadeEnabled = ReadBool(settingsValues, CrossfadeEnabledKey, false),
            CrossfadeDuration = ReadInt(settingsValues, CrossfadeDurationKey, 3),
            GaplessPlayback = ReadBool(settingsValues, GaplessPlaybackKey, false),

            // Audio
            Equalizer = ParseEnum(settingsValues, EqualizerPresetKey, EqualizerPreset.Flat),
            VolumeNormalization = ReadBool(settingsValues, VolumeNormalizationKey, false),
            DefaultVolume = ReadDouble(settingsValues, DefaultVolumeKey, 75.0),
            MonoAudio = ReadBool(settingsValues, MonoAudioKey, false),
            BassBoostLevel = ReadInt(settingsValues, BassBoostLevelKey, 0),
            AudioBalance = ReadInt(settingsValues, AudioBalanceKey, 0),

            // Video
            DefaultAspectRatio = ParseEnum(settingsValues, DefaultAspectRatioKey, AspectRatioOption.Auto),
            DefaultSubtitleLanguage = settingsValues.TryGetValue(DefaultSubtitleLanguageKey, out var lang) && lang is string sLang ? sLang : "None",
            SubtitleFontSize = ParseEnum(settingsValues, SubtitleFontSizeKey, SubtitleFontSize.Medium),
            SubtitleBackgroundOpacity = ReadInt(settingsValues, SubtitleBackgroundOpacityKey, 60),
            HardwareAcceleration = ReadBool(settingsValues, HardwareAccelerationKey, true),
            AutoRotateVideo = ReadBool(settingsValues, AutoRotateVideoKey, true),

            // Appearance
            BackdropType = ParseEnum(settingsValues, BackdropTypeKey, AppThemeBackdrop.Mica),
            ShowMediaCardGlow = ReadBool(settingsValues, ShowMediaCardGlowKey, true),
            ShowTimelinePreview = ReadBool(settingsValues, ShowTimelinePreviewKey, true),
            AccentColor = ParseEnum(settingsValues, AccentColorKey, AccentColorOption.SystemDefault),
            CompactDensityMode = ReadBool(settingsValues, CompactDensityModeKey, false),
            ShowAlbumArtInTransportBar = ReadBool(settingsValues, ShowAlbumArtInTransportBarKey, true),
            AnimatedTransitions = ReadBool(settingsValues, AnimatedTransitionsKey, true),
            AlwaysShowTransportBar = ReadBool(settingsValues, AlwaysShowTransportBarKey, true),

            // Controls & Interface
            ShowShuffleButton = ReadBool(settingsValues, ShowShuffleButtonKey, true),
            ShowRepeatButton = ReadBool(settingsValues, ShowRepeatButtonKey, true),
            ShowSubtitlesButton = ReadBool(settingsValues, ShowSubtitlesButtonKey, true),
            ShowFullscreenButton = ReadBool(settingsValues, ShowFullscreenButtonKey, true),
            ShowPipButton = ReadBool(settingsValues, ShowPipButtonKey, true),
            ShowQueueInMoreMenu = ReadBool(settingsValues, ShowQueueInMoreMenuKey, true),
            ShowSpeedInMoreMenu = ReadBool(settingsValues, ShowSpeedInMoreMenuKey, true),
            ShowOpenFilesOnHome = ReadBool(settingsValues, ShowOpenFilesOnHomeKey, true),
            OpenFilePositionCorner = ParseEnum(settingsValues, OpenFilePositionCornerKey, OpenFileCorner.TopRight),

            // Window State
            WindowWidth = ReadDouble(settingsValues, WindowWidthKey, 1200.0),
            WindowHeight = ReadDouble(settingsValues, WindowHeightKey, 800.0),
            WindowIsMaximized = ReadBool(settingsValues, WindowIsMaximizedKey, false),

            // Library
            AutomaticLibraryScan = ReadBool(settingsValues, AutomaticLibraryScanKey, true),
            LibrarySortOrder = ParseEnum(settingsValues, LibrarySortOrderKey, LibrarySortOrder.Title),
            ShowHiddenFiles = ReadBool(settingsValues, ShowHiddenFilesKey, false),
            AutoImportNewFiles = ReadBool(settingsValues, AutoImportNewFilesKey, true),

            // Privacy
            RememberRecentlyPlayed = ReadBool(settingsValues, RememberRecentlyPlayedKey, true),
            RememberPlaybackPositionPerTrack = ReadBool(settingsValues, RememberPlaybackPositionPerTrackKey, true),

            // Accessibility
            HighContrastMode = ReadBool(settingsValues, HighContrastModeKey, false),
            LargeTextMode = ReadBool(settingsValues, LargeTextModeKey, false),
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
            GeminiApiKey = settingsValues.TryGetValue(GeminiApiKeyKey, out var aiKey) && aiKey is string sAiKey ? sAiKey : "",
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

    public void Save()
    {
        var s = ApplicationData.Current.LocalSettings;

        s.Values[ThemeKey] = Current.Theme.ToString();
        s.Values[FoldersKey] = System.Text.Json.JsonSerializer.Serialize(Current.LibraryFolders);

        // Playback
        s.Values[AutoplayOnLaunchKey] = Current.AutoplayOnLaunch;
        s.Values[ResumePlaybackPositionKey] = Current.ResumePlaybackPosition;
        s.Values[SkipForwardIntervalKey] = Current.SkipForwardInterval;
        s.Values[SkipBackwardIntervalKey] = Current.SkipBackwardInterval;
        s.Values[DefaultPlaybackSpeedKey] = Current.DefaultPlaybackSpeed;
        s.Values[AutoAdvanceToNextTrackKey] = Current.AutoAdvanceToNextTrack;
        s.Values[RememberLastPlayedTrackKey] = Current.RememberLastPlayedTrack;
        s.Values[CrossfadeEnabledKey] = Current.CrossfadeEnabled;
        s.Values[CrossfadeDurationKey] = Current.CrossfadeDuration;
        s.Values[GaplessPlaybackKey] = Current.GaplessPlayback;

        // Audio
        s.Values[EqualizerPresetKey] = Current.Equalizer.ToString();
        s.Values[VolumeNormalizationKey] = Current.VolumeNormalization;
        s.Values[DefaultVolumeKey] = Current.DefaultVolume;
        s.Values[MonoAudioKey] = Current.MonoAudio;
        s.Values[BassBoostLevelKey] = Current.BassBoostLevel;
        s.Values[AudioBalanceKey] = Current.AudioBalance;

        // Video
        s.Values[DefaultAspectRatioKey] = Current.DefaultAspectRatio.ToString();
        s.Values[DefaultSubtitleLanguageKey] = Current.DefaultSubtitleLanguage;
        s.Values[SubtitleFontSizeKey] = Current.SubtitleFontSize.ToString();
        s.Values[SubtitleBackgroundOpacityKey] = Current.SubtitleBackgroundOpacity;
        s.Values[HardwareAccelerationKey] = Current.HardwareAcceleration;
        s.Values[AutoRotateVideoKey] = Current.AutoRotateVideo;

        // Appearance
        s.Values[BackdropTypeKey] = Current.BackdropType.ToString();
        s.Values[ShowMediaCardGlowKey] = Current.ShowMediaCardGlow;
        s.Values[ShowTimelinePreviewKey] = Current.ShowTimelinePreview;
        s.Values[AccentColorKey] = Current.AccentColor.ToString();
        s.Values[CompactDensityModeKey] = Current.CompactDensityMode;
        s.Values[ShowAlbumArtInTransportBarKey] = Current.ShowAlbumArtInTransportBar;
        s.Values[AnimatedTransitionsKey] = Current.AnimatedTransitions;
        s.Values[AlwaysShowTransportBarKey] = Current.AlwaysShowTransportBar;

        // Controls & Interface
        s.Values[ShowShuffleButtonKey] = Current.ShowShuffleButton;
        s.Values[ShowRepeatButtonKey] = Current.ShowRepeatButton;
        s.Values[ShowSubtitlesButtonKey] = Current.ShowSubtitlesButton;
        s.Values[ShowFullscreenButtonKey] = Current.ShowFullscreenButton;
        s.Values[ShowPipButtonKey] = Current.ShowPipButton;
        s.Values[ShowQueueInMoreMenuKey] = Current.ShowQueueInMoreMenu;
        s.Values[ShowSpeedInMoreMenuKey] = Current.ShowSpeedInMoreMenu;
        s.Values[ShowOpenFilesOnHomeKey] = Current.ShowOpenFilesOnHome;
        s.Values[OpenFilePositionCornerKey] = Current.OpenFilePositionCorner.ToString();

        // Library
        s.Values[AutomaticLibraryScanKey] = Current.AutomaticLibraryScan;
        s.Values[LibrarySortOrderKey] = Current.LibrarySortOrder.ToString();
        s.Values[ShowHiddenFilesKey] = Current.ShowHiddenFiles;
        s.Values[AutoImportNewFilesKey] = Current.AutoImportNewFiles;

        // Privacy
        s.Values[RememberRecentlyPlayedKey] = Current.RememberRecentlyPlayed;
        s.Values[RememberPlaybackPositionPerTrackKey] = Current.RememberPlaybackPositionPerTrack;

        // Accessibility
        s.Values[HighContrastModeKey] = Current.HighContrastMode;
        s.Values[LargeTextModeKey] = Current.LargeTextMode;
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
        s.Values[GeminiApiKeyKey] = Current.GeminiApiKey;
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
            DefaultPlaybackSpeedKey, AutoAdvanceToNextTrackKey, RememberLastPlayedTrackKey,
            CrossfadeEnabledKey, CrossfadeDurationKey, GaplessPlaybackKey,
            EqualizerPresetKey, VolumeNormalizationKey, DefaultVolumeKey,
            MonoAudioKey, BassBoostLevelKey, AudioBalanceKey,
            DefaultAspectRatioKey, DefaultSubtitleLanguageKey, SubtitleFontSizeKey,
            SubtitleBackgroundOpacityKey, HardwareAccelerationKey, AutoRotateVideoKey,
            BackdropTypeKey, ShowMediaCardGlowKey, ShowTimelinePreviewKey,
            AccentColorKey, CompactDensityModeKey, ShowAlbumArtInTransportBarKey,
            AnimatedTransitionsKey, AlwaysShowTransportBarKey,
            ShowShuffleButtonKey, ShowRepeatButtonKey, ShowSubtitlesButtonKey,
            ShowFullscreenButtonKey, ShowPipButtonKey, ShowQueueInMoreMenuKey,
            ShowSpeedInMoreMenuKey, ShowOpenFilesOnHomeKey, OpenFilePositionCornerKey,
            AutomaticLibraryScanKey, LibrarySortOrderKey, ShowHiddenFilesKey, AutoImportNewFilesKey,
            RememberRecentlyPlayedKey, RememberPlaybackPositionPerTrackKey,
            HighContrastModeKey, LargeTextModeKey, ReduceMotionKey,
            ScreenReaderOptimizationKey, CaptionsAlwaysOnKey, VisualNotificationsForSoundKey,
            KeyboardNavigationHighlightKey, FocusIndicatorThicknessKey, AutoReadControlsKey,
            LargerClickTargetsKey, ColorBlindModeKey,
            AiLyricsTranslationEnabledKey, AiTranslationTargetLanguageKey, AiSemanticSearchEnabledKey, GeminiApiKeyKey,
            AiEqualizerMatcherEnabledKey, VoiceClarityEnabledKey, NightModeEnabledKey,
            SleepTimerMinutesKey, SleepAtEndOfTrackKey, CustomEqualizerGainsKey, SelectedReverbPresetKey
        ];

        foreach (var key in allKeys)
        {
            s.Values.Remove(key);
        }

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
