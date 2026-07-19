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
    public sealed partial class StreamingMoviesPage : Page
    {
        public StreamingMoviesViewModel ViewModel { get; } = AppServices.StreamingMoviesViewModel;

        public StreamingMoviesPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
            try
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PageContent);
                visual.Opacity = 0f;
            }
            catch { }
        }

        private readonly WatchmodeService _watchmodeService = new();

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
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

        private async void OnSearchBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
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

        private void OnSearchBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string query = args.QueryText;
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
            if (string.IsNullOrEmpty(ViewModel.ActiveSearchQuery)) _ = ViewModel.LoadMoviesAsync();
            else ViewModel.PerformSearchCommand.Execute(ViewModel.ActiveSearchQuery);
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(PageContent);
            var compositor = visual.Compositor;

            if (AppServices.Settings.Current.ReduceMotion)
            {
                visual.Opacity = 1f;
                visual.Offset = new System.Numerics.Vector3(0, 0, 0);
                PageContent.Opacity = 1.0;
                return;
            }

            visual.Opacity = 0f;
            visual.Offset = new System.Numerics.Vector3(0, 20, 0);

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

        private void OnMovieClicked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WatchmodeTitle movie)
            {
                Frame.Navigate(typeof(StreamingDetailsPage), (movie.Id, ViewModel.SelectedRegion));
            }
        }

        private void OnLibraryFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshLibraryList();
        }

        private void RefreshLibraryList()
        {
            if (LibraryGridView == null || LibraryFilterComboBox == null) return;

            var allItems = System.Linq.Enumerable.Where(AppServices.StreamingLibrary.SavedItems, i => i.Type == Services.Streaming.StreamingItemType.Movie);

            if (LibraryFilterComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string category = selectedItem.Content?.ToString() ?? "All";
                if (category != "All")
                {
                    allItems = System.Linq.Enumerable.Where(allItems, i => i.Watchlist == category);
                }
            }

            LibraryGridView.ItemsSource = System.Linq.Enumerable.ToList(allItems);
        }

        private async void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender != e.OriginalSource) return;
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is PivotItem pivotItem)
            {
                string header = pivotItem.Header?.ToString() ?? string.Empty;
                if (header == "Library")
                {
                    RefreshLibraryList();
                }
                else if (header == "Catalog Changes")
                {
                    await LoadCatalogChangesAsync();
                }
            }
        }

        private async System.Threading.Tasks.Task LoadCatalogChangesAsync()
        {
            if (CatalogChangesListView == null) return;

            try
            {
                string startDate = DateTime.Today.AddDays(-5).ToString("yyyyMMdd");
                string endDate = DateTime.Today.ToString("yyyyMMdd");
                var changesResponse = await _watchmodeService.GetChangesAsync(startDate, endDate);

                if (changesResponse?.Changes != null)
                {
                    var filteredChanges = System.Linq.Enumerable.ToList(
                        System.Linq.Enumerable.Where(changesResponse.Changes, c => c.Type == "movie")
                    );
                    CatalogChangesListView.ItemsSource = filteredChanges;
                }
            }
            catch { }
        }

        private void OnCatalogChangeItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WatchmodeChangeItem changeItem)
            {
                Frame.Navigate(typeof(StreamingDetailsPage), (changeItem.Id, ViewModel.SelectedRegion));
            }
        }

        private void LibraryGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SavedStreamingItem item)
            {
                if (int.TryParse(item.Id, out int watchmodeId))
                {
                    Frame.Navigate(typeof(StreamingDetailsPage), (watchmodeId, ViewModel.SelectedRegion));
                }
            }
        }
        private void Card_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                var compositor = visual.Compositor;
                
                if (border.Tag == null)
                {
                    try
                    {
                        var shadowVisual = compositor.CreateSpriteVisual();
                        var shadow = compositor.CreateDropShadow();
                        shadow.BlurRadius = 24f;
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
                scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.04f, 1.04f, 1.0f));
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
        }

        private void Card_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
                var compositor = visual.Compositor;
                
                var dropShadow = border.Tag as Microsoft.UI.Composition.DropShadow;
                if (dropShadow != null)
                {
                    var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
                    opacityAnim.InsertKeyFrame(1.0f, 0.0f);
                    opacityAnim.Duration = TimeSpan.FromMilliseconds(200);
                    dropShadow.StartAnimation("Opacity", opacityAnim);
                    
                    var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
                    offsetAnim.InsertKeyFrame(1.0f, new System.Numerics.Vector3(0, 4, 8));
                    offsetAnim.Duration = TimeSpan.FromMilliseconds(200);
                    dropShadow.StartAnimation("Offset", offsetAnim);
                }

                var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.0f, 1.0f, 1.0f));
                scaleAnim.Duration = TimeSpan.FromMilliseconds(200);
                
                visual.CenterPoint = new System.Numerics.Vector3((float)border.RenderSize.Width / 2, (float)border.RenderSize.Height / 2, 0);
                visual.StartAnimation("Scale", scaleAnim);

                border.Translation = new System.Numerics.Vector3(0, 0, 8);

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
        }
    }
}
