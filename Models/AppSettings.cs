namespace LumiereMediaPlayer.Models;

public enum OpenFileCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public enum GpuPreference
{
    Auto = 0,
    PowerSaving = 1,
    HighPerformance = 2
}

public sealed class AppSettings
{
    public AppThemeOption Theme { get; set; } = AppThemeOption.Default;
    public OpenFileCorner OpenFilePositionCorner { get; set; } = OpenFileCorner.TopRight;

    public List<string> LibraryFolders { get; set; } = [];

    // ── Window State ──────────────────────────────────────────────────
    public double WindowWidth { get; set; } = 1200.0;
    public double WindowHeight { get; set; } = 800.0;
    public bool WindowIsMaximized { get; set; } = false;
    public int WindowPositionX { get; set; } = -1;
    public int WindowPositionY { get; set; } = -1;

    // ── Playback Settings ──────────────────────────────────────────
    public bool AutoplayOnLaunch { get; set; } = true;
    public bool ResumePlaybackPosition { get; set; } = true;
    public int SkipForwardInterval { get; set; } = 30;
    public int SkipBackwardInterval { get; set; } = 10;
    public bool AutoAdvanceToNextTrack { get; set; } = true;
    public bool RememberLastPlayedTrack { get; set; } = true;
    public bool CrossfadeEnabled { get; set; } = false;
    public int CrossfadeDuration { get; set; } = 3; // seconds

    // ── Audio & Output Settings ────────────────────────────────────
    public EqualizerPreset Equalizer { get; set; } = EqualizerPreset.Flat;
    public double DefaultVolume { get; set; } = 100.0;
    /// <summary>Enables native Windows UI interaction sound effects via ElementSoundPlayer.</summary>
    public bool EnableSoundEffects { get; set; } = true;

    // ── Video Settings ─────────────────────────────────────────────
    public AspectRatioOption DefaultAspectRatio { get; set; } = AspectRatioOption.Auto;
    /// <summary>Play an elevated animated preview of a scene when hovering over a video card.</summary>
    public bool EnableHoverVideoPreview { get; set; } = true;
    /// <summary>Decodes individual video frames for seekbar preview (uses more memory). Keep off for instant, low-memory seekbar previews.</summary>
    public bool PreciseSeekThumbnails { get; set; } = true;
    /// <summary>Preferred GPU for hardware video decoding and pipeline acceleration.</summary>
    public GpuPreference PreferredGpu { get; set; } = GpuPreference.Auto;

    // ── HDR & Color Pipeline ───────────────────────────────────────
    /// <summary>Controls when HDR output is engaged.</summary>
    public HdrMode HdrMode { get; set; } = HdrMode.Auto;
    /// <summary>Automatically boost screen brightness to 100% when HDR playback is active.</summary>
    public bool AutoBoostHdrBrightness { get; set; } = true;
    /// <summary>Emergency fallback brightness level to restore if app terminated during HDR override.</summary>
    public int? EmergencyHdrBaselineBrightness { get; set; }
    /// <summary>Tone-mapping operator used for HDR→SDR conversion or highlight roll-off.</summary>
    public ToneMappingMode ToneMappingMode { get; set; } = ToneMappingMode.DisplayAdaptive;
    /// <summary>Target peak brightness in nits for tone-mapping output (100–10000).</summary>
    public int PeakBrightnessNits { get; set; } = 1000;
    /// <summary>Show the HDR/SDR badge on the video player UI.</summary>
    public bool ShowHdrBadge { get; set; } = true;

    // ── Appearance & Visuals ───────────────────────────────────────
    public AppThemeBackdrop BackdropType { get; set; } = AppThemeBackdrop.Mica;
    public AppThemeBackdrop StudioWindowBase { get; set; } = AppThemeBackdrop.Mica;
    public StudioNavRailStyle StudioNavRail { get; set; } = StudioNavRailStyle.MicaAltTint;
    public StudioCanvasStyle StudioCanvas { get; set; } = StudioCanvasStyle.SolidLayer;
    public StudioTransportBarStyle StudioTransportBar { get; set; } = StudioTransportBarStyle.FrostedAcrylic;
    public AccentColorOption AccentColor { get; set; } = AccentColorOption.SystemDefault;
    public string? CustomAccentColorHex { get; set; }
    public string? ActiveThemePresetId { get; set; }
    public List<ThemePreset> CustomThemePresets { get; set; } = [];
    /// <summary>Whether the user has consented to creating shell shortcuts (Desktop, Start Menu).</summary>
    public bool CreateShellShortcuts { get; set; } = false;
    public bool AlwaysShowTransportBar { get; set; } = false;
    public bool AcrylicTransportBar { get; set; } = false;
    public bool AutoHideTransportBarInStreaming { get; set; } = true;
    /// <summary>Translucency percentage for fullscreen transport bar, flyouts, and overlays (0 = pure transparent, 100 = deep frosted acrylic).</summary>
    public int FullscreenControlsTranslucency { get; set; } = 0;
    public string PreferredStreamingRegion { get; set; } = "";
    /// <summary>Use Windows location services to automatically detect the streaming region.</summary>
    public bool UseLocationForRegion { get; set; } = false;

    // ── Controls & Interface ───────────────────────────────────────
    public bool EnableSwipeNavigation { get; set; } = true;
    public bool ShowOpenFilesOnHome { get; set; } = true;

    // ── Media Library & Files ──────────────────────────────────────
    public bool AutomaticLibraryScan { get; set; } = true;

    // ── Privacy & Security ────────────────────────────────────────
    public bool RememberPlaybackPositionPerTrack { get; set; } = true;
    public bool EnableAppLock { get; set; } = false;
    public bool AppLockWhenMinimized { get; set; } = false;

    // ── Accessibility ──────────────────────────────────────────
    public bool HighContrastMode { get; set; } = false;
    public double TextScale { get; set; } = 1.0;
    public bool ReduceMotion { get; set; } = false;
    public bool ScreenReaderOptimization { get; set; } = false;
    public bool CaptionsAlwaysOn { get; set; } = false;
    public bool VisualNotificationsForSound { get; set; } = false;
    public bool KeyboardNavigationHighlight { get; set; } = true;
    public int FocusIndicatorThickness { get; set; } = 2;
    public bool AutoReadControls { get; set; } = false;
    public bool LargerClickTargets { get; set; } = false;
    public ColorBlindMode ColorBlindMode { get; set; } = ColorBlindMode.Off;

    // ── AI Features Settings ───────────────────────────────────────
    public bool AiLyricsTranslationEnabled { get; set; } = false;
    public string AiTranslationTargetLanguage { get; set; } = "Hindi";
    public bool AiSemanticSearchEnabled { get; set; } = false;
    private string? _geminiApiKey;
    public string GeminiApiKey
    {
        get => _geminiApiKey ??= Helpers.SecureStorageHelper.GetSecret("GeminiApiKey");
        set => _geminiApiKey = value;
    }
    public bool UseLocalAi { get; set; } = false;
    public string OllamaModelName { get; set; } = "llama3.2";
    public bool AiEqualizerMatcherEnabled { get; set; } = false;
    public bool VoiceClarityEnabled { get; set; } = false;
    public bool NightModeEnabled { get; set; } = false;

    // ── Premium General Features ───────────────────────────────────
    public int SleepTimerMinutes { get; set; } = 0;
    public bool SleepAtEndOfTrack { get; set; } = false;
    public string CustomEqualizerGains { get; set; } = "0,0,0,0,0,0,0,0,0,0";
    public string SelectedReverbPreset { get; set; } = "None";

    // ── Updates & Maintenance ───────────────────────────────────────
    /// <summary>
    /// When true, the application periodically checks for updates in the background.
    /// Default is false to conform to user privacy and explicit update checking preferences.
    /// </summary>
    public bool AutoCheckForUpdates { get; set; } = false;

    // ── Sports & Live Events ────────────────────────────────────────
    public List<string> FavoriteSportsLeagues { get; set; } = [];
    public List<string> FavoriteSportsTeams { get; set; } = [];
}

