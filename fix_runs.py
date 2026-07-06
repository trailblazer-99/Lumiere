import re

with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix TextBlocks
content = content.replace('<TextBlock Style=\"{StaticResource SubtleCaptionTextStyle}\" TextWrapping=\"Wrap\"/>', '<TextBlock Style=\"{StaticResource SubtleCaptionTextStyle}\" TextWrapping=\"Wrap\">')

# Fix Runs
content = re.sub(r'<Run ([^>]+?)>', r'<Run \1/>', content)
# Ensure we don't end up with <Run ...//>
content = content.replace('//>', '/>')
content = content.replace(' />/>', ' />')

with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
print('Fixed Runs')
