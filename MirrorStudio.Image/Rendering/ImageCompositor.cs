using SkiaSharp;
using MirrorStudio.Image.Adjustments;
using MirrorStudio.Image.Models;

namespace MirrorStudio.Image.Rendering;

/// <summary>
/// Renders an <see cref="ImageDocument"/> to a flat <see cref="SKBitmap"/>,
/// honouring layer order, blend modes, opacity, transforms, masks and
/// non-destructive adjustment layers.
/// </summary>
public static class ImageCompositor
{
    public static SKBitmap Composite(ImageDocument doc)
    {
        var acc = ImageDocument.MakeBitmap(doc.Width, doc.Height);
        using (var s = SKSurface.Create(acc.Info, acc.GetPixels()))
        {
            var canvas = s.Canvas;
            DrawBackground(canvas, doc);
        }

        // bottom (last in list) -> top (first in list)
        for (int i = doc.Layers.Count - 1; i >= 0; i--)
        {
            var layer = doc.Layers[i];
            if (!layer.IsVisible) continue;

            if (layer.Kind == ImageLayerKind.Adjustment)
            {
                AdjustmentProcessor.ApplyAll(acc, layer.Adjustments);
                continue;
            }

            SKBitmap? src = GetLayerBitmap(doc, layer);
            if (src == null) continue;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                BlendMode = ParseBlend(layer.BlendMode),
                Color = SKColors.White.WithAlpha((byte)Math.Clamp(layer.Opacity * 255, 0, 255))
            };

            if (layer.Mask != null)
            {
                var temp = ImageDocument.MakeBitmap(doc.Width, doc.Height);
                using (var ts = SKSurface.Create(temp.Info, temp.GetPixels()))
                    DrawLayer(ts.Canvas, src, layer);
                MultiplyMask(temp, layer.Mask);
                using var s2 = SKSurface.Create(acc.Info, acc.GetPixels());
                s2.Canvas.DrawBitmap(temp, 0, 0, paint);
            }
            else
            {
                using var s2 = SKSurface.Create(acc.Info, acc.GetPixels());
                DrawLayer(s2.Canvas, src, layer);
            }
        }

        return acc;
    }

    private static void DrawLayer(SKCanvas canvas, SKBitmap src, ImageLayer layer)
    {
        int bw = src.Width, bh = src.Height;
        canvas.Save();
        canvas.Translate((float)(layer.PositionX + bw * layer.ScaleX / 2),
                          (float)(layer.PositionY + bh * layer.ScaleY / 2));
        canvas.RotateDegrees((float)layer.Rotation);
        canvas.Scale((float)layer.ScaleX, (float)layer.ScaleY);
        canvas.Translate(-bw / 2f, -bh / 2f);
        using var p = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(src, 0, 0, p);
        canvas.Restore();
    }

    private static SKBitmap? GetLayerBitmap(ImageDocument doc, ImageLayer layer)
    {
        if (layer.Kind is ImageLayerKind.Text)
            return RenderText(doc, layer);
        if (layer.Kind is ImageLayerKind.Shape)
            return RenderShape(doc, layer);
        return layer.Bitmap;
    }

    private static SKBitmap RenderText(ImageDocument doc, ImageLayer layer)
    {
        var bmp = ImageDocument.MakeBitmap(doc.Width, doc.Height);
        using var s = SKSurface.Create(bmp.Info, bmp.GetPixels());
        var canvas = s.Canvas;
        var style = layer.TextStyle;
        if (style == null) return bmp;
        var font = new SKFont(SKTypeface.FromFamilyName(style.FontFamily ?? "Arial",
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright),
            (float)style.FontSize);

        float x = (float)(layer.PositionX <= 0 ? doc.Width / 2.0 : layer.PositionX);
        float y = (float)(layer.PositionY <= 0 ? doc.Height / 2.0 : layer.PositionY);
        if (style.Alignment == "Center") { var m = new SKRect(); font.MeasureText(layer.TextContent ?? "", out m); x -= m.Width / 2; }
        else if (style.Alignment == "Right") { var m = new SKRect(); font.MeasureText(layer.TextContent ?? "", out m); x -= m.Width; }

        if (style.StrokeWidth > 0.5f)
        {
            using var sp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)style.StrokeWidth, Color = ParseColor(style.StrokeColor) };
            canvas.DrawText(layer.TextContent ?? "", x, y, font, sp);
        }
        using var fp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = ParseColor(style.FillColor) };
        canvas.DrawText(layer.TextContent ?? "", x, y, font, fp);
        return bmp;
    }

    private static SKBitmap RenderShape(ImageDocument doc, ImageLayer layer)
    {
        var bmp = ImageDocument.MakeBitmap(doc.Width, doc.Height);
        using var s = SKSurface.Create(bmp.Info, bmp.GetPixels());
        var canvas = s.Canvas;
        float x = (float)layer.PositionX, y = (float)layer.PositionY;
        float w = (float)layer.ShapeWidth, h = (float)layer.ShapeHeight;
        var fill = ParseColor(layer.ShapeFill);
        var stroke = ParseColor(layer.ShapeStroke);
        float sw = (float)layer.ShapeStrokeWidth;
        using var fp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = fill };
        using var sp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = stroke, StrokeWidth = sw };
        var rect = new SKRect(x, y, x + w, y + h);
        switch (layer.ShapeType)
        {
            case "Ellipse" or "Circle":
                if (sw > 0.5f) { canvas.DrawOval(rect, fp); canvas.DrawOval(rect, sp); }
                else canvas.DrawOval(rect, fp);
                break;
            case "Line" or "Arrow":
                using (var lp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = stroke, StrokeWidth = Math.Max(2, sw) })
                    canvas.DrawLine(x, y, x + w, y + h, lp);
                break;
            default:
                if (sw > 0.5f) { canvas.DrawRect(rect, fp); canvas.DrawRect(rect, sp); }
                else canvas.DrawRect(rect, fp);
                break;
        }
        return bmp;
    }

    private static void DrawBackground(SKCanvas canvas, ImageDocument doc)
    {
        var c = ParseColor(doc.BackgroundColor);
        if (c.Alpha > 0)
        {
            canvas.Clear(c);
        }
        else
        {
            // transparent checkerboard placeholder is a UI concern; keep transparent here
            canvas.Clear(SKColors.Transparent);
        }
    }

    private static void MultiplyMask(SKBitmap target, ImageMask mask)
    {
        int w = target.Width, h = target.Height;
        IntPtr p = target.GetPixels();
        if (p == IntPtr.Zero) return;
        unsafe
        {
            var s = new Span<byte>(p.ToPointer(), w * h * 4);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                double f = mask.GetAt(x, y) / 255.0;
                s[o] = (byte)(s[o] * f);
                s[o + 1] = (byte)(s[o + 1] * f);
                s[o + 2] = (byte)(s[o + 2] * f);
                s[o + 3] = (byte)(s[o + 3] * f);
            }
        }
    }

    public static SKBlendMode ParseBlend(string name) => name?.Trim() switch
    {
        "Normal" or "SrcOver" or "" or null => SKBlendMode.SrcOver,
        "Multiply" => SKBlendMode.Multiply,
        "Screen" => SKBlendMode.Screen,
        "Overlay" => SKBlendMode.Overlay,
        "Darken" => SKBlendMode.Darken,
        "Lighten" => SKBlendMode.Lighten,
        "ColorDodge" => SKBlendMode.ColorDodge,
        "ColorBurn" => SKBlendMode.ColorBurn,
        "HardLight" => SKBlendMode.HardLight,
        "SoftLight" => SKBlendMode.SoftLight,
        "Difference" => SKBlendMode.Difference,
        "Exclusion" => SKBlendMode.Exclusion,
        "Hue" => SKBlendMode.Hue,
        "Saturation" => SKBlendMode.Saturation,
        "Color" => SKBlendMode.Color,
        "Luminosity" => SKBlendMode.Luminosity,
        "Add" or "Plus" => SKBlendMode.Plus,
        "Xor" => SKBlendMode.Xor,
        "DestinationIn" => SKBlendMode.DstIn,
        _ => SKBlendMode.SrcOver
    };

    public static SKColor ParseColor(string hex)
    {
        var c = (hex ?? "").Trim().TrimStart('#');
        if (c.Length == 8)
        {
            uint v = uint.Parse(c, System.Globalization.NumberStyles.HexNumber);
            return new SKColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
        if (c.Length == 6)
            return SKColor.Parse("#" + c);
        return SKColors.White;
    }

    /// <summary>Encode a composite to PNG bytes (for the UI / clipboard).</summary>
    public static byte[] ToPngBytes(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
