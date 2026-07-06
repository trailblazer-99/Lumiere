import re

files = ['Pages/StreamingMoviesPage.xaml', 'Pages/StreamingTvShowsPage.xaml', 'Pages/StreamingMusicPage.xaml']

for filename in files:
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    content = content.replace('Source="{x:Bind PosterUrl}"', 'Source="{Binding PosterUrl}"')
    content = content.replace('Source="{x:Bind HighResArtworkUrl}"', 'Source="{Binding HighResArtworkUrl}"')
    
    # Also fix CommandParameter
    content = content.replace('CommandParameter="{Binding ElementName=SearchBox, Path=Text}"', 'CommandParameter="{Binding Text, ElementName=SearchBox}"')
    
    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)

print("Image Bindings fixed")
