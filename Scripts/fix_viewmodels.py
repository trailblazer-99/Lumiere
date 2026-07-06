import re

vm_files = [
    'ViewModels/StreamingMusicViewModel.cs',
    'ViewModels/StreamingMoviesViewModel.cs',
    'ViewModels/StreamingTvShowsViewModel.cs'
]

for filename in vm_files:
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Remove constructor bodies for these specific viewmodels
    # For StreamingMusicViewModel
    content = re.sub(r'public StreamingMusicViewModel\(\)\s*\{\s*_\s*=\s*LoadTracksAsync\(\);\s*\}', 'public StreamingMusicViewModel() { }', content)
    
    # For StreamingMoviesViewModel
    content = re.sub(r'public StreamingMoviesViewModel\(\)\s*\{\s*_\s*=\s*LoadGenresAsync\(\);\s*_\s*=\s*LoadMoviesAsync\(\);\s*\}', 'public StreamingMoviesViewModel() { }', content)
    
    # For StreamingTvShowsViewModel
    content = re.sub(r'public StreamingTvShowsViewModel\(\)\s*\{\s*_\s*=\s*LoadGenresAsync\(\);\s*_\s*=\s*LoadTvShowsAsync\(\);\s*\}', 'public StreamingTvShowsViewModel() { }', content)
    
    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)

print("ViewModels fixed")
