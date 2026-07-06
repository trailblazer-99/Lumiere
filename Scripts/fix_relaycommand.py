import re

files = [
    'ViewModels/StreamingMoviesViewModel.cs',
    'ViewModels/StreamingTvShowsViewModel.cs'
]

for filename in files:
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    content = content.replace('private async Task LoadGenresAsync()', '[RelayCommand]\n        private async Task LoadGenresAsync()')
    
    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)

print("Added RelayCommand to LoadGenresAsync")
