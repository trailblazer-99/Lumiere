with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('<Button Content=\"Playback history\"                                                          VerticalAlignment=\"Center\">', '<Button Content=\"Playback history\"                                                          VerticalAlignment=\"Center\"/>')
content = content.replace('<Button Content=\"Search history\"                                                        VerticalAlignment=\"Center\">', '<Button Content=\"Search history\"                                                        VerticalAlignment=\"Center\"/>')
content = content.replace('<Button Content=\"Recent files\"                                                      VerticalAlignment=\"Center\">', '<Button Content=\"Recent files\"                                                      VerticalAlignment=\"Center\"/>')

# Check if there are other unclosed buttons or tags
# For Button:
# we can just find all <Button ...> that don't have </Button> before the next < tag that closes the parent.
# A simpler way is to just compile and see what else is broken.
with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
print('Fixed buttons')
