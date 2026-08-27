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
    private readonly Microsoft.UI.Xaml.DispatcherTimer _textScaleDebounceTimer;

    // ── Playback ───────────────────────────────────────────────────
    [ObservableProperty] public partial AppThemeOption SelectedTheme { get; set; }
    [ObservableProperty] public partial bool AutoplayOnLaunch { get; set; }
    [ObservableProperty] public partial bool ResumePlaybackPosition { get; set; }
    [ObservableProperty] public partial int SkipForwardInterval { get; set; }
    [ObservableProperty] public partial int SkipBackwardInterval { get; set; }
    [ObservableProperty] public partial bool AutoAdvanceToNextTrack { get; set; }
    [ObservableProperty] public partial bool RememberLastPlayedTrack { get; set; }
    [ObservableProperty] public partial bool EnableSwipeNavigation { get; set; }
    [ObservableProperty] public partial bool CrossfadeEnabled { get; set; }
    [ObservableProperty] public partial int CrossfadeDuration { get; set; }

    // ── Audio ──────────────────────────────────────────────────────
    [ObservableProperty] public partial EqualizerPreset SelectedEqualizer { get; set; }
    [ObservableProperty] public partial double DefaultVolume { get; set; }

    // ── Video ──────────────────────────────────────────────────────
    [ObservableProperty] public partial AspectRatioOption DefaultAspectRatio { get; set; }

    // ── HDR & Color Pipeline ───────────────────────────────────────
    [ObservableProperty] public partial HdrMode SelectedHdrMode { get; set; }
    [ObservableProperty] public partial ToneMappingMode SelectedToneMappingMode { get; set; }
    [ObservableProperty] public partial int PeakBrightnessNits { get; set; }
    [ObservableProperty] public partial bool AutoBoostHdrBrightness { get; set; }
    [ObservableProperty] public partial bool ShowHdrBadge { get; set; }

    // ── Appearance ─────────────────────────────────────────────────
    [ObservableProperty] public partial AppThemeBackdrop SelectedBackdrop { get; set; }
    [ObservableProperty] public partial AccentColorOption SelectedAccentColor { get; set; }
    [ObservableProperty] public partial bool AlwaysShowTransportBar { get; set; }

    // ── Controls & Interface ───────────────────────────────────────
    [ObservableProperty] public partial bool ShowOpenFilesOnHome { get; set; }
    [ObservableProperty] public partial OpenFileCorner SelectedOpenFilePositionCorner { get; set; }

    // ── Library ────────────────────────────────────────────────────
    [ObservableProperty] public partial bool AutomaticLibraryScan { get; set; }

    // ── Privacy ────────────────────────────────────────────────────
    [ObservableProperty] public partial bool RememberPlaybackPositionPerTrack { get; set; }

    // ── Accessibility ──────────────────────────────────────────────
    [ObservableProperty] public partial bool HighContrastMode { get; set; }
    [ObservableProperty] public partial double TextScale { get; set; }
    [ObservableProperty] public partial bool ReduceMotion { get; set; }
    [ObservableProperty] public partial bool ScreenReaderOptimization { get; set; }
    [ObservableProperty] public partial bool CaptionsAlwaysOn { get; set; }
    [ObservableProperty] public partial bool VisualNotificationsForSound { get; set; }
    [ObservableProperty] public partial bool KeyboardNavigationHighlight { get; set; }
    [ObservableProperty] public partial int FocusIndicatorThickness { get; set; }
    [ObservableProperty] public partial bool AutoReadControls { get; set; }
    [ObservableProperty] public partial bool LargerClickTargets { get; set; }
    [ObservableProperty] public partial ColorBlindMode SelectedColorBlindMode { get; set; }

    // ── AI Features Settings ───────────────────────────────────────
    [ObservableProperty] public partial bool AiLyricsTranslationEnabled { get; set; }
    [ObservableProperty] public partial string AiTranslationTargetLanguage { get; set; } = "Hindi";
    [ObservableProperty] public partial bool AiSemanticSearchEnabled { get; set; }
    [ObservableProperty] public partial string GeminiApiKey { get; set; }
    [ObservableProperty] public partial bool UseLocalAi { get; set; }
    [ObservableProperty] public partial string OllamaModelName { get; set; }
    [ObservableProperty] public partial bool AiEqualizerMatcherEnabled { get; set; }
    [ObservableProperty] public partial bool VoiceClarityEnabled { get; set; }
    [ObservableProperty] public partial bool NightModeEnabled { get; set; }
    [ObservableProperty] public partial string AiConnectionStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsTestingAi { get; set; }
    [ObservableProperty] public partial bool? IsAiConnected { get; set; }
    
    // ── Local AI Hardware Status ───────────────────────────────────
    [ObservableProperty] public partial bool IsLocalAiSupported { get; set; }
    [ObservableProperty] public partial string LocalAiHardwareSuggestion { get; set; } = string.Empty;

    // ── Update Status ──────────────────────────────────────────────
    [ObservableProperty] public partial bool IsCheckingForUpdates { get; set; }
    [ObservableProperty] public partial bool IsUpdateAvailable { get; set; }
    [ObservableProperty] public partial string UpdateStatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial string LatestVersion { get; set; } = string.Empty;

    // ── Folders ────────────────────────────────────────────────────
    [ObservableProperty] public partial IReadOnlyList<string> LibraryFolders { get; set; } = [];

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _textScaleDebounceTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _textScaleDebounceTimer.Tick += (s, e) =>
        {
            _textScaleDebounceTimer.Stop();
            SaveAndApplyAccessibility();
        };

        SyncFromSettings();
        _settingsService.SettingsChanged += (_, _) => SyncFromSettings();
        AppServices.DisplayManager.AdvancedColorInfoChanged += (_, _) =>
        {
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(ActiveDisplayProfileSummary));
            });
        };

        // Analyze hardware async in background
        _ = AnalyzeHardwareBackgroundAsync();
    }

    private async System.Threading.Tasks.Task AnalyzeHardwareBackgroundAsync()
    {
        var result = await HardwareDetectionService.AnalyzeHardwareAsync();
        App.MainDispatcher?.TryEnqueue(() =>
        {
            IsLocalAiSupported = result.SupportsLocalAi;
            if (result.SupportsLocalAi)
            {
                LocalAiHardwareSuggestion = $"Hardware analysis: Compatible {(string.IsNullOrWhiteSpace(result.GpuName) ? "System RAM" : result.GpuName)} detected. Local AI inference is recommended for privacy and offline usage.";
            }
        });
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates) return;
        IsCheckingForUpdates = true;
        UpdateStatusText = "Checking for updates...";
        IsUpdateAvailable = false;

        var info = await UpdateService.CheckForUpdatesAsync();
        
        IsCheckingForUpdates = false;
        
        if (info.IsUpdateAvailable)
        {
            IsUpdateAvailable = true;
            UpdateStatusText = $"Update Available: v{info.LatestVersion} (Current: {info.CurrentVersion})";
            LatestVersion = info.LatestVersion;
        }
        else if (!string.IsNullOrEmpty(info.CurrentVersion))
        {
            UpdateStatusText = $"You're on the latest version ({info.CurrentVersion}).";
        }
        else
        {
            UpdateStatusText = "Failed to check for updates. Try again later.";
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task InstallUpdateAsync()
    {
        await UpdateService.InstallUpdateAsync();
    }

    // ── Index properties for ComboBox bindings ─────────────────────

    public int SelectedThemeIndex
    {
        get => (int)SelectedTheme;
        set { if (value >= 0 && SelectedTheme != (AppThemeOption)value) SelectedTheme = (AppThemeOption)value; }
    }

    public int SelectedEqualizerIndex
    {
        get => (int)SelectedEqualizer;
        set { if (value >= 0 && SelectedEqualizer != (EqualizerPreset)value) SelectedEqualizer = (EqualizerPreset)value; }
    }

    public int SelectedBackdropIndex
    {
        get => (int)SelectedBackdrop;
        set { if (value >= 0 && SelectedBackdrop != (AppThemeBackdrop)value) SelectedBackdrop = (AppThemeBackdrop)value; }
    }

    public int SelectedAccentColorIndex
    {
        get => (int)SelectedAccentColor;
        set { if (value >= 0 && SelectedAccentColor != (AccentColorOption)value) SelectedAccentColor = (AccentColorOption)value; }
    }

    public int SelectedAspectRatioIndex
    {
        get => (int)DefaultAspectRatio;
        set { if (value >= 0 && DefaultAspectRatio != (AspectRatioOption)value) DefaultAspectRatio = (AspectRatioOption)value; }
    }

    public int SelectedHdrModeIndex
    {
        get => (int)SelectedHdrMode;
        set { if (value >= 0 && SelectedHdrMode != (HdrMode)value) SelectedHdrMode = (HdrMode)value; }
    }

    public int SelectedToneMappingModeIndex
    {
        get => (int)SelectedToneMappingMode;
        set { if (value >= 0 && SelectedToneMappingMode != (ToneMappingMode)value) SelectedToneMappingMode = (ToneMappingMode)value; }
    }

    public string PeakBrightnessText => $"{PeakBrightnessNits} nits";
    public string ActiveDisplayProfileSummary => AppServices.DisplayManager.DisplayProfileSummary;

    public int SelectedOpenFilePositionCornerIndex
    {
        get => (int)SelectedOpenFilePositionCorner;
        set { if (value >= 0 && SelectedOpenFilePositionCorner != (OpenFileCorner)value) SelectedOpenFilePositionCorner = (OpenFileCorner)value; }
    }

    public int SelectedColorBlindModeIndex
    {
        get => (int)SelectedColorBlindMode;
        set { if (value >= 0 && SelectedColorBlindMode != (ColorBlindMode)value) SelectedColorBlindMode = (ColorBlindMode)value; }
    }

    public int SelectedAiTranslationLanguageIndex
    {
        get => AiTranslationTargetLanguage switch
        {
            "Hindi" => 0,
            "Spanish" => 1,
            "French" => 2,
            "German" => 3,
            "Japanese" => 4,
            "Chinese" => 5,
            "Russian" => 6,
            "Italian" => 7,
            _ => 0,
        };
        set
        {
            if (value >= 0)
            {
                AiTranslationTargetLanguage = value switch
                {
                    0 => "Hindi",
                    1 => "Spanish",
                    2 => "French",
                    3 => "German",
                    4 => "Japanese",
                    5 => "Chinese",
                    6 => "Russian",
                    7 => "Italian",
                    _ => "Hindi",
                };
            }
        }
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

    public string DefaultVolumeText => $"{(int)DefaultVolume}%";
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

    // Audio
    partial void OnSelectedEqualizerChanged(EqualizerPreset value)
    {
        if (_isSyncing) return;
        _settingsService.Current.Equalizer = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedEqualizerIndex));
    }

    partial void OnDefaultVolumeChanged(double value)
    {
        if (_isSyncing) return;
        _settingsService.Current.DefaultVolume = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(DefaultVolumeText));
    }

    // Video
    partial void OnDefaultAspectRatioChanged(AspectRatioOption value)
    {
        if (_isSyncing) return;
        _settingsService.Current.DefaultAspectRatio = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedAspectRatioIndex));
    }

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

    partial void OnAutoBoostHdrBrightnessChanged(bool value)
    {
        if (_isSyncing) return;
        _settingsService.Current.AutoBoostHdrBrightness = value;
        _settingsService.Save();
        TryReapplyHdrPipeline();
    }

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

    partial void OnSelectedAccentColorChanged(AccentColorOption value)
    {
        if (_isSyncing) return;
        _settingsService.Current.AccentColor = value;
        _settingsService.Save();
        ThemeHelper.ApplyAccentColor(value);
        AccessibilityHelper.Apply(_settingsService.Current);
        OnPropertyChanged(nameof(SelectedAccentColorIndex));
    }

    partial void OnAlwaysShowTransportBarChanged(bool value)
    {
        if (!_isSyncing)
        {
            _settingsService.Current.AlwaysShowTransportBar = value;
            _settingsService.Save();
            App.MainWindowInstance?.ApplyTransportBarVisibility(value);
        }
    }

    // Controls & Interface
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

    // Privacy
    partial void OnRememberPlaybackPositionPerTrackChanged(bool value) { if (!_isSyncing) { _settingsService.Current.RememberPlaybackPositionPerTrack = value; _settingsService.Save(); } }

    // Accessibility
    partial void OnHighContrastModeChanged(bool value) { if (!_isSyncing) { _settingsService.Current.HighContrastMode = value; SaveAndApplyAccessibility(); } }
    partial void OnTextScaleChanged(double value) 
    { 
        if (!_isSyncing) 
        { 
            _settingsService.Current.TextScale = value; 
            _textScaleDebounceTimer.Stop();
            _textScaleDebounceTimer.Start();
        } 
    }
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

    // ── AI Features Settings Change Handlers ───────────────────────
    partial void OnAiLyricsTranslationEnabledChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AiLyricsTranslationEnabled = value; _settingsService.Save(); } }
    partial void OnAiTranslationTargetLanguageChanged(string value) { if (!_isSyncing) { _settingsService.Current.AiTranslationTargetLanguage = value; _settingsService.Save(); OnPropertyChanged(nameof(SelectedAiTranslationLanguageIndex)); } }
    partial void OnAiSemanticSearchEnabledChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AiSemanticSearchEnabled = value; _settingsService.Save(); } }
    partial void OnGeminiApiKeyChanged(string value) { if (!_isSyncing) { _settingsService.Current.GeminiApiKey = value; _settingsService.Save(); } }
    partial void OnUseLocalAiChanged(bool value) { if (!_isSyncing) { _settingsService.Current.UseLocalAi = value; _settingsService.Save(); } }
    partial void OnOllamaModelNameChanged(string value) { if (!_isSyncing) { _settingsService.Current.OllamaModelName = value; _settingsService.Save(); } }
    partial void OnAiEqualizerMatcherEnabledChanged(bool value) { if (!_isSyncing) { _settingsService.Current.AiEqualizerMatcherEnabled = value; _settingsService.Save(); } }
    partial void OnVoiceClarityEnabledChanged(bool value)
    {
        if (!_isSyncing)
        {
            _settingsService.Current.VoiceClarityEnabled = value;
            _settingsService.Save();
            AppServices.PlaybackViewModel.Session.ApplyVoiceClarity(value);
        }
    }
    partial void OnNightModeEnabledChanged(bool value)
    {
        if (!_isSyncing)
        {
            _settingsService.Current.NightModeEnabled = value;
            _settingsService.Save();
            AppServices.PlaybackViewModel.Session.ApplyNightMode(value);
        }
    }

    private void SaveAndApplyAccessibility()
    {
        try
        {
            _settingsService.Save();
            AccessibilityHelper.Apply(_settingsService.Current);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error applying accessibility: {ex.Message}");
        }
    }

    // ── Commands ───────────────────────────────────────────────────

    [RelayCommand]
    private async System.Threading.Tasks.Task TestAiConnection()
    {
        IsTestingAi = true;
        AiConnectionStatus = "Pinging configured AI pipeline...";
        IsAiConnected = null;

        try
        {
            // Ensure settings service has the latest key immediately
            _settingsService.Current.GeminiApiKey = GeminiApiKey?.Trim() ?? "";
            var (success, message, latency) = await Services.AiAssistantService.TestAiConnectionAsync(GeminiApiKey);
            IsAiConnected = success;
            AiConnectionStatus = success 
                ? $"✅ {message}" 
                : $"❌ {message}";
        }
        catch (Exception ex)
        {
            IsAiConnected = false;
            AiConnectionStatus = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsTestingAi = false;
        }
    }

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
        AutoAdvanceToNextTrack = c.AutoAdvanceToNextTrack;
        RememberLastPlayedTrack = c.RememberLastPlayedTrack;
        CrossfadeEnabled = c.CrossfadeEnabled;
        CrossfadeDuration = c.CrossfadeDuration;
        EnableSwipeNavigation = c.EnableSwipeNavigation;

        SelectedEqualizer = c.Equalizer;
        DefaultVolume = c.DefaultVolume;

        DefaultAspectRatio = c.DefaultAspectRatio;

        SelectedHdrMode = c.HdrMode;
        AutoBoostHdrBrightness = c.AutoBoostHdrBrightness;
        SelectedToneMappingMode = c.ToneMappingMode;
        PeakBrightnessNits = c.PeakBrightnessNits;
        ShowHdrBadge = c.ShowHdrBadge;

        SelectedBackdrop = c.BackdropType;
        SelectedAccentColor = c.AccentColor;
        AlwaysShowTransportBar = c.AlwaysShowTransportBar;

        ShowOpenFilesOnHome = c.ShowOpenFilesOnHome;
        SelectedOpenFilePositionCorner = c.OpenFilePositionCorner;

        AutomaticLibraryScan = c.AutomaticLibraryScan;

        RememberPlaybackPositionPerTrack = c.RememberPlaybackPositionPerTrack;

        HighContrastMode = c.HighContrastMode;
        TextScale = c.TextScale;
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

        AiLyricsTranslationEnabled = c.AiLyricsTranslationEnabled;
        AiTranslationTargetLanguage = c.AiTranslationTargetLanguage;
        AiSemanticSearchEnabled = c.AiSemanticSearchEnabled;
        GeminiApiKey = c.GeminiApiKey ?? "";
        UseLocalAi = c.UseLocalAi;
        OllamaModelName = c.OllamaModelName ?? "llama3.2";
        AiEqualizerMatcherEnabled = c.AiEqualizerMatcherEnabled;
        VoiceClarityEnabled = c.VoiceClarityEnabled;
        NightModeEnabled = c.NightModeEnabled;

        _isSyncing = false;

        // Notify all index properties
        OnPropertyChanged(nameof(SelectedThemeIndex));
        OnPropertyChanged(nameof(SelectedEqualizerIndex));
        OnPropertyChanged(nameof(SelectedBackdropIndex));
        OnPropertyChanged(nameof(SkipForwardIndex));
        OnPropertyChanged(nameof(SkipBackwardIndex));
        OnPropertyChanged(nameof(DefaultVolumeText));
        OnPropertyChanged(nameof(SelectedAccentColorIndex));
        OnPropertyChanged(nameof(SelectedAspectRatioIndex));
        OnPropertyChanged(nameof(SelectedOpenFilePositionCornerIndex));
        OnPropertyChanged(nameof(CrossfadeDurationText));
        OnPropertyChanged(nameof(SelectedColorBlindModeIndex));
        OnPropertyChanged(nameof(FocusIndicatorThicknessText));
        OnPropertyChanged(nameof(SelectedHdrModeIndex));
        OnPropertyChanged(nameof(SelectedToneMappingModeIndex));
        OnPropertyChanged(nameof(PeakBrightnessText));
        OnPropertyChanged(nameof(SelectedAiTranslationLanguageIndex));
        AccessibilityHelper.Apply(c);
    }
}



