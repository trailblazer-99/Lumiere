using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.Models.Streaming;

namespace LumiereMediaPlayer.ViewModels;

public partial class VideoViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackViewModel _playback;
    private readonly IHdrPipelineService _hdrPipeline;
    private readonly ISettingsService _settings;
    private readonly TmdbService _tmdbService = new();
    private static readonly System.Threading.SemaphoreSlim _tmdbSemaphore = new(3, 3);
    private List<MediaItem> _rawVideos = new();
    public IReadOnlyList<MediaItem> RawVideos => _rawVideos;

    private readonly EventHandler _sessionStateChangedHandler;
    private readonly System.ComponentModel.PropertyChangedEventHandler _playbackPropertyChangedHandler;
    private readonly EventHandler _libraryChangedHandler;

    [ObservableProperty] public partial ObservableCollection<MediaItem> FilteredVideos { get; set; } = new();

    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Name (A-Z)",
        "Name (Z-A)",
        "Date Added (Newest)",
        "Date Added (Oldest)",
        "Duration (Longest)",
        "Duration (Shortest)",
        "Size (Largest)",
        "Size (Smallest)"
    };
    [ObservableProperty] public partial string SelectedSort { get; set; } = "Name (A-Z)";
    partial void OnSelectedSortChanged(string value) => ApplySortAndFilter();

    [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;
    partial void OnSearchQueryChanged(string value) => ApplySortAndFilter();

    public ObservableCollection<string> FilterExtensionOptions { get; } = new() { "All Formats", "Favorites", ".mp4", ".mkv", ".avi", ".mov", ".wmv" };
    [ObservableProperty] public partial string SelectedFilterExtension { get; set; } = "All Formats";
    partial void OnSelectedFilterExtensionChanged(string value) => ApplySortAndFilter();

    [ObservableProperty] public partial bool ShowFavoritesOnly { get; set; }
    partial void OnShowFavoritesOnlyChanged(bool value) => ApplySortAndFilter();

    [ObservableProperty] public partial MediaItem? CurrentVideo { get; set; }

    [ObservableProperty] public partial bool IsPlaying { get; set; }

    [ObservableProperty] public partial string OverlayTitle { get; set; } = "Select a video to play";

    [ObservableProperty] public partial string OverlaySubtitle { get; set; } = "Choose from your library below";

    [ObservableProperty] public partial bool ShowNoSourceOverlay { get; set; } = true;

    // ── HDR status ─────────────────────────────────────────────────

    [ObservableProperty] public partial bool IsHdrActive { get; set; }
    [ObservableProperty] public partial string HdrContentLabel { get; set; } = "SDR";
    [ObservableProperty] public partial string DisplayCapabilityLabel { get; set; } = "SDR Display";
    [ObservableProperty] public partial bool ShowHdrBadge { get; set; }

    public Visibility HdrBadgeVisibility => VisibilityHelper.FromBoolean(ShowHdrBadge && IsHdrActive);
    public Visibility OverlayVisibility => VisibilityHelper.FromBoolean(ShowNoSourceOverlay);
    public Visibility PlayerVisibility => VisibilityHelper.FromBoolean(!ShowNoSourceOverlay);

    public VideoViewModel(PlaybackViewModel playback, IHdrPipelineService hdrPipeline, ISettingsService settings)
    {
        _playback = playback;
        _hdrPipeline = hdrPipeline;
        _settings = settings;

        _sessionStateChangedHandler = (_, _) => SyncFromPlayback();
        _playback.Session.StateChanged += _sessionStateChangedHandler;

        _playbackPropertyChangedHandler = (s, e) =>
        {
            if (e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive))
            {
                SyncFromPlayback();
            }
        };
        _playback.PropertyChanged += _playbackPropertyChangedHandler;

        // Subscribe to HDR pipeline state changes
        _hdrPipeline.HdrStateChanged += OnHdrStateChanged;
        ShowHdrBadge = _settings.Current.ShowHdrBadge;

        SyncFromPlayback();
        _libraryChangedHandler = (s, e) =>
        {
            _rawVideos = MediaLibraryService.VideoTracks.ToList();
            _ = PopulateAllTmdbDataAsync(_rawVideos);
            ApplySortAndFilter();
        };
        MediaLibraryService.LibraryChanged += _libraryChangedHandler;

        _rawVideos = MediaLibraryService.VideoTracks.ToList();
        _ = PopulateAllTmdbDataAsync(_rawVideos);
        ApplySortAndFilter();
    }

    public void Dispose()
    {
        _playback.Session.StateChanged -= _sessionStateChangedHandler;
        _playback.PropertyChanged -= _playbackPropertyChangedHandler;
        _hdrPipeline.HdrStateChanged -= OnHdrStateChanged;
        MediaLibraryService.LibraryChanged -= _libraryChangedHandler;
    }

    public VideoViewModel(PlaybackViewModel playback)
        : this(playback, AppServices.HdrPipeline, AppServices.Settings)
    {
    }

    private async Task PopulateAllTmdbDataAsync(List<MediaItem> items)
    {
        bool anyModified = false;
        var tasks = items.Select(async item =>
        {
            await _tmdbSemaphore.WaitAsync();
            try
            {
                bool modified = await PopulateTmdbDataAsync(item);
                if (modified) anyModified = true;
            }
            finally
            {
                _tmdbSemaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
        if (anyModified)
        {
            await MediaLibraryService.SaveLibraryAsync();
        }
    }

    private async Task<bool> PopulateTmdbDataAsync(MediaItem item)
    {
        if (item.IsSeries)
        {
            try
            {
                var tvResults = await _tmdbService.SearchTvShowsAsync(item.Title);
                var show = VideoMetadataHelper.SelectBestMatch(tvResults, item.Title);
                if (show != null)
                {
                    bool modified = false;
                    if (!string.IsNullOrEmpty(show.PosterPath))
                    {
                        item.PosterUrl = $"https://image.tmdb.org/t/p/w500{show.PosterPath}";
                        modified = true;
                    }
                    if (!string.IsNullOrEmpty(show.Overview))
                    {
                        item.Description = show.Overview;
                        modified = true;
                    }

                    if (item.Episodes != null)
                    {
                        foreach (var ep in item.Episodes)
                        {
                            var epLookup = VideoMetadataHelper.TryCreateEpisodeLookup(ep);
                            int seasonNum = epLookup?.SeasonNumber ?? ep.SeasonNumber;
                            int episodeNum = epLookup?.EpisodeNumber ?? ep.EpisodeNumber;
                            var tmdbEp = await _tmdbService.GetTvEpisodeAsync(show.Id, seasonNum, episodeNum);
                            if (tmdbEp != null)
                            {
                                if (!string.IsNullOrEmpty(tmdbEp.Name)) ep.EpisodeTitle = tmdbEp.Name;
                                if (!string.IsNullOrEmpty(tmdbEp.StillPath)) ep.EpisodeStillUrl = $"https://image.tmdb.org/t/p/w300{tmdbEp.StillPath}";
                                modified = true;
                            }
                        }
                    }
                    return modified;
                }
            }
            catch { }
            return false;
        }

        return await VideoMetadataHelper.PopulateTmdbDataAsync(item, _tmdbService);
    }

    private void ApplySortAndFilter()
    {
        var consolidated = TvShowHelper.ConsolidateVideoLibrary(_rawVideos);
        var filtered = consolidated.AsEnumerable();

        if (ShowFavoritesOnly || SelectedFilterExtension == "Favorites")
        {
            filtered = filtered.Where(x => x.IsFavorite);
        }
        else if (SelectedFilterExtension != "All Formats")
        {
            filtered = filtered.Where(x => x.IsFolder || 
                string.Equals(x.FileExtension, SelectedFilterExtension, StringComparison.OrdinalIgnoreCase) ||
                (x.IsSeries && x.Episodes?.Any(e => string.Equals(e.FileExtension, SelectedFilterExtension, StringComparison.OrdinalIgnoreCase)) == true));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim();
            filtered = filtered.Where(x =>
                (x.Title != null && x.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (x.Artist != null && x.Artist.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (x.IsSeries && x.Episodes != null && x.Episodes.Any(e => e.Title != null && e.Title.Contains(q, StringComparison.OrdinalIgnoreCase))));
        }

        filtered = SelectedSort switch
        {
            "Name (A-Z)" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.Title),
            "Name (Z-A)" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.Title),
            "Date Added (Newest)" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.DateAdded),
            "Date Added (Oldest)" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.DateAdded),
            "Duration (Longest)" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.Duration),
            "Duration (Shortest)" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.Duration),
            "Size (Largest)" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.FileSize),
            "Size (Smallest)" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.FileSize),
            _ => filtered
        };

        var newItems = filtered.ToList();
        if (FilteredVideos.SequenceEqual(newItems))
        {
            return;
        }

        if (App.MainDispatcher != null && !App.MainDispatcher.HasThreadAccess)
        {
            App.MainDispatcher.TryEnqueue(() =>
            {
                FilteredVideos.UpdateInPlace(newItems);
                OnPropertyChanged(nameof(FilteredVideos));
            });
        }
        else
        {
            FilteredVideos.UpdateInPlace(newItems);
            OnPropertyChanged(nameof(FilteredVideos));
        }
    }

    [RelayCommand]
    public async Task AddFilesAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        FilePickerHelper.Initialize(picker);
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".avi");
        picker.FileTypeFilter.Add(".mov");
        picker.FileTypeFilter.Add(".wmv");

        var files = await picker.PickMultipleFilesAsync();
        if (files != null && files.Count > 0)
        {
            foreach (var file in files)
            {
                var props = await file.GetBasicPropertiesAsync();
                var item = new MediaItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = file.DisplayName,
                    SourcePath = file.Path,
                    Kind = MediaKind.Video,
                    FileSize = (long)props.Size,
                    DateCreated = props.ItemDate.DateTime,
                    DateAdded = DateTime.Now,
                    IsFolder = false,
                    FileExtension = file.FileType
                };
                await MediaLibraryService.AddTrackAsync(item);
                _ = Helpers.MediaMetadataScanner.ScanMetadataAsync(item);
            }
            await MediaLibraryService.SaveLibraryAsync();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await MediaLibraryService.SynchronizeLibraryMediaAsync();
        _rawVideos = MediaLibraryService.VideoTracks.ToList();
        ApplySortAndFilter();
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        FilePickerHelper.Initialize(picker);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            AppServices.Settings.AddLibraryFolder(folder.Path);
            MediaLibraryService.StartWatchingDirectory(folder.Path);
            await MediaLibraryService.ScanFolderAsync(folder);
        }
    }

    [RelayCommand]
    public async Task ClearAllVideosAsync()
    {
        if (_playback.CurrentTrack is { IsVideo: true })
        {
            _playback.Stop();
        }

        await MediaLibraryService.ClearVideoTracksAsync();
        _rawVideos.Clear();
        FilteredVideos.Clear();
        CurrentVideo = null;
        IsPlaying = false;
        ShowNoSourceOverlay = true;
        OverlayTitle = "Select a video to play";
        OverlaySubtitle = "Choose from your library below";
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(OverlayVisibility));
        OnPropertyChanged(nameof(PlayerVisibility));
        OnPropertyChanged(nameof(CurrentPosterUrl));
    }

    public bool HasSource => !string.IsNullOrWhiteSpace(CurrentVideo?.SourcePath);

    [RelayCommand]
    public void PlayVideo(MediaItem? video)
    {
        if (video is null) return;
        App.MainWindowInstance?.VideoHoverPreviewControl?.ClosePreview();

        // Deselect any selected items so selection ribbons do not persist into player mode
        foreach (var v in FilteredVideos)
        {
            v.IsSelected = false;
        }
        foreach (var v in _rawVideos)
        {
            v.IsSelected = false;
        }

        // Instantly switch UI to player mode (0ms latency feedback)
        _playback.IsVideoPlayerActive = true;
        CurrentVideo = video;
        IsPlaying = true;
        ShowNoSourceOverlay = false;
        OverlayTitle = video.Title;
        OverlaySubtitle = video.Artist;
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(OverlayVisibility));
        OnPropertyChanged(nameof(PlayerVisibility));
        OnPropertyChanged(nameof(CurrentPosterUrl));

        if (video.IsSeries && video.Episodes?.Count > 0)
        {
            _playback.SetQueue(video.Episodes, 0);
        }
        else
        {
            var parentSeries = FilteredVideos.FirstOrDefault(s => s.IsSeries && s.Episodes?.Any(e => 
                e.Equals(video) || 
                (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, video.Id, StringComparison.Ordinal)) || 
                (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, video.SourcePath, StringComparison.OrdinalIgnoreCase))) == true);
            if (parentSeries?.Episodes != null)
            {
                int epIdx = parentSeries.Episodes.FindIndex(e => 
                    e.Equals(video) || 
                    (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, video.Id, StringComparison.Ordinal)) || 
                    (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, video.SourcePath, StringComparison.OrdinalIgnoreCase)));
                _playback.SetQueue(parentSeries.Episodes, Math.Max(0, epIdx));
            }
            else
            {
                int index = FilteredVideos.IndexOf(video);
                if (index >= 0)
                {
                    _playback.SetQueue(FilteredVideos, index);
                }
                else
                {
                    _playback.SetQueue(new[] { video }, 0);
                }
            }
        }
    }

    public void PlayEpisodeFromSeries(MediaItem? series, MediaItem episode)
    {
        if (episode == null) return;

        // Deselect any selected items so selection ribbons do not persist into player mode
        foreach (var v in FilteredVideos)
        {
            v.IsSelected = false;
        }
        foreach (var v in _rawVideos)
        {
            v.IsSelected = false;
        }

        // Instantly switch UI to player mode (0ms latency feedback)
        _playback.IsVideoPlayerActive = true;
        CurrentVideo = episode;
        IsPlaying = true;
        ShowNoSourceOverlay = false;
        OverlayTitle = !string.IsNullOrWhiteSpace(episode.EpisodeTitle)
            ? $"{episode.EpisodeNumber}. {episode.EpisodeTitle}"
            : (!string.IsNullOrWhiteSpace(episode.Title) ? episode.Title : "Episode");
        OverlaySubtitle = !string.IsNullOrWhiteSpace(episode.Artist)
            ? episode.Artist
            : (series?.Title ?? "TV Show");
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(OverlayVisibility));
        OnPropertyChanged(nameof(PlayerVisibility));
        OnPropertyChanged(nameof(CurrentPosterUrl));

        var parentSeries = series ?? FilteredVideos.FirstOrDefault(s => s.IsSeries && s.Episodes?.Any(e => 
            e.Equals(episode) || 
            (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, episode.Id, StringComparison.Ordinal)) || 
            (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, episode.SourcePath, StringComparison.OrdinalIgnoreCase))) == true);
        
        var episodesList = parentSeries?.Episodes;

        if (episodesList != null && episodesList.Count > 0)
        {
            int epIdx = episodesList.FindIndex(e =>
                e.Equals(episode) ||
                (!string.IsNullOrEmpty(e.Id) && string.Equals(e.Id, episode.Id, StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(e.SourcePath) && string.Equals(e.SourcePath, episode.SourcePath, StringComparison.OrdinalIgnoreCase)));

            _playback.SetQueue(episodesList, Math.Max(0, epIdx));
        }
        else
        {
            _playback.SetQueue(new[] { episode }, 0);
        }
    }

    private void OnHdrStateChanged(object? sender, HdrStateChangedEventArgs e)
    {
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            IsHdrActive = e.IsHdrActive;
            HdrContentLabel = AppServices.HdrPipeline.ContentFormatLabel;
            DisplayCapabilityLabel = AppServices.HdrPipeline.DisplayCapabilityLabel;
            ShowHdrBadge = AppServices.Settings.Current.ShowHdrBadge;
            OnPropertyChanged(nameof(HdrBadgeVisibility));
        });
    }

    public string? CurrentPosterUrl => CurrentVideo?.PosterUrl;

    private void SyncFromPlayback()
    {
        if (_playback.IsVideoPlayerActive)
        {
            if (_playback.CurrentTrack is { IsVideo: true } track)
            {
                if (CurrentVideo != track)
                {
                    CurrentVideo = track;
                }
                IsPlaying = _playback.IsPlaying;
                OverlayTitle = track.Title;
                OverlaySubtitle = track.Artist;
                ShowNoSourceOverlay = string.IsNullOrWhiteSpace(track.SourcePath);
                OnPropertyChanged(nameof(HasSource));
                OnPropertyChanged(nameof(OverlayVisibility));
                OnPropertyChanged(nameof(PlayerVisibility));
                OnPropertyChanged(nameof(CurrentPosterUrl));
            }
            return;
        }

        CurrentVideo = null;
        IsPlaying = false;
        OverlayTitle = "Select a video to play";
        OverlaySubtitle = "Choose from your library below";
        ShowNoSourceOverlay = true;
        IsHdrActive = false;
        HdrContentLabel = "SDR";
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(OverlayVisibility));
        OnPropertyChanged(nameof(PlayerVisibility));
        OnPropertyChanged(nameof(HdrBadgeVisibility));
        OnPropertyChanged(nameof(CurrentPosterUrl));
    }

    private sealed record EpisodeLookup(string SeriesTitle, int SeasonNumber, int EpisodeNumber);
}



