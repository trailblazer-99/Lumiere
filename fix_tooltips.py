import re

files = ['Pages/StreamingMoviesPage.xaml.cs', 'Pages/StreamingTvShowsPage.xaml.cs']

for filename in files:
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    pattern = r'providerList\.Children\.Add\(new Image \{ Source = new Microsoft\.UI\.Xaml\.Media\.Imaging\.BitmapImage\(new Uri\(p\.LogoUrl\)\), Width = 40, Height = 40, ToolTipService = \{ ToolTip = p\.ProviderName \} \}\);'
    
    replacement = r'''var img = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p.LogoUrl)), Width = 40, Height = 40 };
                            ToolTipService.SetToolTip(img, p.ProviderName);
                            providerList.Children.Add(img);'''
    
    content = re.sub(pattern, replacement, content)
    
    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)

print("Tooltips fixed")
