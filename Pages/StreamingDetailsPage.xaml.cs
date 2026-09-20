using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.ViewModels;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingDetailsPage : Page
    {
        public StreamingDetailsViewModel ViewModel { get; } = new();

        public string? CurrentTitleType => ViewModel.CurrentTitleType;

        private bool _isPersonDialogOpen;

        public StreamingDetailsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Disabled;
            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            this.Unloaded -= OnUnloaded;
        }

        private void OnBackButtonClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                if (string.Equals(CurrentTitleType, "tv_series", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(CurrentTitleType, "tv_miniseries", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(CurrentTitleType, "tv", StringComparison.OrdinalIgnoreCase))
                {
                    Frame.Navigate(typeof(StreamingTvShowsPage));
                }
                else
                {
                    Frame.Navigate(typeof(StreamingMoviesPage));
                }
            }
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

                int watchmodeId = 0;
                string selectedRegion = string.Empty;
                string fallback = string.Empty;

                if (e.Parameter is int id)
                {
                    watchmodeId = id;
                }
                else if (e.Parameter is (int tupleId, string region))
                {
                    watchmodeId = tupleId;
                    selectedRegion = region;
                }
                else if (e.Parameter is (string tupleStr, string tupleReg))
                {
                    if (int.TryParse(tupleStr, out int parsedId) && !tupleStr.StartsWith("tmdb_"))
                    {
                        watchmodeId = parsedId;
                    }
                    else
                    {
                        fallback = tupleStr;
                        watchmodeId = -1;
                    }
                    selectedRegion = tupleReg;
                }
                else if (e.Parameter is string tmdbStr)
                {
                    if (int.TryParse(tmdbStr, out int parsedId) && !tmdbStr.StartsWith("tmdb_"))
                    {
                        watchmodeId = parsedId;
                    }
                    else
                    {
                        fallback = tmdbStr;
                        watchmodeId = -1;
                    }
                }

                if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Visible;

                try
                {
                    await ViewModel.LoadDetailsAsync(watchmodeId, selectedRegion, fallback);
                    ApplyViewModelToUI();
                }
                finally
                {
                    if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OnNavigatedTo: {ex.Message}");
                if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyViewModelToUI()
        {
            var details = ViewModel.Details;
            if (details == null)
            {
                TitleText.Text = ViewModel.ErrorMessage ?? "Failed to load details.";
                return;
            }

            // Populate text fields
            TitleText.Text = details.Title ?? "Unknown Title";
            YearText.Text = details.Year?.ToString() ?? string.Empty;
            RatingText.Text = details.UserRating != null ? $"⭐ {details.UserRating:F1}" : "No Rating";
            RuntimeText.Text = details.RuntimeMinutes != null ? $"{details.RuntimeMinutes} min" : string.Empty;

            TypeText.Text = details.Type switch
            {
                "movie" => "MOVIE",
                "tv_series" => "TV SHOW",
                "tv_miniseries" => "MINISERIES",
                _ => details.Type?.ToUpperInvariant() ?? "UNKNOWN"
            };

            App.MainWindowInstance?.SelectStreamingTabForTitleType(details.Type);

            if (details.GenreNames != null)
            {
                GenresText.Text = string.Join(" • ", details.GenreNames);
            }

            // Multi-platform scores
            PopulateScores(ViewModel.Scores);

            OverviewText.Text = details.PlotOverview ?? "No synopsis available.";

            // Poster
            if (!string.IsNullOrEmpty(details.DisplayPoster))
            {
                var bmp = new BitmapImage { DecodePixelWidth = 360, UriSource = new Uri(details.DisplayPoster) };
                PosterImage.Source = bmp;
            }

            // Watchlist status
            UpdateLibraryButtonStatus();

            // Trailer button
            TrailerButton.Visibility = ViewModel.HasTrailer ? Visibility.Visible : Visibility.Collapsed;

            // Region Detail Dropdown
            RegionDetailComboBox.ItemsSource = RegionHelper.GetAllRegions();
            RegionDetailComboBox.SelectedValue = ViewModel.SelectedRegion;

            // Where to Watch section
            BuildProvidersSection();

            // Cast & Crew
            CastGridView.ItemsSource = ViewModel.Cast;
            CrewGridView.ItemsSource = ViewModel.Crew;
            if (ViewModel.Crew.Count == 0)
            {
                DetailsPivot.Items.Remove(CrewPivotItem);
            }
            else if (!DetailsPivot.Items.Contains(CrewPivotItem))
            {
                int castIndex = DetailsPivot.Items.IndexOf(CastPivotItem);
                int insertIndex = Math.Clamp(castIndex >= 0 ? castIndex + 1 : 1, 0, DetailsPivot.Items.Count);
                DetailsPivot.Items.Insert(insertIndex, CrewPivotItem);
            }

            // Similar titles
            if (ViewModel.SimilarTitles.Count > 0)
            {
                SimilarTitlesGridView.ItemsSource = ViewModel.SimilarTitles;
                if (!DetailsPivot.Items.Contains(SimilarPivotItem))
                    DetailsPivot.Items.Add(SimilarPivotItem);
            }
            else
            {
                DetailsPivot.Items.Remove(SimilarPivotItem);
            }

            // Releases
            if (ViewModel.Releases.Count > 0)
            {
                ReleasesListView.ItemsSource = ViewModel.Releases;
                if (!DetailsPivot.Items.Contains(ReleasesPivotItem))
                    DetailsPivot.Items.Add(ReleasesPivotItem);
            }
            else
            {
                DetailsPivot.Items.Remove(ReleasesPivotItem);
            }

            // TV show hierarchy TreeView
            if (details.Type == "tv_series" || details.Type == "tv_miniseries")
            {
                if (!DetailsPivot.Items.Contains(EpisodesPivotItem))
                    DetailsPivot.Items.Add(EpisodesPivotItem);
                PopulateEpisodesTree(ViewModel.Seasons, ViewModel.Episodes);
            }
            else
            {
                if (DetailsPivot.Items.Contains(EpisodesPivotItem))
                    DetailsPivot.Items.Remove(EpisodesPivotItem);
            }
        }

        private void PopulateScores(WatchmodeScores? scores)
        {
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
        }

        private void PopulateQualityBadges()
        {
            QualityBadgesPanel.Children.Clear();
            QualityBadgesPanel.Visibility = Visibility.Collapsed;

            var badges = ViewModel.QualityBadges;
            if (badges == null || badges.Count == 0) return;

            foreach (var badge in badges)
            {
                QualityBadgesPanel.Children.Add(CreateBadge(badge.Text, badge.Tooltip));
            }

            if (QualityBadgesPanel.Children.Count > 0)
            {
                QualityBadgesPanel.Visibility = Visibility.Visible;
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

        private void BuildProvidersSection()
        {
            ProvidersContainer.Children.Clear();

            // Local media card
            if (ViewModel.LocalMatch != null)
            {
                var card = BuildLocalMediaCard(ViewModel.LocalMatch);
                ProvidersContainer.Children.Add(card);
                ProvidersContainer.Children.Add(new Border { Height = 16 });
            }

            // Quality badges
            PopulateQualityBadges();

            var grouped = ViewModel.GroupedSources;
            if (!grouped.HasAnySources)
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

            if (grouped.SubscriptionSources.Count > 0)
            {
                ProvidersContainer.Children.Add(new TextBlock { Text = "Subscription Streaming", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 0) });
                ProvidersContainer.Children.Add(BuildProviderWrapPanel(grouped.SubscriptionSources, "Subscription"));
            }

            if (grouped.FreeSources.Count > 0)
            {
                ProvidersContainer.Children.Add(new TextBlock { Text = "Free Streaming", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) });
                ProvidersContainer.Children.Add(BuildProviderWrapPanel(grouped.FreeSources, "Free"));
            }

            if (grouped.PurchaseSources.Count > 0)
            {
                ProvidersContainer.Children.Add(new TextBlock { Text = "Buy or Rent", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) });
                ProvidersContainer.Children.Add(BuildProviderWrapPanel(grouped.PurchaseSources, "Rent/Buy"));
            }
        }

        private Button BuildLocalMediaCard(MediaItem match)
        {
            var card = new Button
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

            return card;
        }

        private FrameworkElement BuildProviderWrapPanel(List<WatchmodeSource> sourcesList, string labelType)
        {
            var panel = new VariableSizedWrapGrid
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = 155,
                ItemHeight = 125
            };

            foreach (var source in sourcesList)
            {
                var resolvedUrl = StreamingProviderHelper.ResolveProviderUrl(source, ViewModel.Details);
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

                var iconUrl = StreamingProviderHelper.GetProviderIconUrl(source);
                var bmp = new BitmapImage { DecodePixelWidth = 48, UriSource = new Uri(iconUrl) };
                var logo = new Image
                {
                    Source = bmp,
                    Width = 40,
                    Height = 40,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                };
                contentPanel.Children.Add(logo);

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

                string currencySymbol = StreamingProviderHelper.GetCurrencySymbol(ViewModel.SelectedRegion);
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

                var targetUrl = resolvedUrl;
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
                            string cleanUrl = StreamingRouter.CleanFallbackUrl(url);

                            if (cleanUrl.Contains("tv.apple.com", StringComparison.OrdinalIgnoreCase) || cleanUrl.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
                            {
                                string targetRegion = AppleTvDeepLinkHelper.GetCurrentRegion();
                                string mediaType = (CurrentTitleType?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true) ? "movie" : "tvShow";
                                cleanUrl = await AppleTvDeepLinkHelper.ResolveAppleTvUrlAsync(ViewModel.Details?.Title ?? "", mediaType, cleanUrl, targetRegion);
                                AntiGravityLogger.Log($"Apple TV URL resolved to canonical deep link: {cleanUrl}");
                            }

                            var nativeUri = StreamingRouter.GetNativeUri(cleanUrl);
                            AntiGravityLogger.Log($"Launching provider URI (Native): {nativeUri}, Fallback: {cleanUrl}");

                            await StreamingRouter.LaunchStreamUriAsync(nativeUri, cleanUrl);
                        }
                        catch (Exception ex)
                        {
                            AntiGravityLogger.Log($"Failed to launch URI: {ex.Message}");
                            try
                            {
                                string cleanUrl = StreamingRouter.CleanFallbackUrl(url);
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

        private void PopulateEpisodesTree(List<WatchmodeSeason> seasons, List<WatchmodeEpisode> episodes)
        {
            EpisodesTreeView.RootNodes.Clear();

            if (seasons == null || seasons.Count == 0) return;

            var sortedSeasons = seasons.OrderBy(s => s.Number).ToList();

            foreach (var season in sortedSeasons)
            {
                var seasonContent = new TreeViewItemContent
                {
                    Title = season.Name ?? $"Season {season.Number}",
                    Subtitle = $"{season.EpisodeCount} Episodes"
                };

                var seasonNode = new TreeViewNode { Content = seasonContent };

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

        private void UpdateLibraryButtonStatus()
        {
            ViewModel.UpdateLibraryStatus();
            if (ViewModel.IsSaved)
            {
                LibraryIcon.Glyph = "\uE738"; // Checkmark
                LibraryButtonText.Text = $"Saved ({ViewModel.SavedWatchlistCategory})";
            }
            else
            {
                LibraryIcon.Glyph = "\uE710"; // Add
                LibraryButtonText.Text = "Add to Watchlist";
            }
        }

        private void OnLibraryFlyoutOpening(object sender, object e)
        {
            ViewModel.UpdateLibraryStatus();
            foreach (var item in LibraryMenuFlyout.Items)
            {
                if (item is MenuFlyoutItem menuItem)
                {
                    if (menuItem.Name == "RemoveLibraryItem")
                    {
                        menuItem.Visibility = ViewModel.IsSaved ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else if (menuItem.Tag is string category)
                    {
                        if (ViewModel.IsSaved && ViewModel.SavedWatchlistCategory == category)
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
            if (sender is not MenuFlyoutItem menuItem || menuItem.Tag is not string category) return;
            ViewModel.SaveToWatchlist(category);
            UpdateLibraryButtonStatus();
        }

        private void OnRemoveFromLibraryClick(object sender, RoutedEventArgs e)
        {
            ViewModel.RemoveFromWatchlist();
            UpdateLibraryButtonStatus();
        }

        private async void OnTrailerButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var trailer = ViewModel.Details?.Trailer;
                if (!string.IsNullOrEmpty(trailer))
                {
                    if (trailer.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                        trailer.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                    {
                        App.MainWindowInstance?.NavigateToYouTube(trailer);
                    }
                    else
                    {
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(trailer));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OnTrailerButtonClick: {ex.Message}");
            }
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AppServices.Settings.Current.ReduceMotion)
                {
                    try
                    {
                        var v = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PageContent);
                        v.Opacity = 1f;
                    }
                    catch { }
                    PageContent.Opacity = 1.0;
                    return;
                }

                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PageContent);
                var compositor = visual.Compositor;

                var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
                fadeAnim.InsertKeyFrame(0f, 0f);
                fadeAnim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f)));
                fadeAnim.Duration = TimeSpan.FromMilliseconds(400);

                var slideAnim = compositor.CreateVector3KeyFrameAnimation();
                slideAnim.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 20, 0));
                slideAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0), compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f)));
                slideAnim.Duration = TimeSpan.FromMilliseconds(450);

                visual.StartAnimation("Opacity", fadeAnim);
                visual.StartAnimation("Offset", slideAnim);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to animate StreamingDetailsPage entrance: {ex.Message}");
                PageContent.Opacity = 1.0;
            }
        }

        private async void RegionDetailComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (RegionDetailComboBox.SelectedValue is string newRegion && !string.IsNullOrEmpty(newRegion))
                {
                    if (newRegion != ViewModel.SelectedRegion)
                    {
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

                        await ViewModel.ChangeRegionAsync(newRegion);
                        BuildProvidersSection();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in RegionDetailComboBox_SelectionChanged: {ex.Message}");
            }
        }

        private void SimilarTitlesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WatchmodeTitle clickedTitle)
            {
                Frame.Navigate(typeof(StreamingDetailsPage), clickedTitle.Id);
            }
        }

        private async void CastGridView_ItemClick(object sender, ItemClickEventArgs e)
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
                    try
                    {
                        var details = await ViewModel.GetPersonDetailsAsync(person.PersonId, person.FullName);
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                        {
                            try
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
                                        MaxHeight = 350,
                                        ItemTemplate = CreateFilmographyItemTemplate(),
                                        ItemsSource = details.KnownFor
                                    };

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
                            }
                            catch { }
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StreamingDetailsPage] GetPersonDetailsAsync error: {ex.Message}");
                    }
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingDetailsPage] OnPageUnloaded error: {ex.Message}");
            }
        }
    }
}
