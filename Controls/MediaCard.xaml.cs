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
            new PropertyMetadata("#0078D4", OnAccentColorChanged));

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

    private void OnItemChanged(MediaItem? newItem)
    {
        if (newItem != null)
        {
            IsSelected = newItem.IsSelected;
        }
    }

    private void OnIsSelectedChanged(bool isSelected)
    {
        if (SelectionBorder != null)
        {
            SelectionBorder.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }
        if (SelectionHost != null)
        {
            SelectionHost.Opacity = isSelected || _isHovered ? 1.0 : 0.0;
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

    private void OnCardCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        if (CardCheckBox != null)
        {
            IsSelected = CardCheckBox.IsChecked == true;
        }
    }

    private MediaItem? GetAssociatedMediaItem()
    {
        if (Item != null) return Item;
        if (DataContext is MediaItem ctx) return ctx;
        return null;
    }

    private void OnMoreOptionsClick(object sender, RoutedEventArgs e)
    {
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

        var flyout = MediaFlyoutHelper.CreateMediaFlyout(mediaItem, MoreOptionsButton);
        flyout.ShowAt(MoreOptionsButton);
    }

    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var mediaItem = GetAssociatedMediaItem();
        if (mediaItem != null)
        {
            var flyout = MediaFlyoutHelper.CreateMediaFlyout(mediaItem, this);
            flyout.ShowAt(this, e.GetPosition(this));
            e.Handled = true;
        }
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

    private DropShadow? _dropShadow;

    public MediaCard()
    {
        InitializeComponent();
        this.Loaded += (s, e) =>
        {
            InitializeShadow();
            UpdateDisplayImage();
            ApplyAccent(AccentColor);
        };
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
            
            var shadowVisual = compositor.CreateSpriteVisual();
            _dropShadow = compositor.CreateDropShadow();
            _dropShadow.BlurRadius = 16f;
            _dropShadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            _dropShadow.Opacity = 0.0f; // Hidden initially
            _dropShadow.Offset = new System.Numerics.Vector3(0, 4, 0);
            
            shadowVisual.Shadow = _dropShadow;
            
            // Keep size synchronized
            var bindSizeAnimation = compositor.CreateExpressionAnimation("artVisual.Size");
            bindSizeAnimation.SetReferenceParameter("artVisual", artVisual);
            shadowVisual.StartAnimation("Size", bindSizeAnimation);
            
            ElementCompositionPreview.SetElementChildVisual(ShadowHost, shadowVisual);
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
        if (d is MediaCard card && e.NewValue is string hex)
        {
            card.ApplyAccent(hex);
        }
    }

    private void ApplyAccent(string hex)
    {
        if (AlbumArtBackground != null && !string.IsNullOrEmpty(hex))
        {
            try
            {
                AlbumArtBackground.Background = new SolidColorBrush(ColorHelper.FromHex(hex));
            }
            catch { }
        }
    }

    private bool _isHovered;

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            _isHovered = true;
            AnimateOverlay(1.0);
            AnimateScale(1.03);
            AnimateShadow(0.55, 12f);
            if (SelectionHost != null) SelectionHost.Opacity = 1.0;
            if (MoreOptionsButton != null) MoreOptionsButton.Opacity = 1.0;
        }
        catch { }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            _isHovered = false;
            AnimateOverlay(0.0);
            AnimateScale(1.0);
            AnimateShadow(0.0, 4f);
            if (SelectionHost != null && !IsSelected) SelectionHost.Opacity = 0.0;
            if (MoreOptionsButton != null) MoreOptionsButton.Opacity = 0.0;
        }
        catch { }
    }

    private void AnimateOverlay(double targetOpacity)
    {
        if (PlayOverlay == null) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PlayOverlay);
            if (visual == null) return;
            var compositor = visual.Compositor;
            if (compositor == null) return;

            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(200);
            var easing = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f),
                new System.Numerics.Vector2(0.2f, 1.0f));
            animation.InsertKeyFrame(1.0f, (float)targetOpacity, easing);
            visual.StartAnimation("Opacity", animation);
        }
        catch { }
    }

    private SpringVector3NaturalMotionAnimation? _springAnimation;

    private void AnimateScale(double targetScale)
    {
        if (AlbumArtBackground == null || PlayOverlay == null) return;
        try
        {
            var artVisual = ElementCompositionPreview.GetElementVisual(AlbumArtBackground);
            var overlayVisual = ElementCompositionPreview.GetElementVisual(PlayOverlay);
            var selectionVisual = SelectionBorder != null ? ElementCompositionPreview.GetElementVisual(SelectionBorder) : null;
            if (artVisual == null || overlayVisual == null) return;

            var centerPoint = new System.Numerics.Vector3(
                (float)(AlbumArtBackground.ActualWidth / 2),
                (float)(AlbumArtBackground.ActualHeight / 2),
                0);
            artVisual.CenterPoint = centerPoint;
            overlayVisual.CenterPoint = centerPoint;
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
            overlayVisual.StartAnimation("Scale", _springAnimation);
            selectionVisual?.StartAnimation("Scale", _springAnimation);
        }
        catch { }
    }
}
