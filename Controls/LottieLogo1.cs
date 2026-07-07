using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;

namespace LumiereMediaPlayer.Controls;

public sealed class LottieLogo1 : IAnimatedVisualSource
{
    public IAnimatedVisual TryCreateAnimatedVisual(Compositor compositor, out object? diagnostics)
    {
        diagnostics = null;
        return new LottieLogo1Visual(compositor);
    }
}

public sealed class LottieLogo1Visual : IAnimatedVisual
{
    private readonly ContainerVisual _rootVisual;
    private readonly ShapeVisual _shapeVisual;
    private readonly CompositionSpriteShape _ringShape;

    public LottieLogo1Visual(Compositor compositor)
    {
        _rootVisual = compositor.CreateContainerVisual();
        _rootVisual.Size = new Vector2(44, 44);

        // Add Progress property to RootVisual.Properties so it can be animated by AnimatedVisualPlayer
        _rootVisual.Properties.InsertScalar("Progress", 0.0f);

        // Create a ShapeVisual to hold our shapes
        _shapeVisual = compositor.CreateShapeVisual();
        _shapeVisual.Size = new Vector2(44, 44);

        // Outer rotating ring (centered at 22,22 with radius 20 to sit exactly inside the 44x44 area)
        var ringGeometry = compositor.CreateEllipseGeometry();
        ringGeometry.Center = new Vector2(22, 22);
        ringGeometry.Radius = new Vector2(20, 20);

        _ringShape = compositor.CreateSpriteShape(ringGeometry);
        _ringShape.StrokeBrush = compositor.CreateColorBrush(Microsoft.UI.Colors.White);
        _ringShape.StrokeThickness = 1.2f;
        _ringShape.CenterPoint = new Vector2(22, 22);

        // Use ExpressionAnimation to drive the rotation of the ring shape from the Progress property
        var rotationAnimation = compositor.CreateExpressionAnimation("myRoot.Progress * 360.0");
        rotationAnimation.SetReferenceParameter("myRoot", _rootVisual);

        _ringShape.StartAnimation("RotationAngleInDegrees", rotationAnimation);

        _shapeVisual.Shapes.Add(_ringShape);

        _rootVisual.Children.InsertAtTop(_shapeVisual);
    }

    public Visual RootVisual => _rootVisual;

    public TimeSpan Duration => TimeSpan.FromSeconds(2.5);

    public Vector2 Size => new Vector2(44, 44);

    public void Dispose()
    {
        _ringShape.Dispose();
        _shapeVisual.Dispose();
        _rootVisual.Dispose();
    }
}
