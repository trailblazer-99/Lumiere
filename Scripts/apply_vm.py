import re

def fix_movies_vm():
    filepath = 'ViewModels/StreamingMoviesViewModel.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    if 'using FluentMediaPlayer.Services;' not in content:
        content = content.replace('using FluentMediaPlayer.Services.Streaming;', 'using FluentMediaPlayer.Services.Streaming;\nusing FluentMediaPlayer.Services;\nusing System.Linq;')
    
    prop = '''
        public System.Collections.ObjectModel.ObservableCollection<SavedStreamingItem> LibraryMovies => new(AppServices.StreamingLibrary.SavedItems.Where(i => i.Type == StreamingItemType.Movie));
'''
    if 'LibraryMovies' not in content:
        content = content.replace('public StreamingMoviesViewModel()', prop + '\n        public StreamingMoviesViewModel()')
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)

def fix_tv_vm():
    filepath = 'ViewModels/StreamingTvShowsViewModel.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    if 'using FluentMediaPlayer.Services;' not in content:
        content = content.replace('using FluentMediaPlayer.Services.Streaming;', 'using FluentMediaPlayer.Services.Streaming;\nusing FluentMediaPlayer.Services;\nusing System.Linq;')
    
    prop = '''
        public System.Collections.ObjectModel.ObservableCollection<SavedStreamingItem> LibraryTvShows => new(AppServices.StreamingLibrary.SavedItems.Where(i => i.Type == StreamingItemType.TvShow));
'''
    if 'LibraryTvShows' not in content:
        content = content.replace('public StreamingTvShowsViewModel()', prop + '\n        public StreamingTvShowsViewModel()')
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)

def fix_music_vm():
    filepath = 'ViewModels/StreamingMusicViewModel.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    if 'using FluentMediaPlayer.Services;' not in content:
        content = content.replace('using FluentMediaPlayer.Services.Streaming;', 'using FluentMediaPlayer.Services.Streaming;\nusing FluentMediaPlayer.Services;\nusing System.Linq;')
    
    prop = '''
        public System.Collections.ObjectModel.ObservableCollection<SavedStreamingItem> LibraryTracks => new(AppServices.StreamingLibrary.SavedItems.Where(i => i.Type == StreamingItemType.Music));
'''
    if 'LibraryTracks' not in content:
        content = content.replace('public StreamingMusicViewModel()', prop + '\n        public StreamingMusicViewModel()')
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)

fix_movies_vm()
fix_tv_vm()
fix_music_vm()
print("Done viewmodels")
