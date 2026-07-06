import re

with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

mapping = {
    'Autoplay on launch': 'AutoplayOnLaunch',
    'Resume playback position': 'ResumePlaybackPosition',
    'Auto-advance to next track': 'AutoAdvanceToNextTrack',
    'Remember last played track': 'RememberLastPlayedTrack',
    'Crossfade between tracks': 'CrossfadeEnabled',
    'Gapless playback': 'GaplessPlayback',
    'Volume normalization': 'VolumeNormalization',
    'Mono audio': 'MonoAudio',
    'Hardware acceleration': 'HardwareAcceleration',
    'Auto-rotate video': 'AutoRotateVideo',
    'Show media card glow': 'ShowMediaCardGlow',
    'Show timeline preview on hover': 'ShowTimelinePreview',
    'Compact density mode': 'CompactDensityMode',
    'Show album art in transport bar': 'ShowAlbumArtInTransportBar',
    'Animated transitions': 'AnimatedTransitions',
    'Always show transport bar': 'AlwaysShowTransportBar',
    'Show shuffle button': 'ShowShuffleButton',
    'Show repeat button': 'ShowRepeatButton',
    'Show subtitles button': 'ShowSubtitlesButton',
    'Show fullscreen button': 'ShowFullscreenButton',
    'Show Picture in Picture button': 'ShowPipButton',
    'Show queue in more menu': 'ShowQueueInMoreMenu',
    'Show speed controls in more menu': 'ShowSpeedInMoreMenu',
    'Show open files button on home page': 'ShowOpenFilesOnHome',
    'Automatic library scan on launch': 'AutomaticLibraryScan',
    'Show hidden files': 'ShowHiddenFiles',
    'Auto-import new files': 'AutoImportNewFiles',
    'Remember recently played': 'RememberRecentlyPlayed',
    'Remember playback position per track': 'RememberPlaybackPositionPerTrack',
    'High contrast mode': 'HighContrastMode',
    'Large text mode': 'LargeTextMode',
    'Reduce motion': 'ReduceMotion',
    'Screen reader optimization': 'ScreenReaderOptimization',
    'Captions always on': 'CaptionsAlwaysOn',
    'Visual notifications for sound': 'VisualNotificationsForSound',
    'Keyboard navigation highlight': 'KeyboardNavigationHighlight',
    'Auto-read controls': 'AutoReadControls',
    'Larger click targets': 'LargerClickTargets'
}

for text, prop in mapping.items():
    pattern = rf'(<TextBlock FontSize=\"14\" Text=\"{text}\" />.*?)<ToggleSwitch([^>]*)>'
    
    def repl(m):
        ts_attrs = m.group(2)
        if 'IsOn=' not in ts_attrs:
            ts_attrs = ts_attrs + f' IsOn=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"'
        return f'{m.group(1)}<ToggleSwitch{ts_attrs}>'
        
    content = re.sub(pattern, repl, content, flags=re.DOTALL)

with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)

print('Done toggle patching')
