using MirrorStudio.Graphics.Models;

namespace MirrorStudio.Image.Models;

/// <summary>Kind of content an image layer holds.</summary>
public enum ImageLayerKind
{
    Raster,
    Image,
    Adjustment,
    Text,
    Shape
}

/// <summary>
/// A single layer in an <see cref="ImageDocument"/>. Layers are stored
/// top-to-bottom (index 0 is the topmost layer); the compositor renders
/// from the last index upward.
/// </summary>
public sealed class ImageLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer";
    public ImageLayerKind Kind { get; set; } = ImageLayerKind.Raster;

    public bool IsVisible { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public string BlendMode { get; set; } = "Normal";

    // transform applied to the layer's own bitmap before compositing
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double ScaleX { get; set; } = 1.0;
    public double ScaleY { get; set; } = 1.0;
    public double Rotation { get; set; }

    // raster / image content
    public SkiaSharp.SKBitmap? Bitmap { get; set; }
    public string? SourcePath { get; set; }

    // adjustment content (non-destructive tone/colour operations)
    public System.Collections.Generic.List<Adjustment> Adjustments { get; set; } = new();

    // text content (reuses the Milestone 11 text engine types)
    public string? TextContent { get; set; }
    public TextStyle? TextStyle { get; set; }

    // shape content
    public string ShapeType { get; set; } = "Rectangle";
    public string ShapeFill { get; set; } = "#FFFFFFFF";
    public string ShapeStroke { get; set; } = "#FF000000";
    public double ShapeStrokeWidth { get; set; }
    public double ShapeWidth { get; set; } = 200;
    public double ShapeHeight { get; set; } = 120;

    // optional per-layer mask (alpha multiplier, 0..255 per pixel)
    public ImageMask? Mask { get; set; }

    public ImageLayer CloneMeta()
    {
        return new ImageLayer
        {
            Id = Guid.NewGuid(),
            Name = Name + " copy",
            Kind = Kind,
            IsVisible = IsVisible,
            Opacity = Opacity,
            BlendMode = BlendMode,
            PositionX = PositionX,
            PositionY = PositionY,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
            Rotation = Rotation,
            SourcePath = SourcePath,
            Adjustments = new System.Collections.Generic.List<Adjustment>(Adjustments),
            TextContent = TextContent,
            TextStyle = TextStyle,
            ShapeType = ShapeType,
            ShapeFill = ShapeFill,
            ShapeStroke = ShapeStroke,
            ShapeStrokeWidth = ShapeStrokeWidth,
            ShapeWidth = ShapeWidth,
            ShapeHeight = ShapeHeight
        };
    }
}
