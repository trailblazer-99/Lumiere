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

        public StreamingDetailsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

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

        private async Task LoadDetailsAsync()
        {
            // parallel fetch to optimize load time
            var detailsTask = _watchmodeService.GetDetailsAsync(_watchmodeId);
            var castTask = _watchmodeService.GetCastCrewAsync(_watchmodeId);
            var seasonsTask = _watchmodeService.GetSeasonsAsync(_watchmodeId);
            var episodesTask = _watchmodeService.GetEpisodesAsync(_watchmodeId);
            var sourcesTask = _watchmodeService.GetSourcesAsync(_watchmodeId, _selectedRegion);

            await Task.WhenAll(detailsTask, castTask, seasonsTask, episodesTask, sourcesTask);

            _details = await detailsTask;
            var cast = await castTask;
            var seasons = await seasonsTask;
            var episodes = await episodesTask;
            var sources = await sourcesTask;

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

            if (_details.GenreNames != null)
            {
                GenresText.Text = string.Join(" • ", _details.GenreNames);
            }

            OverviewText.Text = _details.PlotOverview ?? "No synopsis available.";

            // Poster
            if (!string.IsNullOrEmpty(_details.DisplayPoster))
            {
                PosterImage.Source = new BitmapImage(new Uri(_details.DisplayPoster));
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

            // Bind cast
            CastGridView.ItemsSource = cast.OrderBy(c => c.Order ?? 999).Take(30).ToList();

            // TV show hierarchy TreeView
            if (_details.Type == "tv_series" || _details.Type == "tv_miniseries")
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
            UpdateLibraryButtonStatus();
        }

        private async void OnTrailerButtonClick(object sender, RoutedEventArgs e)
        {
            if (_details != null && !string.IsNullOrEmpty(_details.Trailer))
            {
                try
                {
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(_details.Trailer));
                }
                catch { }
            }
        }


        private void BuildProvidersSection(List<WatchmodeSource> sources)
        {
            ProvidersContainer.Children.Clear();

            if (sources == null || sources.Count == 0)
            {
                ProvidersContainer.Children.Add(new TextBlock 
                { 
                    Text = "No streaming options found.", 
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
                return;
            }

            // Filter sources by the selected region first to avoid displaying options from other regions
            string targetRegion = (!string.IsNullOrEmpty(_selectedRegion) ? _selectedRegion : "US").ToUpperInvariant();
            var regionalSources = sources
                .Where(s => string.Equals(s.Region, targetRegion, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (regionalSources.Count == 0)
            {
                ProvidersContainer.Children.Add(new TextBlock 
                { 
                    Text = $"No streaming options found in {targetRegion}.", 
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
                return;
            }

            // Deduplicate: group by (Name, Type) and keep the best quality (4K > HD > SD)
            var deduped = regionalSources
                .GroupBy(s => (s.Name?.ToLowerInvariant() ?? "", s.Type?.ToLowerInvariant() ?? ""))
                .Select(g => g.OrderByDescending(s => GetFormatPriority(s.Format)).First())
                .ToList();

            // Group sources by access type
            var subSources = deduped.Where(s => s.Type == "sub").ToList();
            var freeSources = deduped.Where(s => s.Type == "free").ToList();
            var purchaseSources = deduped.Where(s => s.Type == "purchase" || s.Type == "rent").ToList();

            if (subSources.Count > 0)
            {
                ProvidersContainer.Children.Add(new TextBlock { Text = "Subscription Streaming", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 });
                ProvidersContainer.Children.Add(BuildProviderWrapPanel(subSources, "Stream"));
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

        private FrameworkElement BuildProviderWrapPanel(List<WatchmodeSource> sourcesList, string labelType)
        {
            // In WinUI 3, VariableSizedWrapGrid serves as a responsive wrap layout panel
            var panel = new VariableSizedWrapGrid 
            { 
                Orientation = Orientation.Horizontal,
                ItemWidth = 140,
                ItemHeight = 110
            };

            foreach (var source in sourcesList)
            {
                if (string.IsNullOrEmpty(source.WebUrl)) continue;

                var btn = new Button
                {
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(8),
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                var contentPanel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
                
                // Icon
                var iconUrl = GetProviderIconUrl(source);
                var logo = new Image
                {
                    Source = new BitmapImage(new Uri(iconUrl)),
                    Width = 32,
                    Height = 32,
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
                            var nativeUri = LumiereMediaPlayer.Helpers.StreamingRouter.GetNativeUri(url);
                            var launcherOptions = new Windows.System.LauncherOptions
                            {
                                FallbackUri = new Uri(url)
                            };
                            AntiGravityLogger.Log($"Launching provider URI (Native): {nativeUri}, Fallback: {url}");
                            if (nativeUri != null)
                            {
                                await Windows.System.Launcher.LaunchUriAsync(nativeUri, launcherOptions);
                            }
                            else
                            {
                                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                            }
                        }
                        catch (Exception ex)
                        {
                            AntiGravityLogger.Log($"Failed to launch URI: {ex.Message}");
                            try
                            {
                                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
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
            string name = source.Name?.ToLowerInvariant() ?? "";

            // For Apple TV sources whose web_url points to Amazon (Apple TV Channel on Prime Video),
            // resolve to the actual Apple TV deep link instead
            if (name.Contains("apple"))
            {
                bool isAmazonUrl = webUrl.Contains("amazon", StringComparison.OrdinalIgnoreCase) 
                                || webUrl.Contains("primevideo", StringComparison.OrdinalIgnoreCase);
                
                if (isAmazonUrl)
                {
                    // 1. Try ios_url — the API may provide a real Apple TV deep link there
                    if (!string.IsNullOrEmpty(source.IosUrl) && source.IosUrl.Contains("tv.apple.com", StringComparison.OrdinalIgnoreCase))
                    {
                        AntiGravityLogger.Log($"Apple TV: using ios_url deep link: {source.IosUrl}");
                        return source.IosUrl;
                    }

                    // 2. Construct a region-aware Apple TV URL with the title
                    if (_details != null && !string.IsNullOrEmpty(_details.Title))
                    {
                        string regionPath = !string.IsNullOrEmpty(_selectedRegion) ? _selectedRegion.ToLowerInvariant() : "us";
                        string titleSlug = _details.Title.Replace(" ", "-").ToLowerInvariant();
                        string constructed = $"https://tv.apple.com/{regionPath}/search?term={Uri.EscapeDataString(_details.Title)}";
                        AntiGravityLogger.Log($"Apple TV: constructed deep link: {constructed}");
                        return constructed;
                    }

                    return "https://tv.apple.com/";
                }
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
            else if (name.Contains("disney") || name.Contains("hotstar")) domain = "hotstar.com";
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
            else if (name.Contains("jio")) domain = "jiocinema.com";
            else if (name.Contains("zee")) domain = "zee5.com";
            else if (name.Contains("liv")) domain = "sonyliv.com";
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
            if (RegionDetailComboBox.SelectedValue is string newRegion && !string.IsNullOrEmpty(newRegion))
            {
                if (newRegion != _selectedRegion)
                {
                    _selectedRegion = newRegion;
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
    }
}
