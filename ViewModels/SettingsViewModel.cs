using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private bool _isSyncing;

    // ── Playback ───────────────────────────────────────────────────
    [ObservableProperty] public partial AppThemeOption SelectedTheme { get; set; }
    [ObservableProperty] public partial bool AutoplayOnLaunch { get; set; }
    [ObservableProperty] public partial bool ResumePlaybackPosition { get; set; }
    [ObservableProperty] public partial int SkipForwardInterval { get; set; }
    [ObservableProperty] public partial int SkipBackwardInterval { get; set; }
    [ObservableProperty] public partial double DefaultPlaybackSpeed { get; set; }
    [ObservableProperty] public partial bool AutoAdvanceToNextTrack { get; set; }
    [ObservableProperty] public partial bool RememberLastPlayedTrack { get; set; }
    [ObservableProperty] public partial bool EnableSwipeNavigation { get; set; }
    [ObservableProperty] public partial bool CrossfadeEnabled { get; set; }
    [ObservableProperty] public partial int CrossfadeDuration { get; set; }
    [ObservableProperty] public partial bool GaplessPlayback { get; set; }

    // ── Audio ──────────────────────────────────────────────────────
    [ObservableProperty] public partial EqualizerPreset SelectedEqualizer { get; set; }
    [ObservableProperty] public partial bool VolumeNormalization { get; set; }
    [ObservableProperty] public partial double DefaultVolume { get; set; }
    [ObservableProperty] public partial bool MonoAudio { get; set; }
    [ObservableProperty] public partial int BassBoostLevel { get; set; }
    [ObservableProperty] public partial int AudioBalance { get; set; }

    // ── Video ──────────────────────────────────────────────────────
    [ObservableProperty] public partial AspectRatioOption DefaultAspectRatio { get; set; }
    [ObservableProperty] public partial string DefaultSubtitleLanguage { get; set; } = "None";
    [ObservableProperty] public partial SubtitleFontSize SubtitleFontSize { get; set; }
    [ObservableProperty] public partial int SubtitleBackgroundOpacity { get; set; }
    [ObservableProperty] public partial bool HardwareAcceleration { get; set; }
    [ObservableProperty] public partial bool AutoRotateVideo { get; set; }

    // ── HDR & Color Pipeline ───────────────────────────────────────
    [ObservableProperty] public partial HdrMode SelectedHdrMode { get; set; }
    [ObservableProperty] public partial ToneMappingMode SelectedToneMappingMode { get; set; }
    [ObservableProperty] public partial int PeakBrightnessNits { get; set; }
    [ObservableProperty] public partial bool HdrRealTimePlayback { get; set; }
    [ObservableProperty] public partial bool ShowHdrBadge { get; set; }

    // ── Appearance ─────────────────────────────────────────────────
    [ObservableProperty] public partial AppThemeBackdrop SelectedBackdrop { get; set; }
    [ObservableProperty] public partial bool ShowMediaCardGlow { get; set; }
    [ObservableProperty] public partial bool ShowTimelinePreview { get; set; }
    [ObservableProperty] public partial AccentColorOption SelectedAccentColor { get; set; }
    [ObservableProperty] public partial bool CompactDensityMode { get; set; }
    [ObservableProperty] public partial bool ShowAlbumArtInTransportBar { get; set; }
    [ObservableProperty] public partial bool AnimatedTransitions { get; set; }
    [ObservableProperty] public partial bool AlwaysShowTransportBar { get; set; }

    // ── Controls & Interface ───────────────────────────────────────
    [ObservableProperty] public partial bool ShowShuffleButton { get; set; }
    [ObservableProperty] public partial bool ShowRepeatButton { get; set; }
    [ObservableProperty] public partial bool ShowSubtitlesButton { get; set; }
    [ObservableProperty] public partial bool ShowFullscreenButton { get; set; }
    [ObservableProperty] public partial bool ShowPipButton { get; set; }
    [ObservableProperty] public partial bool ShowQueueInMoreMenu { get; set; }
    [ObservableProperty] public partial bool ShowSpeedInMoreMenu { get; set; }
    [ObservableProperty] public partial bool ShowOpenFilesOnHome { get; set; }
    [ObservableProperty] public partial OpenFileCorner SelectedOpenFilePositionCorner { get; set; }

    // ── Library ────────────────────────────────────────────────────
    [ObservableProperty] public partial bool AutomaticLibraryScan { get; set; }
    [ObservableProperty] public partial LibrarySortOrder SelectedLibrarySortOrder { get; set; }
    [ObservableProperty] public partial bool ShowHiddenFiles { get; set; }
    [ObservableProperty] public partial bool AutoImportNewFiles { get; set; }

    // ── Privacy ────────────────────────────────────────────────────
    [ObservableProperty] public partial bool RememberRecentlyPlayed { get; set; }
    [ObservableProperty] public partial bool RememberPlaybackPositionPerTrack { get; set; }

    // ── Accessibility ──────────────────────────────────────────────
    [ObservableProperty] public partial bool HighContrastMode { get; set; }
    [ObservableProperty] public partial bool LargeTextMode { get; set; }
    [ObservableProperty] public partial bool ReduceMotion { get; set; }
    [ObservableProperty] public partial bool ScreenReaderOptimization { get; set; }
    [ObservableProperty] public partial bool CaptionsAlwaysOn { get; set; }
    [ObservableProperty] public partial bool VisualNotificationsForSound { get; set; }
    [ObservableProperty] public partial bool KeyboardNavigationHighlight { get; set; }
    [ObservableProperty] public partial int FocusIndicatorThickness { get; set; }
    [ObservableProperty] public partial bool AutoReadControls { get; set; }
    [ObservableProperty] public partial bool LargerClickTargets { get; set; }
    [ObservableProperty] public partial ColorBlindMode SelectedColorBlindMode { get; set; }

    // ── Folders ────────────────────────────────────────────────────
    [ObservableProperty] public partial IReadOnlyList<string> LibraryFolders { get; set; } = [];

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        SyncFromSettings();
        _settingsService.SettingsChanged += (_, _) => SyncFromSettings();
    }

    // ── Index properties for ComboBox bindings ─────────────────────

    public int SelectedThemeIndex
    {
        get => (int)SelectedTheme;
        set { if (SelectedTheme != (AppThemeOption)value) SelectedTheme = (AppThemeOption)value; }
    }

    public int SelectedEqualizerIndex
    {
        get => (int)SelectedEqualizer;
        set { if (SelectedEqualizer != (EqualizerPreset)value) SelectedEqualizer = (EqualizerPreset)value; }
    }

    public int SelectedBackdropIndex
    {
        get => (int)SelectedBackdrop;
        set { if (SelectedBackdrop != (AppThemeBackdrop)value) SelectedBackdrop = (AppThemeBackdrop)value; }
    }

    public int SelectedAccentColorIndex
    {
        get => (int)SelectedAccentColor;
        set { if (SelectedAccentColor != (AccentColorOption)value) SelectedAccentColor = (AccentColorOption)value; }
    }

    public int SelectedAspectRatioIndex
    {
        get => (int)DefaultAspectRatio;
        set { if (DefaultAspectRatio != (AspectRatioOption)value) DefaultAspectRatio = (AspectRatioOption)value; }
    }

    public int SelectedHdrModeIndex
    {
        get => (int)SelectedHdrMode;
        set { if (SelectedHdrMode != (HdrMode)value) SelectedHdrMode = (HdrMode)value; }
    }

    public int SelectedToneMappingModeIndex
    {
        get => (int)SelectedToneMappingMode;
        set { if (SelectedToneMappingMode != (ToneMappingMode)value) SelectedToneMappingMode = (ToneMappingMode)value; }
    }

    public string PeakBrightnessText => $"{PeakBrightnessNits} nits";

    public int SelectedSubtitleFontSizeIndex
    {
        get => (int)SubtitleFontSize;
        set { if (SubtitleFontSize != (SubtitleFontSize)value) SubtitleFontSize = (SubtitleFontSize)value; }
    }

    public int SelectedSubtitleLanguageIndex
    {
        get => DefaultSubtitleLanguage switch
        {
            "None" => 0,
            "English" => 1,
            "Hindi" => 2,
            "Spanish" => 3,
            "French" => 4,
            "German" => 5,
            "Japanese" => 6,
            "Korean" => 7,
            "Chinese" => 8,
            _ => 0,
        };
        set
        {
            DefaultSubtitleLanguage = value switch
            {
                0 => "None",
                1 => "English",
                2 => "Hindi",
                3 => "Spanish",
                4 => "French",
                5 => "German",
                6 => "Japanese",
                7 => "Korean",
                8 => "Chinese",
                _ => "None",
            };
        }
    }

    public int SelectedLibrarySortOrderIndex
    {
        get => (int)SelectedLibrarySortOrder;
        set { if (SelectedLibrarySortOrder != (LibrarySortOrder)value) SelectedLibrarySortOrder = (LibrarySortOrder)value; }
    }

    public int SelectedOpenFilePositionCornerIndex
    {
        get => (int)SelectedOpenFilePositionCorner;
        set { if (SelectedOpenFilePositionCorner != (OpenFileCorner)value) SelectedOpenFilePositionCorner = (OpenFileCorner)value; }
    }

    public int SelectedColorBlindModeIndex
    {
        get => (int)SelectedColorBlindMode;
        set { if (SelectedColorBlindMode != (ColorBlindMode)value) SelectedColorBlindMode = (ColorBlindMode)value; }
    }

    private static readonly int[] SkipIntervals = [5, 10, 15, 30, 45, 60];

    public int SkipForwardIndex
    {
        get { int idx = Array.IndexOf(SkipIntervals, SkipForwardInterval); return idx >= 0 ? idx : 3; }
        set { if (value >= 0 && value < SkipIntervals.Length) SkipForwardInterval = SkipIntervals[value]; }
    }

    public int SkipBackwardIndex
    {
        get { int idx = Array.IndexOf(SkipIntervals, SkipBackwardInterval); return idx >= 0 ? idx : 1; }
        set { if (value >= 0 && value < SkipIntervals.Length) SkipBackwardInterval = SkipIntervals[value]; }
    }

    private static readonly double[] SpeedValues = [0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    public int DefaultPlaybackSpeedIndex
    {
        get
        {
            int idx = Array.IndexOf(SpeedValues, DefaultPlaybackSpeed);
            return idx >= 0 ? idx : 3; // default 1.0x
        }
        set
        {
            if (value >= 0 && value < SpeedValues.Length)
                DefaultPlaybackSpeed = SpeedValues[value];
        }
    }

    public string DefaultVolumeText => $"{(int)DefaultVolume}%";
    public string BassBoostText => $"{BassBoostLevel}%";
    public string SubtitleOpacityText => $"{SubtitleBackgroundOpacity}%";
    public string AudioBalanceText => AudioBalance switch
    {
        0 => "Center",
        < 0 => $"L {-AudioBalance}%",
        _ => $"R {AudioBalance}%",
    };
    public string CrossfadeDurationText => $"{CrossfadeDuration}s";
    public string FocusIndicatorThicknessText => $"{FocusIndicatorThickness}px";

    // ── Change handlers ────────────────────────────────────────────

    partial void OnSelectedThemeChanged(AppThemeOption value)
    {
        if (_isSyncing) return;
        _settingsService.Current.Theme = value;
        _settingsService.Save();
        ThemeHelper.ApplyTheme(App.MainWindowContent, value);
        ThemeHelper.ApplyAccentColor(_settingsService.Current.AccentColor);
        AccessibilityHelper.Apply(_settingsService.Current);
        OnPropertyChanged(nameof(SelectedThemeIndex));
    }

    partial void OnAutoplayOnLaunchChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AutoplayOnLaunch = value; _settingsService.Save(); } }
    partial void OnResumePlaybackPositionChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ResumePlaybackPosition = value; _settingsService.Save(); } }

    partial void OnSkipForwardIntervalChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.SkipForwardInterval = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SkipForwardIndex));
    }

    partial void OnSkipBackwardIntervalChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.SkipBackwardInterval = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SkipBackwardIndex));
    }

    partial void OnDefaultPlaybackSpeedChanged(double value)
    {
        if (_isSyncing) return;
        _settingsService.Current.DefaultPlaybackSpeed = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(DefaultPlaybackSpeedIndex));
    }

    partial void OnAutoAdvanceToNextTrackChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AutoAdvanceToNextTrack = value; _settingsService.Save(); } }
    partial void OnRememberLastPlayedTrackChanged(bool value) { if (!_isSyncing) { _settingsService.Current.RememberLastPlayedTrack = value; _settingsService.Save(); } }
    partial void OnEnableSwipeNavigationChanged(bool value) { if (!_isSyncing) { _settingsService.Current.EnableSwipeNavigation = value; _settingsService.Save(); } }
    partial void OnCrossfadeEnabledChanged(bool value) { if (!_isSyncing) { _settingsService.Current.CrossfadeEnabled = value; _settingsService.Save(); } }

    partial void OnCrossfadeDurationChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.CrossfadeDuration = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(CrossfadeDurationText));
    }

    partial void OnGaplessPlaybackChanged(bool value) { if (!_isSyncing) { _settingsService.Current.GaplessPlayback = value; _settingsService.Save(); } }

    // Audio
    partial void OnSelectedEqualizerChanged(EqualizerPreset value)
    {
        if (_isSyncing) return;
        _settingsService.Current.Equalizer = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedEqualizerIndex));
    }

    partial void OnVolumeNormalizationChanged(bool value) { if (!_isSyncing) { _settingsService.Current.VolumeNormalization = value; _settingsService.Save(); } }

    partial void OnDefaultVolumeChanged(double value)
    {
        if (_isSyncing) return;
        _settingsService.Current.DefaultVolume = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(DefaultVolumeText));
    }

    partial void OnMonoAudioChanged(bool value) { if (!_isSyncing) { _settingsService.Current.MonoAudio = value; _settingsService.Save(); } }

    partial void OnBassBoostLevelChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.BassBoostLevel = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(BassBoostText));
    }

    partial void OnAudioBalanceChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.AudioBalance = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(AudioBalanceText));
    }

    // Video
    partial void OnDefaultAspectRatioChanged(AspectRatioOption value)
    {
        if (_isSyncing) return;
        _settingsService.Current.DefaultAspectRatio = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedAspectRatioIndex));
    }

    partial void OnDefaultSubtitleLanguageChanged(string value)
    {
        if (_isSyncing) return;
        _settingsService.Current.DefaultSubtitleLanguage = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedSubtitleLanguageIndex));
    }

    partial void OnSubtitleFontSizeChanged(SubtitleFontSize value)
    {
        if (_isSyncing) return;
        _settingsService.Current.SubtitleFontSize = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedSubtitleFontSizeIndex));
    }

    partial void OnSubtitleBackgroundOpacityChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.SubtitleBackgroundOpacity = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SubtitleOpacityText));
    }

    partial void OnHardwareAccelerationChanged(bool value) { if (!_isSyncing) { _settingsService.Current.HardwareAcceleration = value; _settingsService.Save(); } }
    partial void OnAutoRotateVideoChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AutoRotateVideo = value; _settingsService.Save(); } }

    // HDR & Color Pipeline
    partial void OnSelectedHdrModeChanged(HdrMode value)
    {
        if (_isSyncing) return;
        _settingsService.Current.HdrMode = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedHdrModeIndex));
        // Re-apply pipeline immediately if a video is active
        TryReapplyHdrPipeline();
    }

    partial void OnSelectedToneMappingModeChanged(ToneMappingMode value)
    {
        if (_isSyncing) return;
        _settingsService.Current.ToneMappingMode = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedToneMappingModeIndex));
        TryReapplyHdrPipeline();
    }

    partial void OnPeakBrightnessNitsChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.PeakBrightnessNits = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(PeakBrightnessText));
        TryReapplyHdrPipeline();
    }

    partial void OnHdrRealTimePlaybackChanged(bool value) { if (!_isSyncing) { _settingsService.Current.HdrRealTimePlayback = value; _settingsService.Save(); } }
    partial void OnShowHdrBadgeChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowHdrBadge = value; _settingsService.Save(); } }

    private static void TryReapplyHdrPipeline()
    {
        try
        {
            var player = AppServices.PlaybackViewModel.Session.MediaPlayer;
            Windows.Media.Playback.MediaPlaybackItem? item = null;
            if (player.Source is Windows.Media.Playback.MediaPlaybackItem mpi) item = mpi;
            else if (player.Source is Windows.Media.Playback.MediaPlaybackList mpl) item = mpl.CurrentItem;
            AppServices.HdrPipeline.ConfigurePipeline(player, item);
        }
        catch { }
    }

    // Appearance
    partial void OnSelectedBackdropChanged(AppThemeBackdrop value)
    {
        if (_isSyncing) return;
        _settingsService.Current.BackdropType = value;
        _settingsService.Save();
        if (App.MainWindowInstance is MainWindow mainWindow) mainWindow.ApplyBackdrop(value);
        OnPropertyChanged(nameof(SelectedBackdropIndex));
    }

    partial void OnShowMediaCardGlowChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowMediaCardGlow = value; _settingsService.Save(); } }
    partial void OnShowTimelinePreviewChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowTimelinePreview = value; _settingsService.Save(); } }

    partial void OnSelectedAccentColorChanged(AccentColorOption value)
    {
        if (_isSyncing) return;
        _settingsService.Current.AccentColor = value;
        _settingsService.Save();
        ThemeHelper.ApplyAccentColor(value);
        AccessibilityHelper.Apply(_settingsService.Current);
        OnPropertyChanged(nameof(SelectedAccentColorIndex));
    }

    partial void OnCompactDensityModeChanged(bool value) { if (!_isSyncing) { _settingsService.Current.CompactDensityMode = value; _settingsService.Save(); } }
    partial void OnShowAlbumArtInTransportBarChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowAlbumArtInTransportBar = value; _settingsService.Save(); } }
    partial void OnAnimatedTransitionsChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AnimatedTransitions = value; _settingsService.Save(); } }
    partial void OnAlwaysShowTransportBarChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AlwaysShowTransportBar = value; _settingsService.Save(); } }

    // Controls & Interface
    partial void OnShowShuffleButtonChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowShuffleButton = value; _settingsService.Save(); } }
    partial void OnShowRepeatButtonChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowRepeatButton = value; _settingsService.Save(); } }
    partial void OnShowSubtitlesButtonChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowSubtitlesButton = value; _settingsService.Save(); } }
    partial void OnShowFullscreenButtonChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowFullscreenButton = value; _settingsService.Save(); } }
    partial void OnShowPipButtonChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowPipButton = value; _settingsService.Save(); } }
    partial void OnShowQueueInMoreMenuChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowQueueInMoreMenu = value; _settingsService.Save(); } }
    partial void OnShowSpeedInMoreMenuChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowSpeedInMoreMenu = value; _settingsService.Save(); } }
    partial void OnShowOpenFilesOnHomeChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowOpenFilesOnHome = value; _settingsService.Save(); } }

    partial void OnSelectedOpenFilePositionCornerChanged(OpenFileCorner value)
    {
        if (_isSyncing) return;
        _settingsService.Current.OpenFilePositionCorner = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedOpenFilePositionCornerIndex));
    }

    // Library
    partial void OnAutomaticLibraryScanChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AutomaticLibraryScan = value; _settingsService.Save(); } }

    partial void OnSelectedLibrarySortOrderChanged(LibrarySortOrder value)
    {
        if (_isSyncing) return;
        _settingsService.Current.LibrarySortOrder = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedLibrarySortOrderIndex));
    }

    partial void OnShowHiddenFilesChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ShowHiddenFiles = value; _settingsService.Save(); } }
    partial void OnAutoImportNewFilesChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AutoImportNewFiles = value; _settingsService.Save(); } }

    // Privacy
    partial void OnRememberRecentlyPlayedChanged(bool value) { if (!_isSyncing) { _settingsService.Current.RememberRecentlyPlayed = value; _settingsService.Save(); } }
    partial void OnRememberPlaybackPositionPerTrackChanged(bool value) { if (!_isSyncing) { _settingsService.Current.RememberPlaybackPositionPerTrack = value; _settingsService.Save(); } }

    // Accessibility
    partial void OnHighContrastModeChanged(bool value) { if (!_isSyncing) { _settingsService.Current.HighContrastMode = value; SaveAndApplyAccessibility(); } }
    partial void OnLargeTextModeChanged(bool value) { if (!_isSyncing) { _settingsService.Current.LargeTextMode = value; SaveAndApplyAccessibility(); } }
    partial void OnReduceMotionChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ReduceMotion = value; SaveAndApplyAccessibility(); } }
    partial void OnScreenReaderOptimizationChanged(bool value) { if (!_isSyncing) { _settingsService.Current.ScreenReaderOptimization = value; SaveAndApplyAccessibility(); } }
    partial void OnCaptionsAlwaysOnChanged(bool value) { if (!_isSyncing) { _settingsService.Current.CaptionsAlwaysOn = value; SaveAndApplyAccessibility(); } }
    partial void OnVisualNotificationsForSoundChanged(bool value) { if (!_isSyncing) { _settingsService.Current.VisualNotificationsForSound = value; SaveAndApplyAccessibility(); } }
    partial void OnKeyboardNavigationHighlightChanged(bool value) { if (!_isSyncing) { _settingsService.Current.KeyboardNavigationHighlight = value; SaveAndApplyAccessibility(); } }

    partial void OnFocusIndicatorThicknessChanged(int value)
    {
        if (_isSyncing) return;
        _settingsService.Current.FocusIndicatorThickness = value;
        SaveAndApplyAccessibility();
        OnPropertyChanged(nameof(FocusIndicatorThicknessText));
    }

    partial void OnAutoReadControlsChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AutoReadControls = value; SaveAndApplyAccessibility(); } }
    partial void OnLargerClickTargetsChanged(bool value) { if (!_isSyncing) { _settingsService.Current.LargerClickTargets = value; SaveAndApplyAccessibility(); } }

    partial void OnSelectedColorBlindModeChanged(ColorBlindMode value)
    {
        if (_isSyncing) return;
        _settingsService.Current.ColorBlindMode = value;
        SaveAndApplyAccessibility();
        OnPropertyChanged(nameof(SelectedColorBlindModeIndex));
    }

    private void SaveAndApplyAccessibility()
    {
        _settingsService.Save();
        AccessibilityHelper.Apply(_settingsService.Current);
    }

    // ── Commands ───────────────────────────────────────────────────

    [RelayCommand]
    private void RemoveFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _settingsService.RemoveLibraryFolder(path);
        SyncFromSettings();
    }

    public void AddFolder(string path)
    {
        _settingsService.AddLibraryFolder(path);
        SyncFromSettings();
    }

    [RelayCommand]
    private void FactoryReset()
    {
        _settingsService.ResetSettings();
        SyncFromSettings();
        AccessibilityHelper.Apply(_settingsService.Current);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ResetHistoryAndCache()
    {
        await _settingsService.ResetPlaybackHistoryAndCacheAsync();
        SyncFromSettings();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ClearPlaybackHistory()
    {
        await _settingsService.ResetPlaybackHistoryAndCacheAsync();
    }

    [RelayCommand]
    private void ClearSearchHistory()
    {
        // Clears search history from local settings
        try
        {
            var s = Windows.Storage.ApplicationData.Current.LocalSettings;
            var keysToRemove = new List<string>();
            foreach (var pair in s.Values)
            {
                if (pair.Key.StartsWith("Search_"))
                    keysToRemove.Add(pair.Key);
            }
            foreach (var key in keysToRemove)
                s.Values.Remove(key);
        }
        catch { }
    }

    [RelayCommand]
    private void ClearRecentFiles()
    {
        // Clears recently opened files from local settings
        try
        {
            var s = Windows.Storage.ApplicationData.Current.LocalSettings;
            var keysToRemove = new List<string>();
            foreach (var pair in s.Values)
            {
                if (pair.Key.StartsWith("Recent_"))
                    keysToRemove.Add(pair.Key);
            }
            foreach (var key in keysToRemove)
                s.Values.Remove(key);
        }
        catch { }
    }

    // ── Sync ───────────────────────────────────────────────────────

    private void SyncFromSettings()
    {
        _isSyncing = true;

        var c = _settingsService.Current;

        SelectedTheme = c.Theme;
        AutoplayOnLaunch = c.AutoplayOnLaunch;
        ResumePlaybackPosition = c.ResumePlaybackPosition;
        SkipForwardInterval = c.SkipForwardInterval;
        SkipBackwardInterval = c.SkipBackwardInterval;
        DefaultPlaybackSpeed = c.DefaultPlaybackSpeed;
        AutoAdvanceToNextTrack = c.AutoAdvanceToNextTrack;
        RememberLastPlayedTrack = c.RememberLastPlayedTrack;
        CrossfadeEnabled = c.CrossfadeEnabled;
        CrossfadeDuration = c.CrossfadeDuration;
        GaplessPlayback = c.GaplessPlayback;
        EnableSwipeNavigation = c.EnableSwipeNavigation;

        SelectedEqualizer = c.Equalizer;
        VolumeNormalization = c.VolumeNormalization;
        DefaultVolume = c.DefaultVolume;
        MonoAudio = c.MonoAudio;
        BassBoostLevel = c.BassBoostLevel;
        AudioBalance = c.AudioBalance;

        DefaultAspectRatio = c.DefaultAspectRatio;
        DefaultSubtitleLanguage = c.DefaultSubtitleLanguage;
        SubtitleFontSize = c.SubtitleFontSize;
        SubtitleBackgroundOpacity = c.SubtitleBackgroundOpacity;
        HardwareAcceleration = c.HardwareAcceleration;
        AutoRotateVideo = c.AutoRotateVideo;

        SelectedHdrMode = c.HdrMode;
        SelectedToneMappingMode = c.ToneMappingMode;
        PeakBrightnessNits = c.PeakBrightnessNits;
        HdrRealTimePlayback = c.HdrRealTimePlayback;
        ShowHdrBadge = c.ShowHdrBadge;

        SelectedBackdrop = c.BackdropType;
        ShowMediaCardGlow = c.ShowMediaCardGlow;
        ShowTimelinePreview = c.ShowTimelinePreview;
        SelectedAccentColor = c.AccentColor;
        CompactDensityMode = c.CompactDensityMode;
        ShowAlbumArtInTransportBar = c.ShowAlbumArtInTransportBar;
        AnimatedTransitions = c.AnimatedTransitions;
        AlwaysShowTransportBar = c.AlwaysShowTransportBar;

        ShowShuffleButton = c.ShowShuffleButton;
        ShowRepeatButton = c.ShowRepeatButton;
        ShowSubtitlesButton = c.ShowSubtitlesButton;
        ShowFullscreenButton = c.ShowFullscreenButton;
        ShowPipButton = c.ShowPipButton;
        ShowQueueInMoreMenu = c.ShowQueueInMoreMenu;
        ShowSpeedInMoreMenu = c.ShowSpeedInMoreMenu;
        ShowOpenFilesOnHome = c.ShowOpenFilesOnHome;
        SelectedOpenFilePositionCorner = c.OpenFilePositionCorner;

        AutomaticLibraryScan = c.AutomaticLibraryScan;
        SelectedLibrarySortOrder = c.LibrarySortOrder;
        ShowHiddenFiles = c.ShowHiddenFiles;
        AutoImportNewFiles = c.AutoImportNewFiles;

        RememberRecentlyPlayed = c.RememberRecentlyPlayed;
        RememberPlaybackPositionPerTrack = c.RememberPlaybackPositionPerTrack;

        HighContrastMode = c.HighContrastMode;
        LargeTextMode = c.LargeTextMode;
        ReduceMotion = c.ReduceMotion;
        ScreenReaderOptimization = c.ScreenReaderOptimization;
        CaptionsAlwaysOn = c.CaptionsAlwaysOn;
        VisualNotificationsForSound = c.VisualNotificationsForSound;
        KeyboardNavigationHighlight = c.KeyboardNavigationHighlight;
        FocusIndicatorThickness = c.FocusIndicatorThickness;
        AutoReadControls = c.AutoReadControls;
        LargerClickTargets = c.LargerClickTargets;
        SelectedColorBlindMode = c.ColorBlindMode;

        LibraryFolders = c.LibraryFolders.ToList();

        _isSyncing = false;

        // Notify all index properties
        OnPropertyChanged(nameof(SelectedThemeIndex));
        OnPropertyChanged(nameof(SelectedEqualizerIndex));
        OnPropertyChanged(nameof(SelectedBackdropIndex));
        OnPropertyChanged(nameof(SkipForwardIndex));
        OnPropertyChanged(nameof(SkipBackwardIndex));
        OnPropertyChanged(nameof(DefaultVolumeText));
        OnPropertyChanged(nameof(DefaultPlaybackSpeedIndex));
        OnPropertyChanged(nameof(SelectedAccentColorIndex));
        OnPropertyChanged(nameof(SelectedAspectRatioIndex));
        OnPropertyChanged(nameof(SelectedSubtitleFontSizeIndex));
        OnPropertyChanged(nameof(SelectedSubtitleLanguageIndex));
        OnPropertyChanged(nameof(SelectedLibrarySortOrderIndex));
        OnPropertyChanged(nameof(SelectedOpenFilePositionCornerIndex));
        OnPropertyChanged(nameof(BassBoostText));
        OnPropertyChanged(nameof(AudioBalanceText));
        OnPropertyChanged(nameof(SubtitleOpacityText));
        OnPropertyChanged(nameof(CrossfadeDurationText));
        OnPropertyChanged(nameof(SelectedColorBlindModeIndex));
        OnPropertyChanged(nameof(FocusIndicatorThicknessText));
        OnPropertyChanged(nameof(SelectedHdrModeIndex));
        OnPropertyChanged(nameof(SelectedToneMappingModeIndex));
        OnPropertyChanged(nameof(PeakBrightnessText));
        AccessibilityHelper.Apply(c);
    }
}



