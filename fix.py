import re
with open('C:/Users/soura/source/repos/FluentMediaPlayer/obj/Debug/net8.0-windows10.0.19041.0/Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()
content = re.sub(r" x:ConnectionId='[^']+'", "", content)
content = content.replace("\n                 SECTION 1: PLAYBACK SETTINGS\n                 ", "-------------------------------------------------------\n                 SECTION 1: PLAYBACK SETTINGS\n                 -------------------------------------------------------")
content = re.sub(r'mc:Ignorable="d"(\s*)>', r'mc:Ignorable="d"\1Loaded="OnPageLoaded">', content)
with open('C:/Users/soura/source/repos/FluentMediaPlayer/Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
