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

public partial class VideoViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;
    private readonly TmdbService _tmdbService = new();
    private static readonly System.Threading.SemaphoreSlim _tmdbSemaphore = new(3, 3);
    private List<MediaItem> _rawVideos = new();
    public IReadOnlyList<MediaItem> RawVideos => _rawVideos;

    [ObservableProperty] public partial ObservableCollection<MediaItem> FilteredVideos { get; set; } = new();

    public ObservableCollection<string> SortOptions { get; } = new() { "Name (A-Z)", "Name (Z-A)", "Date Added (Newest)", "Date Added (Oldest)", "Size (Largest)", "Size (Smallest)" };
    [ObservableProperty] public partial string SelectedSort { get; set; } = "Name (A-Z)";
    partial void OnSelectedSortChanged(string value) => ApplySortAndFilter();

    public ObservableCollection<string> FilterExtensionOptions { get; } = new() { "All Formats", ".mp4", ".mkv", ".avi", ".mov", ".wmv" };
    [ObservableProperty] public partial string SelectedFilterExtension { get; set; } = "All Formats";
    partial void OnSelectedFilterExtensionChanged(string value) => ApplySortAndFilter();

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

    public VideoViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        _playback.Session.StateChanged += (_, _) => SyncFromPlayback();
        _playback.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlaybackViewModel.IsVideoPlayerActive))
            {
                SyncFromPlayback();
            }
        };

        // Subscribe to HDR pipeline state changes
        AppServices.HdrPipeline.HdrStateChanged += OnHdrStateChanged;
        ShowHdrBadge = AppServices.Settings.Current.ShowHdrBadge;

        SyncFromPlayback();
        SampleMediaLibrary.LibraryChanged += (s, e) =>
        {
            _rawVideos = SampleMediaLibrary.VideoTracks.ToList();
            _ = PopulateAllTmdbDataAsync(_rawVideos);
            ApplySortAndFilter();
        };

        _rawVideos = SampleMediaLibrary.VideoTracks.ToList();
        _ = PopulateAllTmdbDataAsync(_rawVideos);
        ApplySortAndFilter();
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
            await SampleMediaLibrary.SaveLibraryAsync();
        }
    }

    private async Task<bool> PopulateTmdbDataAsync(MediaItem item)
    {
        return await VideoMetadataHelper.PopulateTmdbDataAsync(item, _tmdbService);
    }

    private void ApplySortAndFilter()
    {
        var filtered = _rawVideos.AsEnumerable();
        
        if (SelectedFilterExtension != "All Formats")
        {
            filtered = filtered.Where(x => x.IsFolder || string.Equals(x.FileExtension, SelectedFilterExtension, StringComparison.OrdinalIgnoreCase));
        }
        
        filtered = SelectedSort switch
        {
            "Name (A-Z)" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.Title),
            "Name (Z-A)" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.Title),
            "Date Added (Newest)" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.DateAdded),
            "Date Added (Oldest)" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.DateAdded),
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
            App.MainDispatcher.TryEnqueue(() => FilteredVideos.UpdateInPlace(newItems));
        }
        else
        {
            FilteredVideos.UpdateInPlace(newItems);
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
                await SampleMediaLibrary.AddTrackAsync(item);
                _ = Helpers.MediaMetadataScanner.ScanMetadataAsync(item);
            }
            await SampleMediaLibrary.SaveLibraryAsync();
        }
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
            var options = new Windows.Storage.Search.QueryOptions(Windows.Storage.Search.CommonFileQuery.OrderByName, new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv" });
            var query = folder.CreateFileQueryWithOptions(options);
            var files = await query.GetFilesAsync();
            
            bool added = false;
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
                await SampleMediaLibrary.AddTrackAsync(item);
                _ = Helpers.MediaMetadataScanner.ScanMetadataAsync(item);
                added = true;
            }
            
            if (added)
            {
                await SampleMediaLibrary.SaveLibraryAsync();
            }
        }
    }

    public bool HasSource => !string.IsNullOrWhiteSpace(CurrentVideo?.SourcePath);

    [RelayCommand]
    private void PlayVideo(MediaItem? video)
    {
        if (video is not null)
        {
            _playback.PlayTrack(video);
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
        if (_playback.CurrentTrack is { IsVideo: true } track && _playback.IsVideoPlayerActive)
        {
            CurrentVideo = track;
            IsPlaying = _playback.IsPlaying;
            OverlayTitle = track.Title;
            OverlaySubtitle = track.Artist;
            ShowNoSourceOverlay = string.IsNullOrWhiteSpace(track.SourcePath);
            OnPropertyChanged(nameof(HasSource));
            OnPropertyChanged(nameof(OverlayVisibility));
            OnPropertyChanged(nameof(PlayerVisibility));
            OnPropertyChanged(nameof(CurrentPosterUrl));
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



