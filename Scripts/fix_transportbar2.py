import re

with open('Controls/TransportBar.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

pattern = r'BorderThickness="0,1,0,0">\s*Background="\{ThemeResource AcrylicBackgroundFillColorDefaultBrush\}"'
replacement = r'BorderThickness="0,1,0,0"\n        Background="{ThemeResource AcrylicBackgroundFillColorDefaultBrush}">'

content = re.sub(pattern, replacement, content)

with open('Controls/TransportBar.xaml', 'w', encoding='utf-8') as f:
    f.write(content)

print("Fixed XAML syntax")
