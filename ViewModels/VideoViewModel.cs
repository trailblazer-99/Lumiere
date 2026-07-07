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
    private List<MediaItem> _rawVideos = new();

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
            foreach (var v in _rawVideos) { _ = PopulateTmdbDataAsync(v); }
            ApplySortAndFilter();
        };

        _rawVideos = SampleMediaLibrary.VideoTracks.ToList();
        foreach (var v in _rawVideos) { _ = PopulateTmdbDataAsync(v); }
        ApplySortAndFilter();
    }

    private string CleanVideoTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return string.Empty;

        string title = Path.GetFileNameWithoutExtension(rawTitle);

        // Replace dots, underscores, hyphens with spaces
        title = title.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        // Regex patterns for common torrent release group/quality tags
        string[] tags = new string[] {
            "1080p", "720p", "480p", "2160p", "4k", "bluray", "bdrip", "brrip", "webrip", "web-rip",
            "webdl", "web-dl", "dvdrip", "hdrip", "hdtv", "x264", "x265", "h264", "hevc", "aac",
            "dts", "dd5", "ddp5", "ddp", "ac3", "yts", "yify", "axxo", "subbed", "dubbed",
            "multi", "dual-audio", "dual audio", "dual", "criterion", "remastered", "extended",
            "directors cut", "director's cut", "unrated", "proper", "repack"
        };

        foreach (var tag in tags)
        {
            title = Regex.Replace(title, @"\b" + Regex.Escape(tag) + @"\b", " ", RegexOptions.IgnoreCase);
        }

        // Clean up any year like 19xx or 20xx and strip everything after it
        var yearMatch = Regex.Match(title, @"\b(19|20)\d{2}\b");
        if (yearMatch.Success)
        {
            title = title.Substring(0, yearMatch.Index);
        }

        // Clean up double spaces and trim
        title = Regex.Replace(title, @"\s+", " ").Trim();

        return title;
    }

    private static string NormalizeLookupText(string value)
    {
        value = Path.GetFileNameWithoutExtension(value);
        value = value.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        value = Regex.Replace(value, @"\[[^\]]*\]|\([^\)]*\)", " ");
        value = Regex.Replace(value, @"\b(19|20)\d{2}\b", " ");
        value = Regex.Replace(value, @"\b(season|series)\s*\d+\b", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bS\d{1,2}\b", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static int? TryInferSeasonFromPath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        try
        {
            var directory = Path.GetDirectoryName(sourcePath);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var name = Path.GetFileName(directory);
                var match = Regex.Match(name, @"\b(?:season|series)\s*(\d{1,2})\b|\bS(\d{1,2})\b", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (int.TryParse(value, out var season)) return season;
                }

                directory = Path.GetDirectoryName(directory);
            }
        }
        catch
        {
        }

        return null;
    }

    private static string InferSeriesTitleFromPath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;

        try
        {
            var directory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(directory)) return string.Empty;

            var folderName = Path.GetFileName(directory);
            if (Regex.IsMatch(folderName, @"\b(?:season|series)\s*\d{1,2}\b|\bS\d{1,2}\b", RegexOptions.IgnoreCase))
            {
                var parent = Path.GetDirectoryName(directory);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    folderName = Path.GetFileName(parent);
                }
            }

            return NormalizeLookupText(folderName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private EpisodeLookup? TryCreateEpisodeLookup(MediaItem item)
    {
        var filename = Path.GetFileNameWithoutExtension(item.SourcePath ?? item.Title);
        if (string.IsNullOrWhiteSpace(filename)) return null;

        var season = 0;
        var episode = 0;
        var seriesTitle = string.Empty;

        var match = Regex.Match(
            filename,
            @"^(?<series>.*?)\bS(?<season>\d{1,2})\s*E(?<episode>\d{1,3})\b",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            match = Regex.Match(
                filename,
                @"^(?<series>.*?)\b(?<season>\d{1,2})x(?<episode>\d{1,3})\b",
                RegexOptions.IgnoreCase);
        }

        if (!match.Success)
        {
            match = Regex.Match(
                filename,
                @"^(?<series>.*?)\bSeason\s*(?<season>\d{1,2})\s*Episode\s*(?<episode>\d{1,3})\b",
                RegexOptions.IgnoreCase);
        }

        if (match.Success)
        {
            int.TryParse(match.Groups["season"].Value, out season);
            int.TryParse(match.Groups["episode"].Value, out episode);
            seriesTitle = NormalizeLookupText(match.Groups["series"].Value);
        }
        else
        {
            match = Regex.Match(
                filename,
                @"\bEpisode\s*(?<episode>\d{1,3})\b(?:\s*[-:]\s*(?<episodeTitle>.+))?",
                RegexOptions.IgnoreCase);

            if (!match.Success) return null;

            season = TryInferSeasonFromPath(item.SourcePath) ?? 1;
            int.TryParse(match.Groups["episode"].Value, out episode);
        }

        if (season <= 0 || episode <= 0) return null;

        if (string.IsNullOrWhiteSpace(seriesTitle))
        {
            seriesTitle = InferSeriesTitleFromPath(item.SourcePath);
        }

        if (string.IsNullOrWhiteSpace(seriesTitle)) return null;

        return new EpisodeLookup(seriesTitle, season, episode);
    }

    private static TmdbMedia? SelectBestMatch(IEnumerable<TmdbMedia> results, string query)
    {
        var normalizedQuery = NormalizeLookupText(query);
        return results
            .OrderByDescending(result => string.Equals(NormalizeLookupText(result.DisplayTitle), normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(result => result.VoteAverage)
            .FirstOrDefault();
    }

    private async Task PopulateTmdbDataAsync(MediaItem item)
    {
        if (item.IsFolder) return;
        var episodeLookup = TryCreateEpisodeLookup(item);
        if (!string.IsNullOrEmpty(item.PosterUrl) && episodeLookup == null) return;
        
        // Default fallback to show file format
        if (string.IsNullOrEmpty(item.ReleaseYear))
        {
            item.ReleaseYear = !string.IsNullOrEmpty(item.FileExtension) ? item.FileExtension.TrimStart('.').ToUpper() : "VIDEO";
        }

        try
        {
            if (episodeLookup != null)
            {
                var tvResults = await _tmdbService.SearchTvShowsAsync(episodeLookup.SeriesTitle);
                var show = SelectBestMatch(tvResults, episodeLookup.SeriesTitle);
                if (show == null) return;

                var episode = await _tmdbService.GetTvEpisodeAsync(show.Id, episodeLookup.SeasonNumber, episodeLookup.EpisodeNumber);

                if (!string.IsNullOrEmpty(show.PosterPath))
                {
                    item.PosterUrl = $"https://image.tmdb.org/t/p/w500{show.PosterPath}";
                }

                item.Director = $"TV Episode S{episodeLookup.SeasonNumber:00}E{episodeLookup.EpisodeNumber:00}";

                var airDate = episode?.AirDate;
                if (!string.IsNullOrEmpty(airDate) && airDate.Length >= 4)
                {
                    item.ReleaseYear = airDate.Substring(0, 4);
                }
                else if (!string.IsNullOrEmpty(show.FirstAirDate) && show.FirstAirDate.Length >= 4)
                {
                    item.ReleaseYear = show.FirstAirDate.Substring(0, 4);
                }

                item.Genre = !string.IsNullOrWhiteSpace(episode?.Overview) ? episode.Overview : show.Overview;

                await SampleMediaLibrary.SaveLibraryAsync();
                return;
            }

            var filename = Path.GetFileNameWithoutExtension(item.Title);
            var cleanTitle = CleanVideoTitle(filename);
            if (string.IsNullOrWhiteSpace(cleanTitle)) return;

            var results = await _tmdbService.SearchMoviesAsync(cleanTitle);
            
            var bestMatch = SelectBestMatch(results, cleanTitle);
            if (bestMatch != null)
            {
                if (!string.IsNullOrEmpty(bestMatch.PosterPath))
                {
                    item.PosterUrl = $"https://image.tmdb.org/t/p/w500{bestMatch.PosterPath}";
                }
                
                item.Director = "Movie";
                
                if (!string.IsNullOrEmpty(bestMatch.ReleaseDate) && bestMatch.ReleaseDate.Length >= 4)
                {
                    item.ReleaseYear = bestMatch.ReleaseDate.Substring(0, 4);
                }
                else if (!string.IsNullOrEmpty(bestMatch.FirstAirDate) && bestMatch.FirstAirDate.Length >= 4)
                {
                    item.ReleaseYear = bestMatch.FirstAirDate.Substring(0, 4);
                }

                if (!string.IsNullOrEmpty(bestMatch.Overview))
                {
                    item.Genre = bestMatch.Overview; // Store description in Genre for tooltip/info fallback
                }

                await SampleMediaLibrary.SaveLibraryAsync();
            }
        }
        catch { }
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
        
        FilteredVideos.Clear();
        foreach (var item in filtered)
        {
            FilteredVideos.Add(item);
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
    }

    private sealed record EpisodeLookup(string SeriesTitle, int SeasonNumber, int EpisodeNumber);
}



