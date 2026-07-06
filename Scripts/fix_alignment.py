import re

def fix_alignment(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Find the filter bar stack panel and replace VerticalAlignment="Center" with VerticalAlignment="Bottom"
    content = content.replace('VerticalAlignment="Center"', 'VerticalAlignment="Bottom"')
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_alignment('Pages/StreamingMoviesPage.xaml')
fix_alignment('Pages/StreamingTvShowsPage.xaml')
print('Alignment fixed.')
