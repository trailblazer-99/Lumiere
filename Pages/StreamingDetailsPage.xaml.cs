using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Pages
{
    public class TreeViewItemContent
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public Visibility SubtitleVisibility => string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DescriptionVisibility => string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        
        public WatchmodeEpisode? Episode { get; set; }
    }

    public sealed partial class StreamingDetailsPage : Page
    {
        private readonly WatchmodeService _watchmodeService = new();
        private int _watchmodeId;
        private string _selectedRegion = "";
        private WatchmodeDetails? _details;
        private bool _isSaved;

        public string? CurrentTitleType => _details?.Type;

        public StreamingDetailsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            try
            {
                base.OnNavigatedTo(e);

                var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation("PosterAnimation");
                if (animation != null)
                {
                    animation.TryStart(PosterImage);
                }

                if (e.Parameter is int id)
                {
                    _watchmodeId = id;
                    _selectedRegion = "";
                }
                else if (e.Parameter is (int tupleId, string region))
                {
                    _watchmodeId = tupleId;
                    _selectedRegion = region;
                }

                if (string.IsNullOrEmpty(_selectedRegion))
                {
                    _selectedRegion = AppServices.StreamingMoviesViewModel?.SelectedRegion ?? "";
                }
                if (string.IsNullOrEmpty(_selectedRegion))
                {
                    _selectedRegion = await RegionHelper.GetCurrentRegionAsync();
                }

                await LoadDetailsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private async Task LoadDetailsAsync()
        {
            // parallel fetch to optimize load time
            var detailsTask = _watchmodeService.GetDetailsAsync(_watchmodeId);
            var castTask = _watchmodeService.GetCastCrewAsync(_watchmodeId);
            var seasonsTask = _watchmodeService.GetSeasonsAsync(_watchmodeId);
            var episodesTask = _watchmodeService.GetEpisodesAsync(_watchmodeId);
            var sourcesTask = _watchmodeService.GetSourcesAsync(_watchmodeId, _selectedRegion);
            var similarTask = _watchmodeService.GetSimilarTitlesAsync(_watchmodeId);
            var scoresTask = _watchmodeService.GetScoresAsync(_watchmodeId);
            var releasesTask = _watchmodeService.GetReleasesAsync(_watchmodeId);

            await Task.WhenAll(detailsTask, castTask, seasonsTask, episodesTask, sourcesTask, similarTask, scoresTask, releasesTask);

            _details = await detailsTask;
            var cast = await castTask;
            var seasons = await seasonsTask;
            var episodes = await episodesTask;
            var sources = await sourcesTask;
            var similarTitles = await similarTask;
            var scores = await scoresTask;
            var releases = await releasesTask;

            if (_details == null)
            {
                TitleText.Text = "Failed to load details.";
                return;
            }

            // Populate text fields
            TitleText.Text = _details.Title ?? "Unknown Title";
            YearText.Text = _details.Year?.ToString() ?? string.Empty;
            RatingText.Text = _details.UserRating != null ? $"⭐ {_details.UserRating:F1}" : "No Rating";
            RuntimeText.Text = _details.RuntimeMinutes != null ? $"{_details.RuntimeMinutes} min" : string.Empty;

            TypeText.Text = _details.Type switch
            {
                "movie" => "MOVIE",
                "tv_series" => "TV SHOW",
                "tv_miniseries" => "MINISERIES",
                _ => _details.Type?.ToUpperInvariant() ?? "UNKNOWN"
            };

            App.MainWindowInstance?.SelectStreamingTabForTitleType(_details.Type);

            if (_details.GenreNames != null)
            {
                GenresText.Text = string.Join(" • ", _details.GenreNames);
            }

            // Bind multi-platform scores
            bool anyScore = false;
            if (scores != null)
            {
                if (scores.RottenTomatoesScore != null && scores.RottenTomatoesScore > 0)
                {
                    RtScoreText.Text = $"{scores.RottenTomatoesScore}%";
                    RtScoreBorder.Visibility = Visibility.Visible;
                    anyScore = true;
                }
                else { RtScoreBorder.Visibility = Visibility.Collapsed; }

                if (scores.ImdbScore != null && scores.ImdbScore > 0)
                {
                    ImdbScoreText.Text = scores.ImdbVotes != null ? $"{scores.ImdbScore:F1} ({scores.ImdbVotes:N0})" : $"{scores.ImdbScore:F1}";
                    ImdbScoreBorder.Visibility = Visibility.Visible;
                    anyScore = true;
                }
                else { ImdbScoreBorder.Visibility = Visibility.Collapsed; }

                if (scores.CriticScore != null && scores.CriticScore > 0)
                {
                    CriticScoreText.Text = $"{scores.CriticScore}";
                    CriticScoreBorder.Visibility = Visibility.Visible;
                    anyScore = true;
                }
                else { CriticScoreBorder.Visibility = Visibility.Collapsed; }

                if (scores.AudienceScore != null && scores.AudienceScore > 0)
                {
                    AudienceScoreText.Text = $"{scores.AudienceScore}%";
                    AudienceScoreBorder.Visibility = Visibility.Visible;
                    anyScore = true;
                }
                else { AudienceScoreBorder.Visibility = Visibility.Collapsed; }
            }
            ScoresPanel.Visibility = anyScore ? Visibility.Visible : Visibility.Collapsed;

            OverviewText.Text = _details.PlotOverview ?? "No synopsis available.";

            // Poster
            if (!string.IsNullOrEmpty(_details.DisplayPoster))
            {
                var bmp = new BitmapImage();
                bmp.DecodePixelWidth = 360;
                bmp.UriSource = new Uri(_details.DisplayPoster);
                PosterImage.Source = bmp;
            }

            // Library status
            UpdateLibraryButtonStatus();

            // Trailer button
            if (!string.IsNullOrEmpty(_details.Trailer))
            {
                TrailerButton.Visibility = Visibility.Visible;
            }

            // Build Where to Watch section
            BuildProvidersSection(sources);

            // Initialize Region Detail Dropdown
            RegionDetailComboBox.ItemsSource = RegionHelper.GetAllRegions();
            RegionDetailComboBox.SelectedValue = _selectedRegion;

            // Bind cast & crew separately
            var castMembers = cast
                .Where(c => string.Equals(c.Type, "Cast", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Type, "Actor", StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Order ?? 999)
                .Take(50)
                .ToList();
            CastGridView.ItemsSource = castMembers;

            var crewMembers = cast
                .Where(c => !(string.Equals(c.Type, "Cast", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Type, "Actor", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => GetCrewPriority(c))
                .ThenBy(c => c.Order ?? 999)
                .Take(50)
                .ToList();
            CrewGridView.ItemsSource = crewMembers;
            if (crewMembers.Count == 0)
            {
                DetailsPivot.Items.Remove(CrewPivotItem);
            }
            else if (!DetailsPivot.Items.Contains(CrewPivotItem))
            {
                int castIndex = DetailsPivot.Items.IndexOf(CastPivotItem);
                DetailsPivot.Items.Insert(castIndex >= 0 ? castIndex + 1 : 1, CrewPivotItem);
            }

            // Bind similar titles (filter so movies only show movies and tv shows only show tv shows)
            if (similarTitles != null && similarTitles.Count > 0 && _details != null)
            {
                bool isTv = _details.Type == "tv" || _details.Type == "tv_series" || _details.Type == "tv_miniseries";
                similarTitles = similarTitles.Where(t =>
                {
                    if (string.IsNullOrEmpty(t.Type)) return true;
                    bool itemTv = t.Type == "tv" || t.Type == "tv_series" || t.Type == "tv_miniseries";
                    return isTv ? itemTv : !itemTv;
                }).ToList();
            }

            if (similarTitles != null && similarTitles.Count > 0)
            {
                SimilarTitlesGridView.ItemsSource = similarTitles;
                if (!DetailsPivot.Items.Contains(SimilarPivotItem))
                {
                    DetailsPivot.Items.Add(SimilarPivotItem);
                }
            }
            else
            {
                DetailsPivot.Items.Remove(SimilarPivotItem);
            }

            // Bind release dates and windows
            if (releases != null && releases.Count > 0)
            {
                ReleasesListView.ItemsSource = releases.OrderByDescending(r => r.ReleaseDate).ToList();
                if (!DetailsPivot.Items.Contains(ReleasesPivotItem))
                {
                    DetailsPivot.Items.Add(ReleasesPivotItem);
                }
            }
            else
            {
                DetailsPivot.Items.Remove(ReleasesPivotItem);
            }

            // TV show hierarchy TreeView
            if (_details?.Type == "tv_series" || _details?.Type == "tv_miniseries")
            {
                if (!DetailsPivot.Items.Contains(EpisodesPivotItem))
                {
                    DetailsPivot.Items.Add(EpisodesPivotItem);
                }
                PopulateEpisodesTree(seasons, episodes);
            }
            else
            {
                if (DetailsPivot.Items.Contains(EpisodesPivotItem))
                {
                    DetailsPivot.Items.Remove(EpisodesPivotItem);
                }
            }
        }

        private void UpdateLibraryButtonStatus()
        {
            var savedItems = AppServices.StreamingLibrary.SavedItems;
            var savedItem = savedItems.Find(i => i.Id == _watchmodeId.ToString());
            _isSaved = savedItem != null;

            if (_isSaved)
            {
                LibraryIcon.Glyph = "\uE738"; // Checkmark
                LibraryButtonText.Text = $"Saved ({savedItem!.Watchlist})";
            }
            else
            {
                LibraryIcon.Glyph = "\uE710"; // Add
                LibraryButtonText.Text = "Add to Watchlist";
            }
        }

        private void OnLibraryFlyoutOpening(object sender, object e)
        {
            var savedItems = AppServices.StreamingLibrary.SavedItems;
            var savedItem = savedItems.Find(i => i.Id == _watchmodeId.ToString());

            foreach (var item in LibraryMenuFlyout.Items)
            {
                if (item is MenuFlyoutItem menuItem)
                {
                    if (menuItem.Name == "RemoveLibraryItem")
                    {
                        menuItem.Visibility = savedItem != null ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else if (menuItem.Tag is string category)
                    {
                        if (savedItem != null && savedItem.Watchlist == category)
                        {
                            menuItem.Icon = new SymbolIcon(Symbol.Accept);
                        }
                        else
                        {
                            menuItem.Icon = null;
                        }
                    }
                }
            }
        }

        private void OnSaveWatchlistClick(object sender, RoutedEventArgs e)
        {
            if (_details == null || sender is not MenuFlyoutItem menuItem || menuItem.Tag is not string category) return;

            var savedItems = AppServices.StreamingLibrary.SavedItems;
            var existing = savedItems.Find(i => i.Id == _watchmodeId.ToString());

            if (existing != null)
            {
                existing.Watchlist = category;
                AppServices.StreamingLibrary.Save();
            }
            else
            {
                AppServices.StreamingLibrary.AddItem(new SavedStreamingItem
                {
                    Id = _watchmodeId.ToString(),
                    Title = _details.Title ?? "Unknown Title",
                    Subtitle = _details.Year?.ToString() ?? string.Empty,
                    PosterUrl = _details.DisplayPoster ?? string.Empty,
                    Type = _details.Type == "movie" ? StreamingItemType.Movie : StreamingItemType.TvShow,
                    Watchlist = category
                });
            }

            UpdateLibraryButtonStatus();
        }

        private void OnRemoveFromLibraryClick(object sender, RoutedEventArgs e)
        {
            if (_details == null) return;
            AppServices.StreamingLibrary.RemoveItem(_watchmodeId.ToString(), _details.Type == "movie" ? StreamingItemType.Movie : StreamingItemType.TvShow);
            AppServices.StreamingLibrary.Save();
            UpdateLibraryButtonStatus();
        }

        private async void OnTrailerButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_details != null && !string.IsNullOrEmpty(_details.Trailer))
                {
                    try
                    {
                        if (_details.Trailer.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                            _details.Trailer.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                        {
                            App.MainWindowInstance?.NavigateToYouTube(_details.Trailer);
                        }
                        else
                        {
                            await Windows.System.Launcher.LaunchUriAsync(new Uri(_details.Trailer));
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }


        private Border CreateBadge(string text, string tooltip = "")
        {
            var border = new Border
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 2),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = textBlock;
            if (!string.IsNullOrEmpty(tooltip))
            {
                ToolTipService.SetToolTip(border, tooltip);
            }
            return border;
        }

        private void PopulateQualityBadges(List<WatchmodeSource> sources)
        {
            QualityBadgesPanel.Children.Clear();
            QualityBadgesPanel.Visibility = Visibility.Collapsed;

            if (sources == null || sources.Count == 0) return;

            var formats = sources
                .Select(s => s.Format?.ToUpperInvariant() ?? "")
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .ToList();

            string highestFormat = "";
            if (formats.Contains("4K")) highestFormat = "4K";
            else if (formats.Contains("HD")) highestFormat = "HD";
            else if (formats.Contains("SD")) highestFormat = "SD";

            if (string.IsNullOrEmpty(highestFormat))
            {
                // Default to HD if year is recent
                highestFormat = (_details?.Year >= 2000) ? "HD" : "SD";
            }

            // 1. Resolution Badge
            if (highestFormat == "4K")
            {
                QualityBadgesPanel.Children.Add(CreateBadge("4K UHD", "4K Ultra High Definition"));
            }
            else if (highestFormat == "HD")
            {
                QualityBadgesPanel.Children.Add(CreateBadge("HD", "High Definition (1080p/720p)"));
            }
            else
            {
                QualityBadgesPanel.Children.Add(CreateBadge("SD", "Standard Definition"));
            }

            // 2. HDR / Dolby Vision Badge
            if (highestFormat == "4K")
            {
                if (_details?.Year >= 2017)
                {
                    QualityBadgesPanel.Children.Add(CreateBadge("Dolby Vision", "Dolby Vision High Dynamic Range"));
                }
                else
                {
                    QualityBadgesPanel.Children.Add(CreateBadge("HDR", "High Dynamic Range"));
                }
            }

            // 3. Audio Badge
            if (_details?.Year >= 1995)
            {
                if (highestFormat == "4K" && _details?.Year >= 2015)
                {
                    QualityBadgesPanel.Children.Add(CreateBadge("Dolby Atmos", "Dolby Atmos Spatial Audio"));
                }
                else if (_details?.Year >= 2005)
                {
                    QualityBadgesPanel.Children.Add(CreateBadge("Dolby Audio 5.1", "Dolby Digital Surround Sound"));
                }
                else
                {
                    QualityBadgesPanel.Children.Add(CreateBadge("Surround Sound", "Multi-channel Surround Sound"));
                }
            }
            else
            {
                QualityBadgesPanel.Children.Add(CreateBadge("Stereo", "Two-channel Stereo Sound"));
            }

            if (QualityBadgesPanel.Children.Count > 0)
            {
                QualityBadgesPanel.Visibility = Visibility.Visible;
            }
        }

        private void BuildProvidersSection(List<WatchmodeSource> sources)
        {
            ProvidersContainer.Children.Clear();

            // Check if title is available locally in the user's media library or disk
            string? targetTitle = _details?.Title;
            if (string.IsNullOrWhiteSpace(targetTitle))
            {
                targetTitle = TitleText.Text;
            }
            CheckAndBuildLocalMediaSection(targetTitle);

            // Populate Video/Audio Quality Badges
            PopulateQualityBadges(sources);

            if (sources == null || sources.Count == 0)
            {
                ProvidersContainer.Children.Add(new TextBlock 
                { 
                    Text = "Streaming Not Available", 
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    FontSize = 15,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
                return;
            }

            // Strictly filter sources by the selected region
            string targetRegion = (!string.IsNullOrEmpty(_selectedRegion) ? _selectedRegion : "US").ToUpperInvariant();
            var regionalSources = sources
                .Where(s => string.Equals(s.Region, targetRegion, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Synthesize Native Original Platform source if omitted by API (e.g. Apple TV Original like Presumed Innocent)
            if (IsAppleOriginal(_details))
            {
                bool hasDirectAppleTvSub = regionalSources.Any(s =>
                    s.Name != null &&
                    (string.Equals(s.Name, "Apple TV+", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(s.Name, "Apple TV", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(s.Type, "sub", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Amazon", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Channel", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Roku", StringComparison.OrdinalIgnoreCase));

                if (!hasDirectAppleTvSub)
                {
                    regionalSources.Add(new WatchmodeSource
                    {
                        SourceId = 350,
                        Name = "Apple TV+",
                        Type = "sub",
                        Region = targetRegion,
                        WebUrl = "https://tv.apple.com",
                        Format = "4K"
                    });
                }
            }


            if (regionalSources.Count == 0)
            {
                ProvidersContainer.Children.Add(new TextBlock 
                { 
                    Text = "Streaming Not Available", 
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    FontSize = 15,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
                return;
            }

            // Deduplicate: group by Name and broad Category to eliminate Rent vs Buy duplicates on the same provider.
            // Prioritize sources that actually have a deep link over those that might have a higher format but no deep link.
            var deduped = regionalSources
                .GroupBy(s => 
                {
                    string t = s.Type?.ToLowerInvariant() ?? "";
                    int cat = (t == "free" || t == "free_with_ads" || t == "avod") ? 1 :
                              (t == "sub" || t == "sub_addon" || t == "tve" || t == "subscription") ? 2 : 3;
                    string normalizedName = s.Name?.ToLowerInvariant().Replace(" ", "").Replace("+", "") ?? "";
                    return (normalizedName, cat);
                })
                .Select(g => g.OrderByDescending(s => !string.IsNullOrWhiteSpace(s.WebUrl) && s.WebUrl.Length > 28)
                              .ThenByDescending(s => GetFormatPriority(s.Format))
                              .First())
                .ToList();

            // Group sources by access type and sort according to provider priority
            var subSources = deduped.Where(s => s.Type == "sub" || s.Type == "sub_addon" || s.Type == "tve" || s.Type == "subscription")
                                    .OrderBy(s => GetProviderPriority(s, _details))
                                    .ThenBy(s => s.Name)
                                    .ToList();
            var freeSources = deduped.Where(s => s.Type == "free" || s.Type == "free_with_ads" || s.Type == "avod")
                                     .OrderBy(s => GetProviderPriority(s, _details))
                                     .ThenBy(s => s.Name)
                                     .ToList();
            var purchaseSources = deduped.Where(s => s.Type == "purchase" || s.Type == "rent" || s.Type == "buy" || s.Type == "tvod")
                                         .OrderBy(s => GetProviderPriority(s, _details))
                                         .ThenBy(s => s.Name)
                                         .ToList();

            // Catch-all: if Watchmode returns a source with an unexpected access type, include it in Subscription Streaming
            var accounted = new HashSet<WatchmodeSource>(subSources.Concat(freeSources).Concat(purchaseSources));
            var remaining = deduped.Where(s => !accounted.Contains(s))
                                   .OrderBy(s => GetProviderPriority(s, _details))
                                   .ThenBy(s => s.Name)
                                   .ToList();
            if (remaining.Count > 0)
            {
                subSources.AddRange(remaining);
            }

            if (subSources.Count > 0)
            {
                ProvidersContainer.Children.Add(new TextBlock { Text = "Subscription Streaming", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 0) });
                ProvidersContainer.Children.Add(BuildProviderWrapPanel(subSources, "Subscription"));
            }

            if (freeSources.Count > 0)
            {
                ProvidersContainer.Children.Add(new TextBlock { Text = "Free Streaming", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) });
                ProvidersContainer.Children.Add(BuildProviderWrapPanel(freeSources, "Free"));
            }

            if (purchaseSources.Count > 0)
            {
                ProvidersContainer.Children.Add(new TextBlock { Text = "Buy or Rent", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) });
                ProvidersContainer.Children.Add(BuildProviderWrapPanel(purchaseSources, "Rent/Buy"));
            }
        }

        private async void CheckAndBuildLocalMediaSection(string? title)
        {
            try
            {
            if (string.IsNullOrWhiteSpace(title)) return;
            string cleanTarget = CleanTitleForComparison(title);
            if (string.IsNullOrEmpty(cleanTarget)) return;

            var allLocalItems = AppServices.VideoViewModel.RawVideos
                .Concat(SampleMediaLibrary.AllTracks)
                .Concat(SampleMediaLibrary.VideoTracks)
                .Concat(SampleMediaLibrary.AudioTracks)
                .Distinct()
                .ToList();

            MediaItem? match = null;

            await Task.Run(() =>
            {
                foreach (var item in allLocalItems)
            {
                if (item == null) continue;
                string sourcePath = item.SourcePath ?? "";

                if (IsTitleMatch(title, item.Title) ||
                    IsTitleMatch(title, System.IO.Path.GetFileNameWithoutExtension(sourcePath)) ||
                    IsTitleMatch(title, GetParentDirectoryName(sourcePath)) ||
                    IsTitleMatch(title, GetGrandparentDirectoryName(sourcePath)))
                {
                    match = item;
                    break;
                }
            }

            // On-the-fly recursive disk scan fallback if title is on disk but not yet indexed in library memory
            if (match == null)
            {
                try
                {
                    var foldersToCheck = new List<string>();
                    try { foldersToCheck.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)); } catch { }
                    try { foldersToCheck.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); } catch { }
                    try { foldersToCheck.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos")); } catch { }
                    try
                    {
                        if (AppServices.Settings.Current.LibraryFolders != null)
                        {
                            foreach (var f in AppServices.Settings.Current.LibraryFolders)
                            {
                                if (!string.IsNullOrEmpty(f) && !foldersToCheck.Contains(f, StringComparer.OrdinalIgnoreCase))
                                {
                                    foldersToCheck.Add(f);
                                }
                            }
                        }
                    }
                    catch { }

                    foreach (var folder in foldersToCheck)
                    {
                        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder)) continue;
                        var files = SafeEnumerateVideoFiles(folder, maxDepth: 3);
                        foreach (var filePath in files)
                        {
                            string fileNameNoExt = System.IO.Path.GetFileNameWithoutExtension(filePath);
                            if (IsTitleMatch(title, fileNameNoExt) ||
                                IsTitleMatch(title, GetParentDirectoryName(filePath)) ||
                                IsTitleMatch(title, GetGrandparentDirectoryName(filePath)))
                            {
                                var fileInfo = new System.IO.FileInfo(filePath);
                                string ext = System.IO.Path.GetExtension(filePath);
                                match = new MediaItem
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Title = fileNameNoExt,
                                    SourcePath = filePath,
                                    Kind = MediaKind.Video,
                                    FileSize = fileInfo.Length,
                                    DateCreated = fileInfo.CreationTime,
                                    LastModifiedUtc = fileInfo.LastWriteTimeUtc,
                                    DateAdded = DateTime.Now,
                                    IsFolder = false,
                                    FileExtension = ext
                                };
                                _ = SampleMediaLibrary.AddTrackAsync(match);
                                break;
                            }
                        }
                        if (match != null) break;
                    }
                }
                catch { }
            }
            });

            if (match != null)
            {
                var card = new Microsoft.UI.Xaml.Controls.Button
                {
                    Style = (Style)Application.Current.Resources["DefaultButtonStyle"],
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    Padding = new Thickness(16, 12, 16, 12),
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };

                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal };
                rowPanel.Children.Add(new FontIcon
                {
                    Glyph = "\uE768",
                    FontSize = 24,
                    Margin = new Thickness(0, 0, 14, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                });

                var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                textCol.Children.Add(new TextBlock
                {
                    Text = "Play Local Copy",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                });

                string subText = !string.IsNullOrEmpty(match.Resolution) ? $"{match.Resolution} · In Library" : "In Library";
                textCol.Children.Add(new TextBlock
                {
                    Text = subText,
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });

                rowPanel.Children.Add(textCol);
                card.Content = rowPanel;

                var capturedItem = match;
                card.Click += (_, _) =>
                {
                    AppServices.PlaybackViewModel.PlayTrack(capturedItem);
                };

                ProvidersContainer.Children.Add(card);
                ProvidersContainer.Children.Add(new Microsoft.UI.Xaml.Controls.Border { Height = 16 });
            }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private static IEnumerable<string> SafeEnumerateVideoFiles(string rootPath, int maxDepth = 3)
        {
            var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" };
            var list = new List<string>();
            SafeEnumerateRecursive(rootPath, 0, maxDepth, validExtensions, list);
            return list;
        }

        private static void SafeEnumerateRecursive(string currentDir, int currentDepth, int maxDepth, HashSet<string> validExtensions, List<string> results)
        {
            if (string.IsNullOrEmpty(currentDir) || currentDepth > maxDepth) return;
            try
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(currentDir))
                {
                    string ext = System.IO.Path.GetExtension(file);
                    if (!string.IsNullOrEmpty(ext) && validExtensions.Contains(ext))
                    {
                        results.Add(file);
                    }
                }
                foreach (var subDir in System.IO.Directory.EnumerateDirectories(currentDir))
                {
                    SafeEnumerateRecursive(subDir, currentDepth + 1, maxDepth, validExtensions, results);
                }
            }
            catch { }
        }

        private static string GetParentDirectoryName(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return "";
                string? dir = System.IO.Path.GetDirectoryName(filePath);
                return !string.IsNullOrEmpty(dir) ? (System.IO.Path.GetFileName(dir) ?? "") : "";
            }
            catch { return ""; }
        }

        private static string GetGrandparentDirectoryName(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return "";
                string? dir = System.IO.Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir)) return "";
                string? grandDir = System.IO.Path.GetDirectoryName(dir);
                return !string.IsNullOrEmpty(grandDir) ? (System.IO.Path.GetFileName(grandDir) ?? "") : "";
            }
            catch { return ""; }
        }

        private static bool IsTitleMatch(string? targetTitle, string? candidateTitle)
        {
            string cleanTarget = CleanTitleForComparison(targetTitle);
            string cleanCandidate = CleanTitleForComparison(candidateTitle);

            if (string.IsNullOrEmpty(cleanTarget) || string.IsNullOrEmpty(cleanCandidate))
                return false;

            if (string.Equals(cleanTarget, cleanCandidate, StringComparison.OrdinalIgnoreCase))
                return true;

            if (cleanTarget.Length >= 3 && cleanCandidate.StartsWith(cleanTarget + " ", StringComparison.OrdinalIgnoreCase))
                return true;
            if (cleanCandidate.Length >= 3 && cleanTarget.StartsWith(cleanCandidate + " ", StringComparison.OrdinalIgnoreCase))
                return true;

            if (cleanTarget.Length >= 4 && (cleanCandidate.StartsWith(cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                                            cleanTarget.StartsWith(cleanCandidate, StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private static string CleanTitleForComparison(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string cleaned = text.Replace("'", "").Replace("’", "").Replace("&", " and ");
            cleaned = cleaned.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Replace(':', ' ').Replace(';', ' ')
                             .Replace('(', ' ').Replace(')', ' ').Replace('[', ' ').Replace(']', ' ')
                             .Replace('{', ' ').Replace('}', ' ').Replace(',', ' ');
            var chars = cleaned.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
            cleaned = new string(chars).Trim().ToLowerInvariant();

            var tokensToStrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "1080p", "720p", "2160p", "480p", "4k", "8k", "hdr", "hdr10", "dolby", "vision", "atmos",
                "web", "webdl", "webrip", "bluray", "brrip", "xvid", "divx", "x264", "h264", "x265", "h265", "hevc",
                "aac", "dts", "flac", "mp3", "ac3", "eac3", "ddp5", "remux", "dual", "audio", "sub", "subs", "multi",
                "season", "episode", "pilot"
            };

            var words = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                               .Where(w => !tokensToStrip.Contains(w))
                               .Where(w => !(w.Length == 4 && int.TryParse(w, out int yr) && yr >= 1900 && yr <= 2100))
                               .Where(w => !(w.Length >= 4 && (w.StartsWith("s0") || w.StartsWith("s1") || w.StartsWith("s2") || w.StartsWith("s3")) && w.Contains("e0")))
                               .ToArray();

            return string.Join(" ", words);
        }

        private static int GetFormatPriority(string? format)
        {
            return (format?.ToUpperInvariant()) switch
            {
                "4K" => 3,
                "HD" => 2,
                "SD" => 1,
                _ => 0
            };
        }

        private static bool IsAppleOriginal(WatchmodeDetails? details)
        {
            if (details == null) return false;
            if (details.NetworkNames?.Any(n => n != null && n.Contains("apple", StringComparison.OrdinalIgnoreCase)) == true)
                return true;
            if (details.StudioNames?.Any(s => s != null && s.Contains("apple", StringComparison.OrdinalIgnoreCase)) == true)
                return true;

            string clean = details.Title?.Trim() ?? "";
            var knownAppleOriginals = new[]
            {
                "Presumed Innocent", "Ted Lasso", "Severance", "The Morning Show", "For All Mankind",
                "Slow Horses", "Shrinking", "Silo", "Foundation", "Bad Monkey", "Pachinko",
                "Hijack", "Black Bird", "Dark Matter", "Sugar", "Masters of the Air",
                "Monarch: Legacy of Monsters", "See", "Servant", "Mythic Quest", "Dickinson",
                "Physical", "Invasion", "Lady in the Lake", "Defending Jacob", "Platonic",
                "Palm Royale", "The Afterparty", "Schmigadoon!", "Trying", "Loot",
                "Wolfs", "The Instigators", "Argylle", "Napoleon", "Killers of the Flower Moon",
                "CODA", "Greyhound", "Finch", "Spirited", "Tetris", "Ghosted", "The Family Plan",
                "Fly Me to the Moon", "Sharper", "The Banker", "Cherry"
            };

            return knownAppleOriginals.Any(t => string.Equals(clean, t, StringComparison.OrdinalIgnoreCase) ||
                                                clean.StartsWith(t, StringComparison.OrdinalIgnoreCase));
        }

        private static int GetProviderPriority(WatchmodeSource source, WatchmodeDetails? details)

        {
            if (source == null || string.IsNullOrEmpty(source.Name)) return 100;
            var lower = source.Name.ToLowerInvariant();

            bool isAddOnOrChannel = lower.Contains("channel") || lower.Contains("add-on") || lower.Contains("addon") || 
                                    lower.Contains("on prime") || lower.Contains("on roku") || lower.Contains("on apple") ||
                                    string.Equals(source.Type, "addon", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(source.Type, "sub_addon", StringComparison.OrdinalIgnoreCase);

            if (!isAddOnOrChannel && details != null)
            {
                bool isAppleOriginal = IsAppleOriginal(details);

                bool isNetflixOriginal = (details.NetworkNames?.Any(n => n.Contains("netflix", StringComparison.OrdinalIgnoreCase)) ?? false) ||
                                         (details.StudioNames?.Any(s => s.Contains("netflix", StringComparison.OrdinalIgnoreCase)) ?? false);
                bool isPrimeOriginal = (details.NetworkNames?.Any(n => n.Contains("amazon", StringComparison.OrdinalIgnoreCase) || n.Contains("prime", StringComparison.OrdinalIgnoreCase)) ?? false) ||
                                       (details.StudioNames?.Any(s => s.Contains("amazon", StringComparison.OrdinalIgnoreCase) || s.Contains("prime", StringComparison.OrdinalIgnoreCase)) ?? false);
                bool isDisneyOriginal = (details.NetworkNames?.Any(n => n.Contains("disney", StringComparison.OrdinalIgnoreCase)) ?? false) ||
                                        (details.StudioNames?.Any(s => s.Contains("disney", StringComparison.OrdinalIgnoreCase)) ?? false);
                bool isMaxOriginal = (details.NetworkNames?.Any(n => n.Contains("hbo", StringComparison.OrdinalIgnoreCase) || n.Contains("max", StringComparison.OrdinalIgnoreCase)) ?? false) ||
                                     (details.StudioNames?.Any(s => s.Contains("hbo", StringComparison.OrdinalIgnoreCase) || s.Contains("max", StringComparison.OrdinalIgnoreCase)) ?? false);

                if (isAppleOriginal && lower.Contains("apple")) return 0;
                if (isNetflixOriginal && lower.Contains("netflix")) return 0;
                if (isPrimeOriginal && (lower.Contains("prime") || lower.Contains("amazon"))) return 0;
                if (isDisneyOriginal && lower.Contains("disney")) return 0;
                if (isMaxOriginal && (lower.Contains("max") || lower.Contains("hbo"))) return 0;
            }

            int tierOffset = isAddOnOrChannel ? 50 : 0;
            if (string.Equals(source.Type, "rent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Type, "buy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Type, "purchase", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Type, "tvod", StringComparison.OrdinalIgnoreCase))
            {
                tierOffset = 80;
            }

            int baseRank = 40;
            if (lower.Contains("apple")) baseRank = 1;
            else if (lower.Contains("netflix")) baseRank = 2;
            else if (lower.Contains("prime") || lower.Contains("amazon")) baseRank = 3;
            else if (lower.Contains("disney")) baseRank = 4;
            else if (lower.Contains("max") || lower.Contains("hbo")) baseRank = 5;
            else if (lower.Contains("hulu")) baseRank = 6;
            else if (lower.Contains("paramount")) baseRank = 7;
            else if (lower.Contains("peacock")) baseRank = 8;
            else if (lower.Contains("youtube") || lower.Contains("google")) baseRank = 9;
            else if (lower.Contains("vudu") || lower.Contains("fandango")) baseRank = 10;
            else if (lower.Contains("tubi")) baseRank = 11;
            else if (lower.Contains("pluto")) baseRank = 12;
            else if (lower.Contains("roku")) baseRank = 13;
            else if (lower.Contains("plex")) baseRank = 14;
            else if (lower.Contains("crunchyroll")) baseRank = 15;

            return tierOffset + baseRank;
        }

        private FrameworkElement BuildProviderWrapPanel(List<WatchmodeSource> sourcesList, string labelType)
        {
            // In WinUI 3, VariableSizedWrapGrid serves as a responsive wrap layout panel
            var panel = new VariableSizedWrapGrid 
            { 
                Orientation = Orientation.Horizontal,
                ItemWidth = 155,
                ItemHeight = 125
            };

            foreach (var source in sourcesList)
            {
                var resolvedUrl = ResolveProviderUrl(source);
                if (string.IsNullOrEmpty(resolvedUrl)) continue;

                var btn = new Button
                {
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(12),
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                var contentPanel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
                
                // Icon
                var iconUrl = GetProviderIconUrl(source);
                var bmp = new BitmapImage();
                bmp.DecodePixelWidth = 48;
                bmp.UriSource = new Uri(iconUrl);
                var logo = new Image
                {
                    Source = bmp,
                    Width = 40,
                    Height = 40,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                };
                contentPanel.Children.Add(logo);

                // Service Name
                var text = new TextBlock 
                { 
                    Text = source.Name, 
                    FontSize = 11, 
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    TextWrapping = TextWrapping.Wrap, 
                    TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center 
                };
                contentPanel.Children.Add(text);

                // Type & Format Label (e.g. "Rent HD" / "Buy 4K")
                string typeLabel = source.Type switch
                {
                    "rent" => "Rent",
                    "purchase" => "Buy",
                    "sub" => "Stream",
                    "free" => "Free",
                    _ => source.Type?.ToUpperInvariant() ?? ""
                };
                string formatText = !string.IsNullOrEmpty(source.Format) ? $" {source.Format}" : "";
                var typeFormatText = new TextBlock
                {
                    Text = $"{typeLabel}{formatText}",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Opacity = 0.6
                };
                contentPanel.Children.Add(typeFormatText);

                // Price tag if applicable
                string currencySymbol = GetCurrencySymbol(_selectedRegion);
                string priceLabel = source.Price != null ? $"{currencySymbol}{source.Price:F2}" : labelType;
                var priceText = new TextBlock 
                { 
                    Text = priceLabel, 
                    FontSize = 10, 
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    Opacity = 0.9 
                };
                contentPanel.Children.Add(priceText);

                btn.Content = contentPanel;

                var targetUrl = ResolveProviderUrl(source);
                if (!string.IsNullOrEmpty(targetUrl) && !targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    targetUrl = "https://" + targetUrl;
                }

                btn.Tag = targetUrl;
                btn.Click += async (s, args) =>
                {
                    if (s is Button clickBtn && clickBtn.Tag is string url && !string.IsNullOrEmpty(url))
                    {
                        try
                        {
                            string cleanUrl = LumiereMediaPlayer.Helpers.StreamingRouter.CleanFallbackUrl(url);

                            // Intercept Apple TV URLs to guarantee canonical Show ID extraction instead of Episode IDs
                            if (cleanUrl.Contains("tv.apple.com", StringComparison.OrdinalIgnoreCase))
                            {
                                string term = "";
                                var qMatch = System.Text.RegularExpressions.Regex.Match(cleanUrl, @"[?&]term=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (qMatch.Success) term = qMatch.Groups[1].Value;
                                else term = _details?.Title ?? "";

                                if (!string.IsNullOrEmpty(term))
                                {
                                    try
                                    {
                                        string appleTvSearchUrl = $"https://tv.apple.com/us/search?term={Uri.EscapeDataString(term)}";
                                        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
                                        var html = await client.GetStringAsync(appleTvSearchUrl);
                                        
                                        var match = System.Text.RegularExpressions.Regex.Match(html, @"href=""(https://tv\.apple\.com/us/(?:show|movie)/[^""]+umc\.cmc\.[a-z0-9]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        if (match.Success)
                                        {
                                            cleanUrl = match.Groups[1].Value;
                                            AntiGravityLogger.Log($"Apple TV Web Scraper upgraded URL to: {cleanUrl}");
                                        }
                                        else
                                        {
                                            // Fallback to iTunes API if it's not an Apple TV original but a rentable movie
                                            string mediaType = (CurrentTitleType?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true) ? "movie" : "tvShow";
                                            string itunesUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}&media={mediaType}&limit=1";
                                            var response = await client.GetStringAsync(itunesUrl);
                                            using var doc = System.Text.Json.JsonDocument.Parse(response);
                                            var results = doc.RootElement.GetProperty("results");
                                            if (results.GetArrayLength() > 0)
                                            {
                                                var trackViewUrl = results[0].GetProperty("trackViewUrl").GetString();
                                                if (!string.IsNullOrEmpty(trackViewUrl))
                                                {
                                                    cleanUrl = trackViewUrl;
                                                    AntiGravityLogger.Log($"iTunes API upgraded URL to: {cleanUrl}");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        AntiGravityLogger.Log($"Apple TV / iTunes URL upgrade failed: {ex.Message}");
                                    }
                                }
                            }

                            var nativeUri = LumiereMediaPlayer.Helpers.StreamingRouter.GetNativeUri(cleanUrl);
                            AntiGravityLogger.Log($"Launching provider URI (Native): {nativeUri}, Fallback: {cleanUrl}");
                            
                            await LumiereMediaPlayer.Helpers.StreamingRouter.LaunchStreamUriAsync(nativeUri, cleanUrl);
                        }
                        catch (Exception ex)
                        {
                            AntiGravityLogger.Log($"Failed to launch URI: {ex.Message}");
                            try
                            {
                                string cleanUrl = LumiereMediaPlayer.Helpers.StreamingRouter.CleanFallbackUrl(url);
                                await Windows.System.Launcher.LaunchUriAsync(new Uri(cleanUrl));
                            }
                            catch (Exception fallbackEx)
                            {
                                AntiGravityLogger.Log($"Failed fallback launch URI: {fallbackEx.Message}");
                            }
                        }
                    }
                };

                panel.Children.Add(btn);
            }

            return panel;
        }

        private string ResolveProviderUrl(WatchmodeSource source)
        {
            string webUrl = source.WebUrl ?? "";

            // If WebUrl is missing or just a root domain, check if mobile URLs have a valid deep link.
            if (string.IsNullOrWhiteSpace(webUrl) || webUrl.Length < 30)
            {
                if (!string.IsNullOrWhiteSpace(source.AndroidUrl) && source.AndroidUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && source.AndroidUrl.Length > 28)
                    webUrl = source.AndroidUrl;
                else if (!string.IsNullOrWhiteSpace(source.IosUrl) && source.IosUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && source.IosUrl.Length > 28)
                    webUrl = source.IosUrl;
            }

            string name = source.Name?.ToLowerInvariant() ?? "";

            if (name.Contains("crunchyroll"))
            {
                if (string.IsNullOrWhiteSpace(webUrl) ||
                    webUrl.Equals("https://www.crunchyroll.com", StringComparison.OrdinalIgnoreCase) ||
                    webUrl.Equals("http://www.crunchyroll.com", StringComparison.OrdinalIgnoreCase) ||
                    webUrl.Equals("https://crunchyroll.com", StringComparison.OrdinalIgnoreCase))
                {
                    string searchQuery = !string.IsNullOrWhiteSpace(_details?.Title) ? _details.Title : "";
                    webUrl = !string.IsNullOrWhiteSpace(searchQuery)
                        ? $"https://www.crunchyroll.com/search?q={Uri.EscapeDataString(searchQuery)}"
                        : "https://www.crunchyroll.com";
                }
            }

            if (name.Contains("apple"))
            {
                // Rewrite any tv.apple.com or itunes.apple.com URL to be region-aware based on local OS storefront region
                if ((webUrl.Contains("tv.apple.com", StringComparison.OrdinalIgnoreCase) || 
                     webUrl.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase)) &&
                    !webUrl.Contains("/search", StringComparison.OrdinalIgnoreCase))
                {
                    string targetRegion = "us";
                    try
                    {
                        string osRegion = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(osRegion))
                        {
                            targetRegion = osRegion;
                        }
                    }
                    catch { }

                    var match = System.Text.RegularExpressions.Regex.Match(webUrl, @"((?:tv|itunes)\.apple\.com)/([a-zA-Z]{2})(/|$)");
                    if (match.Success)
                    {
                        string foundRegion = match.Groups[2].Value;
                        if (!foundRegion.Equals(targetRegion, StringComparison.OrdinalIgnoreCase))
                        {
                            webUrl = System.Text.RegularExpressions.Regex.Replace(webUrl, @"((?:tv|itunes)\.apple\.com/)[a-zA-Z]{2}(/|$)", $"$1{targetRegion}$2");
                            AntiGravityLogger.Log($"Apple TV: Rewrote URL region from '{foundRegion}' to '{targetRegion}'. Result: {webUrl}");
                        }
                    }
                    else
                    {
                        webUrl = System.Text.RegularExpressions.Regex.Replace(webUrl, @"(tv\.apple\.com|itunes\.apple\.com)(/|$)", $"$1/{targetRegion}/");
                        AntiGravityLogger.Log($"Apple TV: Inserted region '{targetRegion}'. Result: {webUrl}");
                    }
                }

                // If this is a series page, try to deep link directly to the first season's episodes
                if (webUrl.Contains("/show/", StringComparison.OrdinalIgnoreCase) && !webUrl.Contains("/season/", StringComparison.OrdinalIgnoreCase))
                {
                    var sep = webUrl.EndsWith("/") ? "" : "/";
                    webUrl = $"{webUrl}{sep}season/1";
                }
            }

            // Check if it's missing or just a root domain (or a root domain with a region code like /us/)
            bool isRootDomain = false;
            if (Uri.TryCreate(webUrl, UriKind.Absolute, out var parsedUri))
            {
                var trimmedPath = parsedUri.AbsolutePath.Trim('/');
                isRootDomain = string.IsNullOrEmpty(trimmedPath) || (trimmedPath.Length == 2 && name.Contains("apple"));
            }

            if (string.IsNullOrWhiteSpace(webUrl) || (isRootDomain && string.IsNullOrEmpty(parsedUri?.Query)))
            {
                string query = !string.IsNullOrWhiteSpace(_details?.Title) ? _details.Title : "";
                string encoded = Uri.EscapeDataString(query);

                if (name.Contains("netflix"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.netflix.com/search?q={encoded}" : "https://www.netflix.com";
                else if (name.Contains("prime") || name.Contains("amazon"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.amazon.com/s?k={encoded}&i=instant-video" : "https://www.primevideo.com";
                else if (name.Contains("disney") && !name.Contains("hotstar"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.disneyplus.com/search?q={encoded}" : "https://www.disneyplus.com";
                else if (name.Contains("hotstar"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.hotstar.com/in/explore?search_query={encoded}" : "https://www.hotstar.com";
                else if (name.Contains("jiocinema") || name.Contains("jio cinema"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.jiocinema.com/search/{encoded}" : "https://www.jiocinema.com";
                else if (name.Contains("hulu"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.hulu.com/search?q={encoded}" : "https://www.hulu.com";
                else if (name.Contains("max") || name.Contains("hbo"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://play.max.com/search?q={encoded}" : "https://play.max.com";
                else if (name.Contains("paramount"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.paramountplus.com/search/?q={encoded}" : "https://www.paramountplus.com";
                else if (name.Contains("peacock"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.peacocktv.com/watch/search?q={encoded}" : "https://www.peacocktv.com";
                else if (name.Contains("youtube"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.youtube.com/results?search_query={encoded}" : "https://www.youtube.com";
                else if (name.Contains("crunchyroll"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.crunchyroll.com/search?q={encoded}" : "https://www.crunchyroll.com";
                else if (name.Contains("vudu") || name.Contains("fandango"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.vudu.com/content/movies/search?minVisible=0&returnUrl=%252F&searchString={encoded}" : "https://www.vudu.com";
                else if (name.Contains("apple") || name.Contains("itunes"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://tv.apple.com/search?term={encoded}" : "https://tv.apple.com";
                else if (!string.IsNullOrEmpty(query))
                    webUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(query + " watch on " + source.Name)}";
            }

            return webUrl;
        }

        private string GetCurrencySymbol(string regionCode)
        {
            try
            {
                if (!string.IsNullOrEmpty(regionCode))
                {
                    var region = new System.Globalization.RegionInfo(regionCode);
                    return region.CurrencySymbol;
                }
            }
            catch { }
            return "$";
        }

        private string GetProviderIconUrl(WatchmodeSource source)
        {
            string name = source.Name?.ToLowerInvariant() ?? "";
            string domain = "";

            // 1. Prioritize name matching for popular streaming platforms to guarantee exact brand logos
            if (name.Contains("netflix")) domain = "netflix.com";
            else if (name.Contains("hulu")) domain = "hulu.com";
            else if (name.Contains("prime") || name.Contains("amazon")) domain = "primevideo.com";
            else if (name.Contains("disney")) domain = "disneyplus.com";
            else if (name.Contains("hotstar")) domain = "hotstar.com";
            else if (name.Contains("max") || name.Contains("hbo")) domain = "max.com";
            else if (name.Contains("apple")) domain = "tv.apple.com";
            else if (name.Contains("peacock")) domain = "peacocktv.com";
            else if (name.Contains("paramount")) domain = "paramountplus.com";
            else if (name.Contains("youtube")) domain = "youtube.com";
            else if (name.Contains("google")) domain = "play.google.com";
            else if (name.Contains("vudu") || name.Contains("fandango")) domain = "vudu.com";
            else if (name.Contains("crunchyroll")) domain = "crunchyroll.com";
            else if (name.Contains("funimation")) domain = "funimation.com";
            else if (name.Contains("plex")) domain = "plex.tv";
            else if (name.Contains("tubi")) domain = "tubitv.com";
            else if (name.Contains("pluto")) domain = "pluto.tv";
            else if (name.Contains("roku")) domain = "roku.com";
            else if (name.Contains("jiocinema")) domain = "jiocinema.com";
            else if (name.Contains("zee5") || name.Equals("zee")) domain = "zee5.com";
            else if (name.Contains("sonyliv")) domain = "sonyliv.com";
            else if (name.Contains("sling")) domain = "sling.com";
            else if (name.Contains("fubo")) domain = "fubo.tv";
            else if (name.Contains("philo")) domain = "philo.com";
            else if (name.Contains("directv")) domain = "directv.com";
            else if (name.Contains("showtime") || name.Equals("sho")) domain = "sho.com";
            else if (name.Contains("starz")) domain = "starz.com";
            else if (name.Contains("mgm") || name.Contains("epix")) domain = "mgmplus.com";
            else if (name.Contains("criterion")) domain = "criterionchannel.com";
            else if (name.Contains("shudder")) domain = "shudder.com";
            else if (name.Contains("britbox")) domain = "britbox.com";
            else if (name.Contains("acorn")) domain = "acorn.tv";
            else if (name.Contains("kanopy")) domain = "kanopy.com";
            else if (name.Contains("hoopla")) domain = "hoopladigital.com";
            else if (name.Contains("iplayer") || name.Contains("bbc")) domain = "bbc-iplayer.co.uk";
            else if (name.Contains("itv")) domain = "itv.com";
            else if (name.Contains("my5")) domain = "channel5.com";
            else if (name.Contains("microsoft")) domain = "microsoft.com";
            else if (name.Contains("playstation")) domain = "playstation.com";

            // 2. If no popular name matches, check host domain of target WebUrl
            if (string.IsNullOrEmpty(domain) && !string.IsNullOrEmpty(source.WebUrl))
            {
                try
                {
                    string url = source.WebUrl;
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        url = "https://" + url;
                    }
                    var uri = new Uri(url);
                    domain = uri.Host.ToLowerInvariant();
                    if (domain.StartsWith("www.")) domain = domain.Substring(4);
                }
                catch { }
            }

            // 3. Fallback default
            if (string.IsNullOrEmpty(domain))
            {
                domain = "netflix.com";
            }

            return $"https://www.google.com/s2/favicons?domain={domain}&sz=128";
        }

        private void PopulateEpisodesTree(List<WatchmodeSeason> seasons, List<WatchmodeEpisode> episodes)
        {
            EpisodesTreeView.RootNodes.Clear();

            if (seasons == null || seasons.Count == 0) return;

            // Sort seasons
            var sortedSeasons = seasons.OrderBy(s => s.Number).ToList();

            foreach (var season in sortedSeasons)
            {
                var seasonContent = new TreeViewItemContent
                {
                    Title = season.Name ?? $"Season {season.Number}",
                    Subtitle = $"{season.EpisodeCount} Episodes"
                };

                var seasonNode = new TreeViewNode { Content = seasonContent };

                // Get episodes for this season
                var seasonEpisodes = episodes
                    .Where(e => e.SeasonNumber == season.Number || e.SeasonId == season.Id)
                    .OrderBy(e => e.EpisodeNumber)
                    .ToList();

                foreach (var ep in seasonEpisodes)
                {
                    var epContent = new TreeViewItemContent
                    {
                        Title = $"{ep.EpisodeNumber}. {ep.Name ?? "Episode"}",
                        Subtitle = ep.ReleaseDate ?? string.Empty,
                        Description = ep.Overview ?? "No description available.",
                        Episode = ep
                    };

                    seasonNode.Children.Add(new TreeViewNode { Content = epContent });
                }

                EpisodesTreeView.RootNodes.Add(seasonNode);
            }
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            // Slide in animation
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PageContent);
            var compositor = visual.Compositor;

            visual.Opacity = 0f;
            visual.Offset = new System.Numerics.Vector3(0, 30, 0);

            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f)));
            fadeAnim.Duration = TimeSpan.FromMilliseconds(400);

            var slideAnim = compositor.CreateVector3KeyFrameAnimation();
            slideAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0), compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f)));
            slideAnim.Duration = TimeSpan.FromMilliseconds(500);

            visual.StartAnimation("Opacity", fadeAnim);
            visual.StartAnimation("Offset", slideAnim);
        }

        private async void RegionDetailComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
            if (RegionDetailComboBox.SelectedValue is string newRegion && !string.IsNullOrEmpty(newRegion))
            {
                if (newRegion != _selectedRegion)
                {
                    _selectedRegion = newRegion;
                    
                    // Propagate selection back to global view models to sync state across views
                    if (AppServices.StreamingMoviesViewModel != null)
                        AppServices.StreamingMoviesViewModel.SelectedRegion = newRegion;
                    if (AppServices.StreamingTvShowsViewModel != null)
                        AppServices.StreamingTvShowsViewModel.SelectedRegion = newRegion;

                    ProvidersContainer.Children.Clear();
                    
                    var progressRing = new ProgressRing 
                    { 
                        IsActive = true, 
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Width = 32,
                        Height = 32,
                        Margin = new Thickness(0, 16, 0, 16)
                    };
                    ProvidersContainer.Children.Add(progressRing);

                    try
                    {
                        var sources = await _watchmodeService.GetSourcesAsync(_watchmodeId, _selectedRegion);
                        BuildProvidersSection(sources);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to reload sources: {ex.Message}");
                        ProvidersContainer.Children.Clear();
                        ProvidersContainer.Children.Add(new TextBlock 
                        { 
                            Text = "Error loading streaming sources.",
                            FontStyle = Windows.UI.Text.FontStyle.Italic,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        });
                    }
                }
            }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private void SimilarTitlesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WatchmodeTitle clickedTitle)
            {
                Frame.Navigate(typeof(StreamingDetailsPage), clickedTitle.Id);
            }
        }

        private int GetCrewPriority(WatchmodeCastCrew c)
        {
            if (string.Equals(c.Type, "Cast", StringComparison.OrdinalIgnoreCase)) return 0;
            string role = c.Role ?? "";
            if (role.Contains("Director", StringComparison.OrdinalIgnoreCase)) return 1;
            if (role.Contains("Writer", StringComparison.OrdinalIgnoreCase) || role.Contains("Screenplay", StringComparison.OrdinalIgnoreCase)) return 2;
            if (role.Contains("Producer", StringComparison.OrdinalIgnoreCase)) return 3;
            return 10;
        }

        private bool _isPersonDialogOpen = false;

        private async void CastGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
            if (_isPersonDialogOpen || !(e.ClickedItem is WatchmodeCastCrew person))
                return;

            try
            {
                _isPersonDialogOpen = true;

                var progressRing = new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center, Width = 36, Height = 36, Margin = new Thickness(0, 24, 0, 24) };
                var container = new StackPanel { Spacing = 12 };
                container.Children.Add(progressRing);

                var dialog = new ContentDialog
                {
                    Title = $"{person.FullName} — Filmography",
                    Content = container,
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot
                };

                _ = Task.Run(async () =>
                {
                    var details = await _watchmodeService.GetPersonDetailsAsync(person.PersonId, person.FullName);
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        container.Children.Clear();
                        if (details?.KnownFor != null && details.KnownFor.Count > 0)
                        {
                            var titleBlock = new TextBlock
                            {
                                Text = $"Known for ({details.KnownFor.Count} titles):",
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                Margin = new Thickness(0, 0, 0, 8)
                            };
                            container.Children.Add(titleBlock);

                            var listView = new ListView
                            {
                                SelectionMode = ListViewSelectionMode.None,
                                IsItemClickEnabled = true,
                                MaxHeight = 350
                            };

                            listView.ItemTemplate = CreateFilmographyItemTemplate();
                            listView.ItemsSource = details.KnownFor;

                            listView.ItemClick += (s, args) =>
                            {
                                if (args.ClickedItem is WatchmodeTitle clickedTitle)
                                {
                                    dialog.Hide();
                                    Frame.Navigate(typeof(StreamingDetailsPage), clickedTitle.Id);
                                }
                            };

                            container.Children.Add(listView);
                        }
                        else
                        {
                            container.Children.Add(new TextBlock
                            {
                                Text = "No filmography information available.",
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                                Margin = new Thickness(0, 12, 0, 12)
                            });
                        }
                    });
                });

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CastGridView_ItemClick Error: {ex.Message}");
            }
            finally
            {
                _isPersonDialogOpen = false;
            }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private DataTemplate CreateFilmographyItemTemplate()
        {
            var xaml = @"<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                            <Grid Padding=""8"" Margin=""0,0,0,6"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""*"" />
                                    <ColumnDefinition Width=""Auto"" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Text=""{Binding DisplayTitle}"" FontWeight=""SemiBold"" FontSize=""14"" VerticalAlignment=""Center"" />
                                <TextBlock Grid.Column=""1"" Text=""{Binding DisplayYear}"" FontSize=""12"" Foreground=""Gray"" VerticalAlignment=""Center"" Margin=""8,0,0,0"" />
                            </Grid>
                         </DataTemplate>";
            return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PosterImage != null) PosterImage.Source = null;
                if (CastGridView != null) CastGridView.ItemsSource = null;
                if (CrewGridView != null) CrewGridView.ItemsSource = null;
                if (SimilarTitlesGridView != null) SimilarTitlesGridView.ItemsSource = null;
                if (ReleasesListView != null) ReleasesListView.ItemsSource = null;
                if (ProvidersContainer != null) ProvidersContainer.Children.Clear();
                if (EpisodesTreeView != null) EpisodesTreeView.RootNodes.Clear();

                _details = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingDetailsPage] OnPageUnloaded error: {ex.Message}");
            }
        }
    }
}
