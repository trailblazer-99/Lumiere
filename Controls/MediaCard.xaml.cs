using LumiereMediaPlayer.Helpers;
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
        DependencyProperty.Register(nameof(Artwork), typeof(ImageSource), typeof(MediaCard), new PropertyMetadata(null));

    public static readonly DependencyProperty PosterUrlProperty =
        DependencyProperty.Register(nameof(PosterUrl), typeof(string), typeof(MediaCard), new PropertyMetadata(null, OnPosterUrlChanged));

    private static void OnPosterUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaCard card)
        {
            var url = e.NewValue as string;
            if (!string.IsNullOrEmpty(url))
            {
                try { card.PosterImageElement.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri(url)); } catch { card.PosterImageElement.Source = null; }
            }
            else
            {
                card.PosterImageElement.Source = null;
            }
        }
    }

    private DropShadow? _dropShadow;

    public MediaCard()
    {
        InitializeComponent();
        this.Loaded += (s, e) => InitializeShadow();
    }

    private void InitializeShadow()
    {
        try
        {
            var hostVisual = ElementCompositionPreview.GetElementVisual(ShadowHost);
            var artVisual = ElementCompositionPreview.GetElementVisual(AlbumArtBackground);
            var compositor = hostVisual.Compositor;
            
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
        AlbumArtBackground.Background = new SolidColorBrush(ColorHelper.FromHex(hex));
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateOverlay(1.0);
        AnimateScale(1.03);
        AnimateShadow(0.55, 12f);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateOverlay(0.0);
        AnimateScale(1.0);
        AnimateShadow(0.0, 4f);
    }

    private void AnimateOverlay(double targetOpacity)
    {
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, PlayOverlay);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void AnimateScale(double targetScale)
    {
        var artVisual = ElementCompositionPreview.GetElementVisual(AlbumArtBackground);
        var overlayVisual = ElementCompositionPreview.GetElementVisual(PlayOverlay);

        artVisual.CenterPoint = new System.Numerics.Vector3(
            (float)(AlbumArtBackground.ActualWidth / 2),
            (float)(AlbumArtBackground.ActualHeight / 2),
            0);
        overlayVisual.CenterPoint = artVisual.CenterPoint;

        var compositor = artVisual.Compositor;
        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(1.0f, new System.Numerics.Vector3((float)targetScale));
        scaleAnimation.Duration = TimeSpan.FromMilliseconds(200);

        artVisual.StartAnimation("Scale", scaleAnimation);
        overlayVisual.StartAnimation("Scale", scaleAnimation);
    }
}
