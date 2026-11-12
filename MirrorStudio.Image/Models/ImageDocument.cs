using System.Collections.ObjectModel;
using SkiaSharp;

namespace MirrorStudio.Image.Models;

/// <summary>Document model for the professional image editor.</summary>
public sealed class ImageDocument
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double Dpi { get; set; } = 96.0;
    public string BackgroundColor { get; set; } = "#00000000";
    public ObservableCollection<ImageLayer> Layers { get; set; } = new();
    public ImageSelection? Selection { get; set; }

    public ImageLayer? TopLayer => Layers.Count > 0 ? Layers[0] : null;
    public ImageLayer? ActiveLayer { get; set; }

    public void AddLayer(ImageLayer layer, bool makeActive = true)
    {
        Layers.Insert(0, layer);
        if (makeActive) ActiveLayer = layer;
    }

    public bool RemoveLayer(Guid id)
    {
        var layer = Layers.FirstOrDefault(l => l.Id == id);
        if (layer == null || Layers.Count <= 1) return false;
        Layers.Remove(layer);
        if (ReferenceEquals(ActiveLayer, layer)) ActiveLayer = TopLayer;
        layer.Bitmap?.Dispose();
        return true;
    }

    public void MoveLayer(Guid id, int delta)
    {
        int idx = -1;
        for (int i = 0; i < Layers.Count; i++)
            if (Layers[i].Id == id) { idx = i; break; }
        if (idx < 0) return;
        int target = Math.Clamp(idx + delta, 0, Layers.Count - 1);
        if (target == idx) return;
        var item = Layers[idx];
        Layers.RemoveAt(idx);
        Layers.Insert(target, item);
    }

    public ImageDocument Clone()
    {
        var doc = new ImageDocument
        {
            Width = Width,
            Height = Height,
            Dpi = Dpi,
            BackgroundColor = BackgroundColor,
            Selection = Selection?.Clone()
        };
        foreach (var l in Layers)
        {
            var c = l.CloneMeta();
            if (l.Bitmap != null)
            {
                c.Bitmap = l.Bitmap.Copy();
                c.Mask = l.Mask?.Clone();
            }
            doc.Layers.Add(c);
            if (ReferenceEquals(l, ActiveLayer)) doc.ActiveLayer = c;
        }
        return doc;
    }

    // ---- document-level transforms ----

    public void Resize(int w, int h) => Resize(w, h, true);

    public void Resize(int w, int h, bool resample)
    {
        if (resample)
        {
            foreach (var l in Layers)
                if (l.Bitmap != null) l.Bitmap = Resample(l.Bitmap, w, h);
        }
        Width = w;
        Height = h;
    }

    public void Rotate(int degrees)
    {
        foreach (var l in Layers)
            if (l.Bitmap != null) l.Bitmap = RotateBitmap(l.Bitmap, degrees);
        if (degrees is 90 or 270 or -90 or -270) (Width, Height) = (Height, Width);
    }

    public void Flip(bool horizontal)
    {
        foreach (var l in Layers)
            if (l.Bitmap != null) l.Bitmap = FlipBitmap(l.Bitmap, horizontal);
    }

    public void Crop(System.Drawing.Rectangle rect)
    {
        int x = Math.Clamp(rect.X, 0, Width);
        int y = Math.Clamp(rect.Y, 0, Height);
        int w = Math.Clamp(rect.Width, 1, Width - x);
        int h = Math.Clamp(rect.Height, 1, Height - y);
        foreach (var l in Layers)
        {
            if (l.Bitmap == null) continue;
            l.Bitmap = CropBitmap(l.Bitmap, x, y, w, h);
            l.PositionX -= x;
            l.PositionY -= y;
        }
        Width = w;
        Height = h;
        Selection = null;
    }

    // ---- bitmap helpers (all produce Rgba8888 premultiplied bitmaps) ----

    public static SKBitmap MakeBitmap(int w, int h) =>
        new(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static SKBitmap Resample(SKBitmap src, int w, int h)
    {
        var outB = MakeBitmap(w, h);
        using var surface = SKSurface.Create(outB.Info, outB.GetPixels());
        var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(src, new SKRect(0, 0, w, h));
        surface.Flush();
        return outB;
    }

    private static SKBitmap RotateBitmap(SKBitmap src, int degrees)
    {
        var rotated = degrees is 90 or 270 or -90 or -270
            ? MakeBitmap(src.Height, src.Width)
            : MakeBitmap(src.Width, src.Height);
        using var surface = SKSurface.Create(rotated.Info, rotated.GetPixels());
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-src.Width / 2f, -src.Height / 2f);
        canvas.DrawBitmap(src, 0, 0);
        surface.Flush();
        return rotated;
    }

    private static SKBitmap FlipBitmap(SKBitmap src, bool horizontal)
    {
        var outB = MakeBitmap(src.Width, src.Height);
        using var surface = SKSurface.Create(outB.Info, outB.GetPixels());
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(horizontal ? src.Width : 0, horizontal ? 0 : src.Height);
        canvas.Scale(horizontal ? -1 : 1, horizontal ? 1 : -1);
        canvas.DrawBitmap(src, 0, 0);
        surface.Flush();
        return outB;
    }

    private static SKBitmap CropBitmap(SKBitmap src, int x, int y, int w, int h)
    {
        var outB = MakeBitmap(w, h);
        using var surface = SKSurface.Create(outB.Info, outB.GetPixels());
        surface.Canvas.Clear(SKColors.Transparent);
        var sub = new SKBitmap();
        if (sub.ExtractSubset(src, new SKRectI(x, y, x + w, y + h)))
        {
            surface.Canvas.DrawBitmap(sub, 0, 0);
            sub.Dispose();
        }
        surface.Flush();
        return outB;
    }
}
