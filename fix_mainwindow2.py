import re

with open('MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

pattern = r'(private void UpdateLayoutForVideoMode\(\)\s*\{\s*bool isPip = AppWindow\?\.Presenter\?\.Kind == AppWindowPresenterKind\.CompactOverlay;\s*if \(isPip\) return;\s*bool isVideoMode = ContentFrame\?\.Content is VideoPage && _playback\.CurrentTrack is \{ IsVideo: true \} &&\s*_playback\.IsVideoPlayerActive;\s*bool isFullScreen = AppWindow\?\.Presenter\?\.Kind == AppWindowPresenterKind\.FullScreen;\s*)if \(isVideoMode\)'

replacement = r'\1if (isVideoMode && isFullScreen)'

content = re.sub(pattern, replacement, content, flags=re.DOTALL)

pattern2 = r'(// Reset composition visual opacity\s*var visual = Microsoft\.UI\.Xaml\.Hosting\.ElementCompositionPreview\.GetElementVisual\(TransportControls\);\s*visual\.Opacity = 1\.0f;\s*\})'
replacement2 = r'\1\n\n            if (RootGrid != null && RootGrid.RowDefinitions.Count > 1)\n            {\n                RootGrid.RowDefinitions[1].Height = GridLength.Auto;\n            }'

content = re.sub(pattern2, replacement2, content, flags=re.DOTALL)

with open('MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Updated MainWindow.xaml.cs")
