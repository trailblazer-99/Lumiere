import re

with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

mapping = {
    'Skip forward interval': 'SkipForwardIndex',
    'Skip backward interval': 'SkipBackwardIndex',
    'Default playback speed': 'DefaultPlaybackSpeedIndex',
    'Equalizer preset': 'SelectedEqualizerIndex',
    'Default aspect ratio': 'SelectedAspectRatioIndex',
    'Default subtitle language': 'SelectedSubtitleLanguageIndex',
    'Subtitle font size': 'SelectedSubtitleFontSizeIndex',
    'App theme': 'SelectedThemeIndex',
    'Backdrop type': 'SelectedBackdropIndex',
    'Accent color': 'SelectedAccentColorIndex',
    'Sort library by': 'SelectedLibrarySortOrderIndex',
    'Color blind mode': 'SelectedColorBlindModeIndex'
}

for text, prop in mapping.items():
    pattern = rf'(<TextBlock FontSize=\"14\" Text=\"{text}\" />.*?)<ComboBox([^>]*)>'
    
    def repl(m):
        cb_attrs = m.group(2)
        if 'SelectedIndex' not in cb_attrs:
            cb_attrs = cb_attrs + f' SelectedIndex=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"'
        return f'{m.group(1)}<ComboBox{cb_attrs}>'
        
    content = re.sub(pattern, repl, content, flags=re.DOTALL)

with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)

print('Done')
