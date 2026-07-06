import re

with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

content = re.sub(r'<GradientStop ([^>]+?)>', r'<GradientStop \1/>', content)
content = content.replace('//>', '/>')
content = content.replace(' />/>', ' />')

with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
print('Fixed GradientStop')
