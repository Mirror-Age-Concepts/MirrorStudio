using SkiaSharp;
using System.Drawing;
using MirrorStudio.Image.Brushes;
using MirrorStudio.Image.Models;

namespace MirrorStudio.Image.Tools;

/// <summary>A pointer-driven image editing tool.</summary>
public interface IImageTool
{
    string Name { get; }
    void OnDown(ImageEditorContext ctx, double ix, double iy);
    void OnMove(ImageEditorContext ctx, double ix, double iy, bool down);
    void OnUp(ImageEditorContext ctx, double ix, double iy);
}

public static class ToolFactory
{
    public static IImageTool Create(string name) => name switch
    {
        "brush" => new BrushTool(false),
        "eraser" => new BrushTool(true),
        "clone" => new CloneTool(),
        "heal" => new HealingTool(),
        "move" => new MoveTool(),
        "crop" => new CropTool(),
        "marquee" => new RectSelectTool(),
        "ellipse" => new EllipseSelectTool(),
        "lasso" => new LassoTool(),
        "text" => new TextTool(),
        "rect" or "shape" => new ShapeTool(),
        "zoom" => new ZoomTool(),
        _ => new MoveTool()
    };
}

internal sealed class MoveTool : IImageTool
{
    public string Name => "Move";
    private (double x, double y)? _start;
    private (double x, double y) _orig;

    public void OnDown(ImageEditorContext ctx, double ix, double iy)
    {
        var l = ctx.Document.ActiveLayer;
        if (l == null) return;
        _start = (ix, iy);
        _orig = (l.PositionX, l.PositionY);
    }
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down)
    {
        if (!down || _start == null) return;
        var l = ctx.Document.ActiveLayer;
        if (l == null) return;
        l.PositionX = _orig.x + (ix - _start.Value.x);
        l.PositionY = _orig.y + (iy - _start.Value.y);
    }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) => _start = null;
}

internal sealed class RectSelectTool : IImageTool
{
    public string Name => "RectSelect";
    private (double x, double y) _anchor;
    public void OnDown(ImageEditorContext ctx, double ix, double iy) => _anchor = (ix, iy);
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down)
    {
        if (!down) return;
        ctx.Document.Selection = new ImageSelection
        {
            Mode = ImageSelection.SelectionMode.Rectangle,
            Bounds = ToolGeometry.Normalize(_anchor, (ix, iy))
        };
    }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) { }
}

internal sealed class EllipseSelectTool : IImageTool
{
    public string Name => "EllipseSelect";
    private (double x, double y) _anchor;
    public void OnDown(ImageEditorContext ctx, double ix, double iy) => _anchor = (ix, iy);
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down)
    {
        if (!down) return;
        ctx.Document.Selection = new ImageSelection
        {
            Mode = ImageSelection.SelectionMode.Ellipse,
            Bounds = ToolGeometry.Normalize(_anchor, (ix, iy))
        };
    }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) { }
}

internal sealed class LassoTool : IImageTool
{
    public string Name => "Lasso";
    private readonly List<Point> _pts = new();
    public void OnDown(ImageEditorContext ctx, double ix, double iy)
    {
        _pts.Clear();
        _pts.Add(new Point((int)ix, (int)iy));
    }
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down)
    {
        if (!down) return;
        _pts.Add(new Point((int)ix, (int)iy));
        ctx.Document.Selection = new ImageSelection
        {
            Mode = ImageSelection.SelectionMode.Polygon,
            Points = new List<Point>(_pts)
        };
    }
    public void OnUp(ImageEditorContext ctx, double ix, double iy)
    {
        if (_pts.Count >= 3)
            ctx.Document.Selection = new ImageSelection
            {
                Mode = ImageSelection.SelectionMode.Polygon,
                Points = new List<Point>(_pts)
            };
    }
}

internal sealed class CropTool : IImageTool
{
    public string Name => "Crop";
    private (double x, double y) _anchor;
    public void OnDown(ImageEditorContext ctx, double ix, double iy) => _anchor = (ix, iy);
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down)
    {
        if (!down) return;
        ctx.Document.Selection = new ImageSelection
        {
            Mode = ImageSelection.SelectionMode.Rectangle,
            Bounds = ToolGeometry.Normalize(_anchor, (ix, iy))
        };
    }
    public void OnUp(ImageEditorContext ctx, double ix, double iy)
    {
        var sel = ctx.Document.Selection;
        if (sel != null && sel.Bounds.Width > 4 && sel.Bounds.Height > 4)
            ctx.Document.Crop(sel.Bounds);
    }
}

internal sealed class BrushTool : IImageTool
{
    private readonly bool _erase;
    public BrushTool(bool erase) => _erase = erase;
    public string Name => _erase ? "Eraser" : "Brush";

    public void OnDown(ImageEditorContext ctx, double ix, double iy)
    {
        var bmp = ctx.ActiveBitmap();
        if (bmp == null) return;
        var (lx, ly) = ctx.ToLayerLocal(ix, iy);
        BrushEngine.Stamp(bmp, ctx.Brush, lx, ly, ctx.Foreground, _erase, ctx.ActiveMask());
        ctx.LastPoint = (ix, iy);
    }
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down)
    {
        if (!down) return;
        var bmp = ctx.ActiveBitmap();
        if (bmp == null) return;
        var (llx, lly) = ctx.ToLayerLocal(ix, iy);
        var (plx, ply) = ctx.ToLayerLocal(ctx.LastPoint?.x ?? ix, ctx.LastPoint?.y ?? iy);
        var (nx, ny) = BrushEngine.Stroke(bmp, ctx.Brush, plx, ply, llx, lly, ctx.Foreground, _erase, ctx.ActiveMask());
        ctx.LastPoint = (ix, iy);
    }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) => ctx.LastPoint = null;
}

internal sealed class CloneTool : IImageTool
{
    public string Name => "Clone";
    public void OnDown(ImageEditorContext ctx, double ix, double iy)
    {
        if (ctx.CloneSource == null)
        {
            ctx.CloneSource = (ix, iy); // first click sets the sample point
            return;
        }
        Stamp(ctx, ix, iy);
        ctx.LastPoint = (ix, iy);
    }
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down)
    {
        if (!down || ctx.CloneSource == null) return;
        Stamp(ctx, ix, iy);
        ctx.LastPoint = (ix, iy);
    }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) { }

    private static void Stamp(ImageEditorContext ctx, double ix, double iy)
    {
        var target = ctx.ActiveBitmap();
        var srcLayer = ctx.Document.ActiveLayer;
        if (target == null || srcLayer?.Bitmap == null || ctx.CloneSource == null) return;
        var (lx, ly) = ctx.ToLayerLocal(ix, iy);
        var (sx, sy) = ctx.ToLayerLocal(ctx.CloneSource.Value.x, ctx.CloneSource.Value.y);
        // source sample point moves with the brush so it "paints" from the image
        BrushEngine.StampClone(target, srcLayer.Bitmap, lx, ly, sx, sy, ctx.Brush, 1.0, ctx.ActiveMask());
    }
}

internal sealed class HealingTool : IImageTool
{
    public string Name => "Heal";
    public void OnDown(ImageEditorContext ctx, double ix, double iy)
    {
        if (ctx.CloneSource == null) { ctx.CloneSource = (ix, iy); return; }
        Stamp(ctx, ix, iy);
    }
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down)
    {
        if (!down || ctx.CloneSource == null) return;
        Stamp(ctx, ix, iy);
    }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) { }
    private static void Stamp(ImageEditorContext ctx, double ix, double iy)
    {
        var target = ctx.ActiveBitmap();
        var srcLayer = ctx.Document.ActiveLayer;
        if (target == null || srcLayer?.Bitmap == null || ctx.CloneSource == null) return;
        var (lx, ly) = ctx.ToLayerLocal(ix, iy);
        var (sx, sy) = ctx.ToLayerLocal(ctx.CloneSource.Value.x, ctx.CloneSource.Value.y);
        BrushEngine.StampClone(target, srcLayer.Bitmap, lx, ly, sx, sy, ctx.Brush, 0.7, ctx.ActiveMask());
    }
}

internal sealed class TextTool : IImageTool
{
    public string Name => "Text";
    public void OnDown(ImageEditorContext ctx, double ix, double iy)
    {
        var l = ctx.Document.ActiveLayer;
        if (l != null && l.Kind == ImageLayerKind.Text)
        {
            l.PositionX = ix; l.PositionY = iy;
            return;
        }
        var layer = new ImageLayer
        {
            Name = "Text",
            Kind = ImageLayerKind.Text,
            PositionX = ix,
            PositionY = iy,
            TextContent = "Text",
            TextStyle = new MirrorStudio.Graphics.Models.TextStyle
            {
                FontFamily = "Arial",
                FontSize = 48,
                Bold = false,
                Italic = false,
                Alignment = "Center",
                FillColor = "#FFFFFFFF",
                StrokeColor = "#FF000000",
                StrokeWidth = 0
            }
        };
        ctx.Document.AddLayer(layer);
    }
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down) { }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) { }
}

internal sealed class ShapeTool : IImageTool
{
    public string Name => "Shape";
    public void OnDown(ImageEditorContext ctx, double ix, double iy)
    {
        var l = ctx.Document.ActiveLayer;
        if (l != null && l.Kind == ImageLayerKind.Shape)
        {
            l.ShapeWidth = Math.Max(8, ix - l.PositionX);
            l.ShapeHeight = Math.Max(8, iy - l.PositionY);
            return;
        }
        var layer = new ImageLayer
        {
            Name = "Shape",
            Kind = ImageLayerKind.Shape,
            PositionX = ix,
            PositionY = iy,
            ShapeWidth = 160,
            ShapeHeight = 100,
            ShapeType = "Rectangle",
            ShapeFill = "#FF4FA3FF",
            ShapeStroke = "#FF000000",
            ShapeStrokeWidth = 0
        };
        ctx.Document.AddLayer(layer);
    }
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down) { }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) { }
}

internal sealed class ZoomTool : IImageTool
{
    public string Name => "Zoom";
    public void OnDown(ImageEditorContext ctx, double ix, double iy) { }
    public void OnMove(ImageEditorContext ctx, double ix, double iy, bool down) { }
    public void OnUp(ImageEditorContext ctx, double ix, double iy) { }
}

internal static class ToolGeometry
{
    public static Rectangle Normalize((double x, double y) a, (double x, double y) b)
    {
        int x = (int)Math.Min(a.x, b.x), y = (int)Math.Min(a.y, b.y);
        int w = (int)Math.Abs(a.x - b.x), h = (int)Math.Abs(a.y - b.y);
        return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
    }
}
