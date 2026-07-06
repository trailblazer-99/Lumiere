with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('/ IsOn=', 'IsOn=').replace('/ SelectedIndex=', 'SelectedIndex=').replace('/ Value=', 'Value=').replace('\">', '\" />')

# But wait, replacing \"> with \" /> will break everything else that ends with \">.
# Let's use regex properly.
import re
content = re.sub(r'/ (IsOn|SelectedIndex|Value)=\"([^\"]+)\">', r'\1=\"\2\" />', content)

with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
