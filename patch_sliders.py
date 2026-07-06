import re

with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

mapping = {
    'Crossfade duration': 'CrossfadeDuration',
    'Default startup volume': 'DefaultVolume',
    'Bass boost level': 'BassBoostLevel',
    'Audio balance': 'AudioBalance',
    'Subtitle background opacity': 'SubtitleBackgroundOpacity',
    'Focus indicator thickness': 'FocusIndicatorThickness'
}

for text, prop in mapping.items():
    pattern = rf'(<TextBlock FontSize=\"14\" Text=\"{text}\" />.*?)<Slider([^>]*)>'
    
    def repl(m):
        ts_attrs = m.group(2)
        if 'Value=' not in ts_attrs:
            ts_attrs = ts_attrs + f' Value=\"{{x:Bind ViewModel.{prop}, Mode=TwoWay}}\"'
        return f'{m.group(1)}<Slider{ts_attrs}>'
        
    content = re.sub(pattern, repl, content, flags=re.DOTALL)

with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)

print('Done slider patching')
