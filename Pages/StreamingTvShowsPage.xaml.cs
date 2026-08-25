using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using LumiereMediaPlayer.ViewModels;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services.Streaming;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingTvShowsPage : Page
    {
        public StreamingTvShowsViewModel ViewModel { get; } = AppServices.StreamingTvShowsViewModel;

        public Visibility GetDiscoverEmptyState(bool isLoading, bool hasError) =>
            (ViewModel.TvShows != null && ViewModel.TvShows.Count == 0 && !isLoading && !hasError) ? Visibility.Visible : Visibility.Collapsed;

        public StreamingTvShowsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            this.DataContext = this;
            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _heroTimer?.Stop();
        }

        private readonly WatchmodeService _watchmodeService = new();

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            try
            {
                try
                {
                    try
                    {
                        base.OnNavigatedTo(e);
                    
                        // Refresh library items in case we are returning from Details Page where they changed
                        RefreshLibraryList();
                    
                        if (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back)
                        {
                            // Preserve search results, active filters, and pivot tabs when returning from Details Page
                            return;
                        }

                        ViewModel.ResetState();

                        if (MainPivot != null)
                        {
                            MainPivot.SelectedIndex = 0;
                        }
                        if (SearchBox != null)
                        {
                            SearchBox.Text = string.Empty;
                        }
                        await ViewModel.InitializeAndLoadAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StreamingTvShowsPage] OnNavigatedTo error: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OnNavigatedTo: {ex.Message}");
            }
        }

        private async void OnSearchBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                try
                {
                    if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                    {
                        string query = sender.Text;
                        if (query.Length >= 3)
                        {
                            try
                            {
                                var suggestions = await ViewModel.WatchmodeSearchSuggestionsAsync(query);
                                sender.ItemsSource = suggestions;
                            }
                            catch { }
                        }
                        else
                        {
                            sender.ItemsSource = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OnSearchBoxTextChanged: {ex.Message}");
            }
        }

        private void OnSearchBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string? query = args.ChosenSuggestion != null ? args.ChosenSuggestion.ToString() : args.QueryText;
            if (!string.IsNullOrWhiteSpace(query))
            {
                ViewModel.PerformSearchCommand.Execute(query);
            }
            else
            {
                ViewModel.PerformSearchCommand.Execute(string.Empty);
            }
        }

        private void RegionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && !comboBox.IsDropDownOpen)
            {
                TriggerRegionReload();
            }
        }

        private void RegionComboBox_DropDownClosed(object sender, object e)
        {
            TriggerRegionReload();
        }

        private void TriggerRegionReload()
        {
            if (ViewModel == null) return;
            if (string.IsNullOrEmpty(ViewModel.ActiveSearchQuery)) _ = ViewModel.LoadTvShowsAsync();
            else ViewModel.PerformSearchCommand.Execute(ViewModel.ActiveSearchQuery);
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
                System.Diagnostics.Debug.WriteLine($"Failed to animate StreamingTvShowsPage entrance: {ex.Message}");
                PageContent.Opacity = 1.0;
            }
        }

        private void OnTvShowClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WatchmodeTitle tvShow)
            {
                var container = ((GridView)sender).ContainerFromItem(e.ClickedItem) as UIElement;
                if (container != null)
                {
                    Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("PosterAnimation", container);
                }
                Frame.Navigate(typeof(StreamingDetailsPage), (tvShow.Id, ViewModel.SelectedRegion));
            }
        }

        private void OnLibraryFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshLibraryList();
        }

        private void RefreshLibraryList()
        {
            if (LibraryGridView == null || LibraryFilterComboBox == null) return;

            var allItems = System.Linq.Enumerable.Where(AppServices.StreamingLibrary.SavedItems, i => i.Type == Services.Streaming.StreamingItemType.TvShow);

            if (LibraryFilterComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string category = selectedItem.Content?.ToString() ?? "All";
                if (category != "All")
                {
                    allItems = System.Linq.Enumerable.Where(allItems, i => i.Watchlist == category);
                }
            }

            var list = System.Linq.Enumerable.ToList(allItems);
            LibraryGridView.ItemsSource = list;
            if (LibraryEmptyStatePanel != null)
            {
                LibraryEmptyStatePanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }


        private async void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selectedItem = MainPivot.SelectedItem as PivotItem;
                string header = selectedItem?.Header?.ToString() ?? string.Empty;
                if (header == "Library")
                {
                    RefreshLibraryList();
                }
                else if (header == "Trending")
                {
                    await LoadTrendingAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in MainPivot_SelectionChanged: {ex.Message}");
            }
        }

        private readonly TmdbService _tmdbService = new();

        private DispatcherTimer? _heroTimer;

        private async System.Threading.Tasks.Task LoadTrendingAsync()
        {
            try
            {
                int retries = 0;
                while ((TrendingGridView == null || TrendingHeroCarousel == null) && retries < 20)
                {
                    await System.Threading.Tasks.Task.Delay(50);
                    retries++;
                }
                if (TrendingGridView == null || TrendingHeroCarousel == null) return;

                var popularTvShows = await _tmdbService.GetPopularTvShowsAsync(1);
                if (popularTvShows == null || popularTvShows.Count == 0) return;

                var heroItems = popularTvShows.Take(5).ToList();
                var gridItems = popularTvShows.Skip(5).ToList();
                
                TrendingHeroCarousel.ItemsSource = heroItems;
                TrendingGridView.ItemsSource = gridItems;

                if (_heroTimer == null)
                {
                    _heroTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                    _heroTimer.Tick += (s, e) =>
                    {
                        try
                        {
                            if (TrendingHeroCarousel != null && TrendingHeroCarousel.ItemsSource is System.Collections.IList list && list.Count > 0)
                            {
                                TrendingHeroCarousel.SelectedIndex = (TrendingHeroCarousel.SelectedIndex + 1) % list.Count;
                            }
                        }
                        catch { }
                    };
                }
                CollapseInternalFlipViewButtons(TrendingHeroCarousel);
                TrendingHeroCarousel.Loaded += (s, e) => CollapseInternalFlipViewButtons(TrendingHeroCarousel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadTrendingAsync TV shows error: {ex.Message}");
            }
        }

        private void CollapseInternalFlipViewButtons(DependencyObject? parent)
        {
            if (parent == null) return;
            try
            {
                int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < count; i++)
                {
                    var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                    if (child is Button btn && (btn.Name == "PreviousButtonHorizontal" || btn.Name == "NextButtonHorizontal" || btn.Name == "PreviousButtonVertical" || btn.Name == "NextButtonVertical"))
                    {
                        btn.Visibility = Visibility.Collapsed;
                        btn.Opacity = 0;
                        btn.Width = 0;
                        btn.Height = 0;
                        btn.MinWidth = 0;
                        btn.MinHeight = 0;
                        btn.MaxWidth = 0;
                        btn.MaxHeight = 0;
                        btn.IsEnabled = false;
                        btn.IsHitTestVisible = false;
                    }
                    else
                    {
                        CollapseInternalFlipViewButtons(child);
                    }
                }
            }
            catch { }
        }

        private void OnCarouselPrevClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TrendingHeroCarousel?.ItemsSource is System.Collections.IList list && list.Count > 0)
                {
                    int prev = TrendingHeroCarousel.SelectedIndex - 1;
                    if (prev < 0) prev = list.Count - 1;
                    TrendingHeroCarousel.SelectedIndex = prev;
                }
            }
            catch { }
        }

        private void OnCarouselNextClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TrendingHeroCarousel?.ItemsSource is System.Collections.IList list && list.Count > 0)
                {
                    int next = (TrendingHeroCarousel.SelectedIndex + 1) % list.Count;
                    TrendingHeroCarousel.SelectedIndex = next;
                }
            }
            catch { }
        }

        private void OnHeroViewDetailsClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TmdbMedia tmdbItem)
            {
                Frame.Navigate(typeof(StreamingDetailsPage), ($"tmdb_tv-{tmdbItem.Id}", ViewModel.SelectedRegion));
            }
        }

        private void OnHeroPlayTrailerClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TmdbMedia tmdbItem)
            {
                string title = !string.IsNullOrEmpty(tmdbItem.DisplayTitle) ? tmdbItem.DisplayTitle : "TV Show";
                string query = $"{title} {tmdbItem.DisplayYear} tv trailer";
                string url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
                Frame.Navigate(typeof(StreamingYouTubePage), url);
            }
        }

        private void OnContextViewDetailsClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                if (fe.DataContext is WatchmodeTitle wm)
                    Frame.Navigate(typeof(StreamingDetailsPage), (wm.Id, ViewModel.SelectedRegion));
                else if (fe.DataContext is TmdbMedia tm)
                    Frame.Navigate(typeof(StreamingDetailsPage), ($"tmdb_tv-{tm.Id}", ViewModel.SelectedRegion));
                else if (fe.DataContext is SavedStreamingItem saved)
                {
                    if (saved.Id.StartsWith("tmdb_"))
                        Frame.Navigate(typeof(StreamingDetailsPage), (saved.Id, ViewModel.SelectedRegion));
                    else if (int.TryParse(saved.Id, out int wid))
                        Frame.Navigate(typeof(StreamingDetailsPage), (wid, ViewModel.SelectedRegion));
                }
            }
        }

        private void OnContextAddWatchlistClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                if (fe.DataContext is WatchmodeTitle wm)
                {
                    AppServices.StreamingLibrary.AddItem(new SavedStreamingItem { Id = wm.Id.ToString(), Title = wm.Title ?? "", Subtitle = $"({wm.Year})", PosterUrl = wm.PosterUrl, Type = Services.Streaming.StreamingItemType.TvShow, Watchlist = "Watchlist" });
                }
                else if (fe.DataContext is TmdbMedia tm)
                {
                    AppServices.StreamingLibrary.AddItem(new SavedStreamingItem { Id = $"tmdb_tv-{tm.Id}", Title = tm.DisplayTitle ?? "", Subtitle = $"({tm.DisplayYear})", PosterUrl = tm.PosterUrl, Type = Services.Streaming.StreamingItemType.TvShow, Watchlist = "Watchlist" });
                }
            }
        }

        private void OnContextPlayTrailerClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                string title = "";
                if (fe.DataContext is WatchmodeTitle wm) title = $"{wm.Title} {wm.Year} trailer";
                else if (fe.DataContext is TmdbMedia tm) title = $"{tm.DisplayTitle} {tm.DisplayYear} trailer";

                if (!string.IsNullOrEmpty(title))
                {
                    string url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(title)}";
                    Frame.Navigate(typeof(StreamingYouTubePage), url);
                }
            }
        }

        private void OnContextRemoveFromLibraryClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SavedStreamingItem saved)
            {
                AppServices.StreamingLibrary.RemoveItem(saved.Id, Services.Streaming.StreamingItemType.TvShow);
                RefreshLibraryList();
            }
        }

        private void OnTrendingItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TmdbMedia tmdbItem)
            {
                var container = ((GridView)sender).ContainerFromItem(e.ClickedItem) as UIElement;
                if (container != null)
                {
                    Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("PosterAnimation", container);
                }
                Frame.Navigate(typeof(StreamingDetailsPage), ($"tmdb_tv-{tmdbItem.Id}", ViewModel.SelectedRegion));
            }
        }

        private void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SavedStreamingItem item)
            {
                var container = ((GridView)sender).ContainerFromItem(e.ClickedItem) as UIElement;
                if (container != null)
                {
                    Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("PosterAnimation", container);
                }
                if (item.Id.StartsWith("tmdb_"))
                {
                    Frame.Navigate(typeof(StreamingDetailsPage), (item.Id, ViewModel.SelectedRegion));
                }
                else if (int.TryParse(item.Id, out int watchmodeId))
                {
                    Frame.Navigate(typeof(StreamingDetailsPage), (watchmodeId, ViewModel.SelectedRegion));
                }
            }
        }
        private void Card_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                try
                {
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                    if (visual == null) return;
                    var compositor = visual.Compositor;
                    if (compositor == null) return;
                    
                    if (border.Tag == null)
                    {
                        try
                        {
                            var shadowVisual = compositor.CreateSpriteVisual();
                            var shadow = compositor.CreateDropShadow();
                            shadow.BlurRadius = 32f;
                            shadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                            shadow.Opacity = 0.0f;
                            shadow.Offset = new System.Numerics.Vector3(0, 4, 0);
                            
                            shadowVisual.Shadow = shadow;
                            
                            var bindSizeAnimation = compositor.CreateExpressionAnimation("visual.Size");
                            bindSizeAnimation.SetReferenceParameter("visual", visual);
                            shadowVisual.StartAnimation("Size", bindSizeAnimation);
                            
                            if (visual.Parent is Microsoft.UI.Composition.ContainerVisual container)
                            {
                                container.Children.InsertBelow(shadowVisual, visual);
                            }
                            
                            border.Tag = shadow;
                        }
                        catch { }
                    }

                    var dropShadow = border.Tag as Microsoft.UI.Composition.DropShadow;
                    if (dropShadow != null)
                    {
                        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                        opacityAnim.InsertKeyFrame(1.0f, 0.55f);
                        opacityAnim.Duration = TimeSpan.FromMilliseconds(250);
                        dropShadow.StartAnimation("Opacity", opacityAnim);
                        
                        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
                        offsetAnim.InsertKeyFrame(1.0f, new System.Numerics.Vector3(0, 8, 16));
                        offsetAnim.Duration = TimeSpan.FromMilliseconds(250);
                        dropShadow.StartAnimation("Offset", offsetAnim);
                    }

                    var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                    scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.06f, 1.06f, 1.0f));
                    scaleAnim.Duration = TimeSpan.FromMilliseconds(250);
                    
                    visual.CenterPoint = new System.Numerics.Vector3((float)border.RenderSize.Width / 2, (float)border.RenderSize.Height / 2, 0);
                    visual.StartAnimation("Scale", scaleAnim);

                    border.Translation = new System.Numerics.Vector3(0, 0, 16);

                    Border? overlay = null;
                    if (border.Child is Grid grid)
                    {
                        foreach (var child in grid.Children)
                        {
                            if (child is Border b && b.Name == "HoverOverlay")
                            {
                                overlay = b;
                                break;
                            }
                        }
                    }

                    if (overlay != null)
                    {
                        var anim = new DoubleAnimation
                        {
                            To = 1.0,
                            Duration = TimeSpan.FromMilliseconds(250),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };
                        var sb = new Storyboard();
                        Storyboard.SetTarget(anim, overlay);
                        Storyboard.SetTargetProperty(anim, "Opacity");
                        sb.Children.Add(anim);
                        sb.Begin();
                    }

                    if (Application.Current.Resources.TryGetValue("SystemControlHighlightAccentBrush", out var accentBrush))
                    {
                        border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)accentBrush;
                    }
                }
                catch { }
            }
        }

        private void Card_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                try
                {
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                    if (visual == null) return;
                    var compositor = visual.Compositor;
                    if (compositor == null) return;
                    
                    var dropShadow = border.Tag as Microsoft.UI.Composition.DropShadow;
                    if (dropShadow != null)
                    {
                        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                        opacityAnim.InsertKeyFrame(1.0f, 0.0f);
                        opacityAnim.Duration = TimeSpan.FromMilliseconds(200);
                        dropShadow.StartAnimation("Opacity", opacityAnim);
                        
                        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
                        offsetAnim.InsertKeyFrame(1.0f, new System.Numerics.Vector3(0, 6, 12));
                        offsetAnim.Duration = TimeSpan.FromMilliseconds(200);
                        dropShadow.StartAnimation("Offset", offsetAnim);
                    }

                    var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                    scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.0f, 1.0f, 1.0f));
                    scaleAnim.Duration = TimeSpan.FromMilliseconds(200);
                    
                    visual.CenterPoint = new System.Numerics.Vector3((float)border.RenderSize.Width / 2, (float)border.RenderSize.Height / 2, 0);
                    visual.StartAnimation("Scale", scaleAnim);

                    border.Translation = new System.Numerics.Vector3(0, 0, 16);

                    Border? overlay = null;
                    if (border.Child is Grid grid)
                    {
                        foreach (var child in grid.Children)
                        {
                            if (child is Border b && b.Name == "HoverOverlay")
                            {
                                overlay = b;
                                break;
                            }
                        }
                    }

                    if (overlay != null)
                    {
                        var anim = new DoubleAnimation
                        {
                            To = 0.0,
                            Duration = TimeSpan.FromMilliseconds(200),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };
                        var sb = new Storyboard();
                        Storyboard.SetTarget(anim, overlay);
                        Storyboard.SetTargetProperty(anim, "Opacity");
                        sb.Children.Add(anim);
                        sb.Begin();
                    }

                    if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var defaultBrush))
                    {
                        border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)defaultBrush;
                    }
                }
                catch { }
            }
        }
    }
}
