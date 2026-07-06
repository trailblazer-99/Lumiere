import re

with open('MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# UpdateLayoutForVideoMode
# Find the start of the else block inside UpdateLayoutForVideoMode
pattern_update = r'(if \(FullscreenVideoContainer != null\)\s*{\s*FullscreenVideoContainer\.Visibility = Visibility\.Collapsed;\s*if \(FullscreenVideoPlayer\.MediaPlayer != null\)\s*FullscreenVideoPlayer\.MediaPlayer\.MediaOpened -= OnFullscreenMediaOpened;\s*FullscreenVideoPlayer\.SetMediaPlayer\(null\);\s*})\s*if \(RootNavigationView != null\)\s*{.*?if \(ContentFrame\?\.Content is VideoPage vp\)\s*{\s*vp\.SyncMediaPlayer\(\);\s*}'

replacement_update = r'\1'

# We should make sure we correctly remove the RootNavigationView overrides from the else block.
content = re.sub(pattern_update, replacement_update, content, flags=re.DOTALL)

# Let's fix HideVideoControls so it only hides controls in FULLSCREEN mode!
pattern_hide = r'(private void HideVideoControls\(\)\s*{\s*)bool isVideoMode = ContentFrame\?\.Content is VideoPage && _playback\.CurrentTrack is \{ IsVideo: true \} && _playback\.IsVideoPlayerActive;\s*if \(isVideoMode\)'
replacement_hide = r'\1bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;\n        if (isFullScreen)'
content = re.sub(pattern_hide, replacement_hide, content, flags=re.DOTALL)

# Let's fix ShowVideoControls so it only affects Fullscreen mode
pattern_show = r'(private void ShowVideoControls\(\)\s*{\s*FadeElement\(AppTitleBar, 1\.0\);\s*FadeElement\(TransportControls, 1\.0\);\s*)if \(RootNavigationView != null && RootNavigationView\.PaneDisplayMode == NavigationViewPaneDisplayMode\.LeftMinimal\)\s*{\s*RootNavigationView\.IsBackButtonVisible = NavigationViewBackButtonVisible\.Visible;\s*}'
replacement_show = r'\1'
content = re.sub(pattern_show, replacement_show, content, flags=re.DOTALL)

# Also fix the OnPointerMoved, etc., so they check for Fullscreen mode before showing/hiding controls
pattern_pointer = r'bool isVideoMode = ContentFrame\?\.Content is VideoPage && _playback\.CurrentTrack is \{ IsVideo: true \} && _playback\.IsVideoPlayerActive;\s*if \(isVideoMode\)'
replacement_pointer = r'bool isFullScreen = AppWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;\n        if (isFullScreen)'
content = re.sub(pattern_pointer, replacement_pointer, content, flags=re.DOTALL)


with open('MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Updated MainWindow.xaml.cs")
