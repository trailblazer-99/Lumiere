using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace LumiereMediaPlayer.Controls;

public sealed partial class MediaCard : UserControl
{
    public static readonly DependencyProperty CardWidthProperty =
        DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(MediaCard), new PropertyMetadata(168.0));

    public static readonly DependencyProperty CardHeightProperty =
        DependencyProperty.Register(nameof(CardHeight), typeof(double), typeof(MediaCard), new PropertyMetadata(168.0));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(MediaCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(string), typeof(MediaCard),
            new PropertyMetadata(null, OnAccentColorChanged));

    public static readonly DependencyProperty ArtworkProperty =
        DependencyProperty.Register(nameof(Artwork), typeof(ImageSource), typeof(MediaCard),
            new PropertyMetadata(null, (d, e) => ((MediaCard)d).UpdateDisplayImage()));

    public static readonly DependencyProperty PosterUrlProperty =
        DependencyProperty.Register(nameof(PosterUrl), typeof(string), typeof(MediaCard),
            new PropertyMetadata(null, (d, e) => ((MediaCard)d).UpdateDisplayImage()));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(MediaCard),
            new PropertyMetadata(false, (d, e) => ((MediaCard)d).OnIsSelectedChanged((bool)e.NewValue)));

    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(MediaItem), typeof(MediaCard),
            new PropertyMetadata(null, (d, e) => ((MediaCard)d).OnItemChanged(e.NewValue as MediaItem)));

    public static readonly DependencyProperty LocationRepProperty =
        DependencyProperty.Register(nameof(LocationRep), typeof(string), typeof(MediaCard), new PropertyMetadata(null));

    public event EventHandler? SelectionChanged;

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public string? LocationRep
    {
        get => (string?)GetValue(LocationRepProperty);
        set => SetValue(LocationRepProperty, value);
    }

    public Visibility LocationRepVisibility => VisibilityHelper.FromBoolean(!string.IsNullOrWhiteSpace(LocationRep) || (Item?.HasLocationRep == true));
    public string EffectiveLocationRep => !string.IsNullOrWhiteSpace(LocationRep) ? LocationRep : (Item?.LocationRep ?? string.Empty);
    public string EffectiveToolTip => Item?.SourcePath ?? EffectiveLocationRep;

    private MediaItem? _subscribedItem;

    private void OnItemChanged(MediaItem? newItem)
    {
        if (_subscribedItem != null)
        {
            _subscribedItem.PropertyChanged -= OnSubscribedItemPropertyChanged;
            _subscribedItem = null;
        }

        if (newItem != null)
        {
            _subscribedItem = newItem;
            _subscribedItem.PropertyChanged += OnSubscribedItemPropertyChanged;
            IsSelected = newItem.IsSelected;
            if (string.IsNullOrEmpty(LocationRep))
            {
                LocationRep = newItem.LocationRep;
            }
        }
        else
        {
            IsSelected = false;
        }
    }

    private void OnSubscribedItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaItem.IsSelected) && sender is MediaItem mi)
        {
            if (DispatcherQueue?.HasThreadAccess == true)
            {
                if (IsSelected != mi.IsSelected)
                {
                    IsSelected = mi.IsSelected;
                }
            }
            else
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (IsSelected != mi.IsSelected)
                    {
                        IsSelected = mi.IsSelected;
                    }
                });
            }
        }
    }

    private void OnIsSelectedChanged(bool isSelected)
    {
        if (SelectionBorder != null)
        {
            SelectionBorder.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }
        if (Item != null && Item.IsSelected != isSelected)
        {
            Item.IsSelected = isSelected;
        }
        else if (DataContext is MediaItem ctxItem && ctxItem.IsSelected != isSelected)
        {
            ctxItem.IsSelected = isSelected;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private MediaItem? GetAssociatedMediaItem()
    {
        if (Item != null) return Item;
        if (DataContext is MediaItem ctx) return ctx;
        return null;
    }

    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        var mediaItem = GetAssociatedMediaItem();
        if (mediaItem == null)
        {
            mediaItem = new MediaItem
            {
                Title = this.Title,
                Artist = this.Subtitle,
                PosterUrl = this.PosterUrl,
                AccentColor = this.AccentColor
            };
        }

        var flyout = MediaFlyoutHelper.CreateMediaFlyout(mediaItem, this, () =>
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        });
        flyout.ShowAt(this, e.GetPosition(this));
    }

    public ImageSource? DisplayImage
    {
        get
        {
            if (Artwork != null) return Artwork;
            if (!string.IsNullOrWhiteSpace(PosterUrl))
            {
                return ImageBindHelper.SafeImageFromUrl(PosterUrl, 300);
            }
            return null;
        }
    }

    public void UpdateDisplayImage()
    {
        try
        {
            var src = DisplayImage;
            if (PosterImageElement != null)
            {
                PosterImageElement.Source = src;
            }
        }
        catch { }
    }

    public FrameworkElement TitleContainerElement => TitleContainer;

    public static DateTime LastScrollActivityTime => ScrollDebounceHelper.LastScrollActivityTime;

    public static bool IsScrollActive => ScrollDebounceHelper.IsScrollActive;

    public static void NotifyScrollActivity() => ScrollDebounceHelper.NotifyScrollActivity();

    private DropShadow? _dropShadow;
    private SpriteVisual? _shadowVisual;
    private ExpressionAnimation? _bindSizeAnimation;
    private CompositionClip? _imageClip;

    public MediaCard()
    {
        InitializeComponent();
        this.PointerWheelChanged += (s, e) => NotifyScrollActivity();
        this.DataContextChanged += (s, e) =>
        {
            if (Item == null && e.NewValue is MediaItem dcItem)
            {
                OnItemChanged(dcItem);
            }
        };
        this.Loaded += (s, e) =>
        {
            ScrollDebounceHelper.ScrollActivityOccurred -= OnGlobalScrollActivity;
            ScrollDebounceHelper.ScrollActivityOccurred += OnGlobalScrollActivity;
            InitializeShadow();
            EnsureCardImageClip();
            UpdateDisplayImage();
            ApplyAccent(AccentColor);

            var effectiveItem = Item ?? (DataContext as MediaItem);
            if (effectiveItem != null)
            {
                if (_subscribedItem != effectiveItem)
                {
                    OnItemChanged(effectiveItem);
                }
                else
                {
                    IsSelected = effectiveItem.IsSelected;
                }
            }
        };
        this.SizeChanged += (s, e) => EnsureCardImageClip();
        this.Unloaded += (s, e) =>
        {
            ScrollDebounceHelper.ScrollActivityOccurred -= OnGlobalScrollActivity;
            CleanupShadow();
            if (_imageClip != null)
            {
                if (PosterImageElement != null)
                {
                    var visual = ElementCompositionPreview.GetElementVisual(PosterImageElement);
                    if (visual != null) visual.Clip = null;
                }
                try { _imageClip.Dispose(); } catch { }
                _imageClip = null;
            }
            if (PosterImageElement != null)
            {
                PosterImageElement.Source = null;
            }
            if (_subscribedItem != null)
            {
                _subscribedItem.PropertyChanged -= OnSubscribedItemPropertyChanged;
                _subscribedItem = null;
            }
            _previewDwellTimer?.Stop();
            var previewControl = App.MainWindowInstance?.VideoHoverPreviewControl;
            if (previewControl != null && ReferenceEquals(previewControl.ActiveSourceCard, this) && !previewControl.IsExpanded)
            {
                previewControl.ClosePreview();
            }
        };
    }

    private void EnsureCardImageClip()
    {
        try
        {
            if (PosterImageElement == null) return;
            var visual = ElementCompositionPreview.GetElementVisual(PosterImageElement);
            if (visual == null) return;
            var compositor = visual.Compositor;
            if (compositor == null) return;

            float w = (float)(ActualWidth > 0 ? ActualWidth : CardWidth);
            float h = (float)(CardHeight > 0 ? CardHeight : 168);
            if (w <= 0 || h <= 0) return;

            var oldClip = _imageClip;
            var clip = compositor.CreateGeometricClip();
            var geom = compositor.CreateRoundedRectangleGeometry();
            geom.Size = new System.Numerics.Vector2(w, h);
            geom.CornerRadius = new System.Numerics.Vector2(8f, 8f);
            clip.Geometry = geom;
            visual.Clip = clip;
            _imageClip = clip;
            try { oldClip?.Dispose(); } catch { }
        }
        catch { }
    }

    private void CleanupShadow()
    {
        try
        {
            if (ShadowHost != null)
            {
                ElementCompositionPreview.SetElementChildVisual(ShadowHost, null);
            }
            if (_bindSizeAnimation != null)
            {
                try { _bindSizeAnimation.Dispose(); } catch { }
                _bindSizeAnimation = null;
            }
            if (_dropShadow != null)
            {
                try { _dropShadow.Dispose(); } catch { }
                _dropShadow = null;
            }
            if (_shadowVisual != null)
            {
                try { _shadowVisual.Dispose(); } catch { }
                _shadowVisual = null;
            }
        }
        catch { }
    }

    private void InitializeShadow()
    {
        if (_dropShadow != null) return;
        try
        {
            if (ShadowHost == null || AlbumArtBackground == null) return;
            var hostVisual = ElementCompositionPreview.GetElementVisual(ShadowHost);
            var artVisual = ElementCompositionPreview.GetElementVisual(AlbumArtBackground);
            var compositor = hostVisual?.Compositor;
            if (compositor == null || artVisual == null) return;

            _shadowVisual = compositor.CreateSpriteVisual();
            _dropShadow = compositor.CreateDropShadow();
            _dropShadow.BlurRadius = 16f;
            _dropShadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            _dropShadow.Opacity = 0.0f; // Hidden initially
            _dropShadow.Offset = new System.Numerics.Vector3(0, 4, 0);

            _shadowVisual.Shadow = _dropShadow;

            // Keep size synchronized
            _bindSizeAnimation = compositor.CreateExpressionAnimation("artVisual.Size");
            _bindSizeAnimation.SetReferenceParameter("artVisual", artVisual);
            _shadowVisual.StartAnimation("Size", _bindSizeAnimation);

            ElementCompositionPreview.SetElementChildVisual(ShadowHost, _shadowVisual);
        }
        catch { }
    }

    private void AnimateShadow(double targetOpacity, float targetOffsetZ)
    {
        if (_dropShadow == null) return;
        try
        {
            var compositor = _dropShadow.Compositor;

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.InsertKeyFrame(1.0f, (float)targetOpacity);
            opacityAnim.Duration = TimeSpan.FromMilliseconds(200);
            _dropShadow.StartAnimation("Opacity", opacityAnim);

            var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
            offsetAnim.InsertKeyFrame(1.0f, new System.Numerics.Vector3(0, targetOffsetZ / 2, targetOffsetZ));
            offsetAnim.Duration = TimeSpan.FromMilliseconds(200);
            _dropShadow.StartAnimation("Offset", offsetAnim);
        }
        catch { }
    }

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CardHeight
    {
        get => (double)GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string AccentColor
    {
        get => (string)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public ImageSource Artwork
    {
        get => (ImageSource)GetValue(ArtworkProperty);
        set => SetValue(ArtworkProperty, value);
    }

    public string PosterUrl
    {
        get => (string)GetValue(PosterUrlProperty);
        set => SetValue(PosterUrlProperty, value);
    }

    private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaCard card)
        {
            card.ApplyAccent(e.NewValue as string);
        }
    }

    private void ApplyAccent(string? hex)
    {
        if (AlbumArtBackground != null)
        {
            try
            {
                var color = !string.IsNullOrWhiteSpace(hex)
                    ? ColorHelper.FromHex(hex)
                    : ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor);
                AlbumArtBackground.Background = new SolidColorBrush(color);
            }
            catch
            {
                try
                {
                    AlbumArtBackground.Background = new SolidColorBrush(ThemeHelper.GetAccentColor(AppServices.Settings.Current.AccentColor));
                }
                catch { }
            }
        }
    }

    private bool _isHovered;
    private bool _isDwellSuppressedUntilExit;
    private DispatcherTimer? _previewDwellTimer;

    public void SuppressDwellUntilExit()
    {
        _isDwellSuppressedUntilExit = true;
        _previewDwellTimer?.Stop();
    }

    private void OnGlobalScrollActivity()
    {
        if (DispatcherQueue?.HasThreadAccess == true)
        {
            _previewDwellTimer?.Stop();
            _isDwellSuppressedUntilExit = true;
        }
        else
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                _previewDwellTimer?.Stop();
                _isDwellSuppressedUntilExit = true;
            });
        }
    }

    private void OnTitlePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Suppress preview dwell when pointer enters the title area - preview should only trigger from thumbnail
        _previewDwellTimer?.Stop();
        _isDwellSuppressedUntilExit = true;
    }

    private void OnTitlePointerExited(object sender, PointerRoutedEventArgs e)
    {
    }

    private void TryStartPreviewDwell()
    {
        if (IsScrollActive) return;
        var mediaItem = GetAssociatedMediaItem();
        if (_isDwellSuppressedUntilExit || mediaItem?.IsVideo != true || !AppServices.Settings.Current.EnableHoverVideoPreview) return;
        if (AppServices.PlaybackViewModel.IsVideoPlayerActive) return;

        var previewControl = App.MainWindowInstance?.VideoHoverPreviewControl;
        if (previewControl?.IsExpanded == true) return;

        var mainWindow = App.MainWindowInstance;
        bool isFullscreen = mainWindow?.AppWindow?.Presenter?.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen;
        if (!isFullscreen)
        {
            int dwellMs = (previewControl != null && previewControl.IsPreviewActive) ? 180 : 320;
            if (_previewDwellTimer == null)
            {
                _previewDwellTimer = new DispatcherTimer();
                _previewDwellTimer.Tick += OnPreviewDwellTimerTick;
            }
            _previewDwellTimer.Interval = TimeSpan.FromMilliseconds(dwellMs);
            _previewDwellTimer.Stop();
            _previewDwellTimer.Start();
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (IsScrollActive)
            {
                _isDwellSuppressedUntilExit = true;
                return;
            }

            _isHovered = true;

            var previewControl = App.MainWindowInstance?.VideoHoverPreviewControl;
            if (previewControl?.IsExpanded == true)
            {
                // In expanded TV show modal mode, ignore background card hover
                return;
            }

            AnimateScale(1.03);
            AnimateShadow(0.55, 12f);

            if (AppServices.PlaybackViewModel.IsVideoPlayerActive) return;

            if (previewControl != null && ReferenceEquals(previewControl.ActiveSourceCard, this))
            {
                previewControl.OnSourceCardPointerEntered(this);
            }

            // Do not start preview dwell if pointer entered directly within the title container
            var point = e.GetCurrentPoint(this).Position;
            if (point.Y >= (CardHeight > 0 ? CardHeight : 168))
            {
                _isDwellSuppressedUntilExit = true;
                return;
            }

            TryStartPreviewDwell();
        }
        catch { }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            _isHovered = false;
            _isDwellSuppressedUntilExit = false;
            _previewDwellTimer?.Stop();

            var previewControl = App.MainWindowInstance?.VideoHoverPreviewControl;
            if (previewControl?.IsExpanded == true)
            {
                return;
            }

            if (previewControl != null && ReferenceEquals(previewControl.ActiveSourceCard, this))
            {
                previewControl.OnSourceCardPointerExited(this);
            }

            AnimateScale(1.0);
            AnimateShadow(0.0, 4f);
        }
        catch { }
    }

    private void OnPreviewDwellTimerTick(object? sender, object e)
    {
        _previewDwellTimer?.Stop();
        if (_isDwellSuppressedUntilExit || IsScrollActive) return;
        if (AppServices.PlaybackViewModel.IsVideoPlayerActive) return;
        var previewControl = App.MainWindowInstance?.VideoHoverPreviewControl;
        if (previewControl?.IsExpanded == true) return;
        var mediaItem = GetAssociatedMediaItem();
        if (_isHovered && mediaItem?.IsVideo == true && AppServices.Settings.Current.EnableHoverVideoPreview)
        {
            previewControl?.ShowPreview(this, mediaItem);
        }
    }

    private SpringVector3NaturalMotionAnimation? _springAnimation;

    private void AnimateScale(double targetScale)
    {
        if (AlbumArtBackground == null) return;
        try
        {
            var artVisual = ElementCompositionPreview.GetElementVisual(AlbumArtBackground);
            var selectionVisual = SelectionBorder != null ? ElementCompositionPreview.GetElementVisual(SelectionBorder) : null;
            if (artVisual == null) return;

            var centerPoint = new System.Numerics.Vector3(
                (float)(AlbumArtBackground.ActualWidth / 2),
                (float)(AlbumArtBackground.ActualHeight / 2),
                0);
            artVisual.CenterPoint = centerPoint;
            if (selectionVisual != null)
            {
                selectionVisual.CenterPoint = centerPoint;
            }

            var compositor = artVisual.Compositor;
            if (compositor == null) return;

            if (_springAnimation == null)
            {
                _springAnimation = compositor.CreateSpringVector3Animation();
                _springAnimation.Target = "Scale";
                _springAnimation.DampingRatio = 0.65f;
                _springAnimation.Period = TimeSpan.FromMilliseconds(160);
            }

            _springAnimation.FinalValue = new System.Numerics.Vector3((float)targetScale);

            artVisual.StartAnimation("Scale", _springAnimation);
            selectionVisual?.StartAnimation("Scale", _springAnimation);
        }
        catch { }
    }
}
