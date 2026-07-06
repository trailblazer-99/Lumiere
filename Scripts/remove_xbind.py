import re

files = ['Pages/StreamingMoviesPage.xaml', 'Pages/StreamingTvShowsPage.xaml', 'Pages/StreamingMusicPage.xaml']

for filename in files:
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    content = content.replace('{x:Bind ', '{Binding ')
    
    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)

print("x:Bind replaced with Binding")
