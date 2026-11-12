using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace LumiereMediaPlayer.Controls;

/// <summary>
/// A high-performance XAML text control that renders text with a crisp, high-visibility
/// 3D directional specular Fresnel border surrounding every glyph. Designed for optimal
/// legibility over transparent liquid glass surfaces with dynamic video playing beneath.
/// </summary>
public sealed class OutlinedTextBlock : UserControl
{
    private static readonly (double X, double Y)[] s_offsets = new[]
    {
        (-1.0, -1.0), // [0] Top-Left
        ( 0.0, -1.0), // [1] Top
        ( 1.0, -1.0), // [2] Top-Right
        (-1.0,  0.0), // [3] Left
        ( 1.0,  0.0), // [4] Right
        (-1.0,  1.0), // [5] Bottom-Left
        ( 0.0,  1.0), // [6] Bottom
        ( 1.0,  1.0)  // [7] Bottom-Right
    };

    // 3D Carved Crystal directional specular Fresnel & refractive occlusion brushes
    private static readonly Brush[] s_specularBrushes = new Brush[]
    {
        new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)), // [0] (-1, -1) Top-Left: Direct 45° Specular Fresnel Glint
        new SolidColorBrush(Color.FromArgb(0xD5, 0xFF, 0xFF, 0xFF)), // [1] ( 0, -1) Top: Bright Specular Highlight
        new SolidColorBrush(Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF)), // [2] ( 1, -1) Top-Right: Translucent Specular Sheen
        new SolidColorBrush(Color.FromArgb(0xD5, 0xFF, 0xFF, 0xFF)), // [3] (-1,  0) Left: Bright Specular Edge
        new SolidColorBrush(Color.FromArgb(0x85, 0x00, 0x00, 0x00)), // [4] ( 1,  0) Right: Ambient Refractive Shadow
        new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)), // [5] (-1,  1) Bottom-Left: Subtle Refractive Edge
        new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x00, 0x00)), // [6] ( 0,  1) Bottom: Ambient Occlusion Shadow
        new SolidColorBrush(Color.FromArgb(0xB8, 0x00, 0x00, 0x00))  // [7] ( 1,  1) Bottom-Right: Deepest Core Shadow
    };

    private static readonly SolidColorBrush s_defaultFillBrush = new(Colors.White);
    private static readonly SolidColorBrush s_defaultStrokeBrush = new(Colors.Black);

    private readonly Grid _rootGrid;
    private readonly TextBlock[] _outlineTextBlocks;
    private readonly TranslateTransform[] _outlineTransforms;
    private readonly TextBlock _foregroundTextBlock;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(OutlinedTextBlock),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(OutlinedTextBlock),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedTextBlock),
            new PropertyMetadata(1.2, OnVisualPropertyChanged));

    public static readonly DependencyProperty UseSpecularBorderProperty =
        DependencyProperty.Register(nameof(UseSpecularBorder), typeof(bool), typeof(OutlinedTextBlock),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(OutlinedTextBlock),
            new PropertyMetadata(TextWrapping.NoWrap, OnVisualPropertyChanged));

    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(OutlinedTextBlock),
            new PropertyMetadata(TextTrimming.None, OnVisualPropertyChanged));

    public static readonly DependencyProperty MaxLinesProperty =
        DependencyProperty.Register(nameof(MaxLines), typeof(int), typeof(OutlinedTextBlock),
            new PropertyMetadata(0, OnVisualPropertyChanged));

    public static readonly DependencyProperty LineHeightProperty =
        DependencyProperty.Register(nameof(LineHeight), typeof(double), typeof(OutlinedTextBlock),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(OutlinedTextBlock),
            new PropertyMetadata(TextAlignment.Left, OnVisualPropertyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool UseSpecularBorder
    {
        get => (bool)GetValue(UseSpecularBorderProperty);
        set => SetValue(UseSpecularBorderProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    public int MaxLines
    {
        get => (int)GetValue(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
    }

    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public OutlinedTextBlock()
    {
        // Default font size to standard 12pt for metadata display
        this.FontSize = 12.0;

        _rootGrid = new Grid();
        _outlineTextBlocks = new TextBlock[s_offsets.Length];
        _outlineTransforms = new TranslateTransform[s_offsets.Length];

        // 8 outline shadow text blocks (hidden from screen reader accessibility trees)
        for (int i = 0; i < s_offsets.Length; i++)
        {
            var transform = new TranslateTransform();
            _outlineTransforms[i] = transform;

            var tb = new TextBlock
            {
                IsHitTestVisible = false,
                RenderTransform = transform
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(tb, AccessibilityView.Raw);
            _outlineTextBlocks[i] = tb;
            _rootGrid.Children.Add(tb);
        }

        // 1 center foreground text block
        _foregroundTextBlock = new TextBlock();
        _rootGrid.Children.Add(_foregroundTextBlock);

        this.Content = _rootGrid;

        // Listen for inherited/explicit Control properties changes
        RegisterPropertyChangedCallback(ForegroundProperty, (s, _) => ((OutlinedTextBlock)s).UpdateChildren());
        RegisterPropertyChangedCallback(FontFamilyProperty, (s, _) => ((OutlinedTextBlock)s).UpdateChildren());
        RegisterPropertyChangedCallback(FontSizeProperty, (s, _) => ((OutlinedTextBlock)s).UpdateChildren());
        RegisterPropertyChangedCallback(FontWeightProperty, (s, _) => ((OutlinedTextBlock)s).UpdateChildren());

        // Apply initial defaults
        UpdateChildren();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OutlinedTextBlock otb)
        {
            otb.UpdateChildren();
        }
    }

    private void UpdateChildren()
    {
        string text = Text ?? string.Empty;
        double fontSize = FontSize > 0 ? FontSize : 12.0;
        FontWeight fontWeight = FontWeight;
        TextWrapping wrapping = TextWrapping;
        TextTrimming trimming = TextTrimming;
        int maxLines = MaxLines;
        double lineHeight = LineHeight;
        TextAlignment alignment = TextAlignment;
        FontFamily family = FontFamily;

        bool useSpecular = UseSpecularBorder;
        Brush fill = FillBrush ?? Foreground ?? s_defaultFillBrush;
        Brush customStroke = StrokeBrush;
        double thickness = StrokeThickness;
        bool showOutline = thickness > 0;

        for (int i = 0; i < s_offsets.Length; i++)
        {
            var tb = _outlineTextBlocks[i];
            tb.Text = text;
            tb.FontSize = fontSize;
            tb.FontWeight = fontWeight;
            tb.TextWrapping = wrapping;
            tb.TextTrimming = trimming;
            tb.TextAlignment = alignment;
            if (family != null) tb.FontFamily = family;
            if (maxLines > 0) tb.MaxLines = maxLines;
            if (lineHeight > 0) tb.LineHeight = lineHeight;

            // Apply 3D specular crystal border or custom stroke brush
            if (customStroke != null)
            {
                tb.Foreground = customStroke;
            }
            else if (useSpecular)
            {
                tb.Foreground = s_specularBrushes[i];
            }
            else
            {
                tb.Foreground = s_defaultStrokeBrush;
            }

            tb.Visibility = showOutline ? Visibility.Visible : Visibility.Collapsed;

            if (showOutline)
            {
                var transform = _outlineTransforms[i];
                transform.X = s_offsets[i].X * thickness;
                transform.Y = s_offsets[i].Y * thickness;
            }
        }

        _foregroundTextBlock.Text = text;
        _foregroundTextBlock.FontSize = fontSize;
        _foregroundTextBlock.FontWeight = fontWeight;
        _foregroundTextBlock.TextWrapping = wrapping;
        _foregroundTextBlock.TextTrimming = trimming;
        _foregroundTextBlock.TextAlignment = alignment;
        if (family != null) _foregroundTextBlock.FontFamily = family;
        if (maxLines > 0) _foregroundTextBlock.MaxLines = maxLines;
        if (lineHeight > 0) _foregroundTextBlock.LineHeight = lineHeight;
        _foregroundTextBlock.Foreground = fill;
    }
}
