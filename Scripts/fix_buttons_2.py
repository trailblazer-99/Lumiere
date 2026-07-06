with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('<Button Content=\"Reset history &amp; cache\"                                                          VerticalAlignment=\"Center\">', '<Button Content=\"Reset history &amp; cache\"                                                          VerticalAlignment=\"Center\"/>')
content = content.replace('<Button Content=\"Factory reset\"                                                  Style=\"{StaticResource AccentButtonStyle}\" VerticalAlignment=\"Center\">', '<Button Content=\"Factory reset\"                                                  Style=\"{StaticResource AccentButtonStyle}\" VerticalAlignment=\"Center\"/>')

with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
print('Fixed buttons 2')
