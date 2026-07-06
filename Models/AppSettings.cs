namespace FluentMediaPlayer.Models;

public enum OpenFileCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed class AppSettings
{
    public AppThemeOption Theme { get; set; } = AppThemeOption.Default;
    public OpenFileCorner OpenFilePositionCorner { get; set; } = OpenFileCorner.TopRight;

    public List<string> LibraryFolders { get; set; } = [];

    // ── Playback Settings ──────────────────────────────────────────
    public bool AutoplayOnLaunch { get; set; } = true;
    public bool ResumePlaybackPosition { get; set; } = true;
    public int SkipForwardInterval { get; set; } = 30;
    public int SkipBackwardInterval { get; set; } = 10;
    public double DefaultPlaybackSpeed { get; set; } = 1.0;
    public bool AutoAdvanceToNextTrack { get; set; } = true;
    public bool RememberLastPlayedTrack { get; set; } = true;
    public bool CrossfadeEnabled { get; set; } = false;
    public int CrossfadeDuration { get; set; } = 3; // seconds
    public bool GaplessPlayback { get; set; } = false;

    // ── Audio & Output Settings ────────────────────────────────────
    public EqualizerPreset Equalizer { get; set; } = EqualizerPreset.Flat;
    public bool VolumeNormalization { get; set; } = false;
    public double DefaultVolume { get; set; } = 75.0;
    public bool MonoAudio { get; set; } = false;
    public int BassBoostLevel { get; set; } = 0;
    public int AudioBalance { get; set; } = 0; // -100 (left) to +100 (right)

    // ── Video Settings ─────────────────────────────────────────────
    public AspectRatioOption DefaultAspectRatio { get; set; } = AspectRatioOption.Auto;
    public string DefaultSubtitleLanguage { get; set; } = "None";
    public SubtitleFontSize SubtitleFontSize { get; set; } = SubtitleFontSize.Medium;
    public int SubtitleBackgroundOpacity { get; set; } = 60;
    public bool HardwareAcceleration { get; set; } = true;
    public bool AutoRotateVideo { get; set; } = true;

    // ── HDR & Color Pipeline ───────────────────────────────────────
    /// <summary>Controls when HDR output is engaged.</summary>
    public HdrMode HdrMode { get; set; } = HdrMode.Auto;
    /// <summary>Tone-mapping operator used for HDR→SDR conversion or highlight roll-off.</summary>
    public ToneMappingMode ToneMappingMode { get; set; } = ToneMappingMode.Aces;
    /// <summary>Target peak brightness in nits for tone-mapping output (100–10000).</summary>
    public int PeakBrightnessNits { get; set; } = 1000;
    /// <summary>When true, MediaPlayer uses real-time mode for lower decode latency.</summary>
    public bool HdrRealTimePlayback { get; set; } = true;
    /// <summary>Show the HDR/SDR badge on the video player UI.</summary>
    public bool ShowHdrBadge { get; set; } = true;

    // ── Appearance & Visuals ───────────────────────────────────────
    public AppThemeBackdrop BackdropType { get; set; } = AppThemeBackdrop.Mica;
    public bool ShowMediaCardGlow { get; set; } = true;
    public bool ShowTimelinePreview { get; set; } = true;
    public AccentColorOption AccentColor { get; set; } = AccentColorOption.SystemDefault;
    public bool CompactDensityMode { get; set; } = false;
    public bool ShowAlbumArtInTransportBar { get; set; } = true;
    public bool AnimatedTransitions { get; set; } = true;
    public bool AlwaysShowTransportBar { get; set; } = true;

    // ── Controls & Interface ───────────────────────────────────────
    public bool EnableSwipeNavigation { get; set; } = true;
    public bool ShowShuffleButton { get; set; } = true;
    public bool ShowRepeatButton { get; set; } = true;
    public bool ShowSubtitlesButton { get; set; } = true;
    public bool ShowFullscreenButton { get; set; } = true;
    public bool ShowPipButton { get; set; } = true;
    public bool ShowQueueInMoreMenu { get; set; } = true;
    public bool ShowSpeedInMoreMenu { get; set; } = true;
    public bool ShowOpenFilesOnHome { get; set; } = true;

    // ── Media Library & Files ──────────────────────────────────────
    public bool AutomaticLibraryScan { get; set; } = true;
    public LibrarySortOrder LibrarySortOrder { get; set; } = LibrarySortOrder.Title;
    public bool ShowHiddenFiles { get; set; } = false;
    public bool AutoImportNewFiles { get; set; } = true;

    // ── Privacy & History ──────────────────────────────────────────
    public bool RememberRecentlyPlayed { get; set; } = true;
    public bool RememberPlaybackPositionPerTrack { get; set; } = true;

    // ── Accessibility ──────────────────────────────────────────
    public bool HighContrastMode { get; set; } = false;
    public bool LargeTextMode { get; set; } = false;
    public bool ReduceMotion { get; set; } = false;
    public bool ScreenReaderOptimization { get; set; } = false;
    public bool CaptionsAlwaysOn { get; set; } = false;
    public bool VisualNotificationsForSound { get; set; } = false;
    public bool KeyboardNavigationHighlight { get; set; } = true;
    public int FocusIndicatorThickness { get; set; } = 2;
    public bool AutoReadControls { get; set; } = false;
    public bool LargerClickTargets { get; set; } = false;
    public ColorBlindMode ColorBlindMode { get; set; } = ColorBlindMode.Off;
}

