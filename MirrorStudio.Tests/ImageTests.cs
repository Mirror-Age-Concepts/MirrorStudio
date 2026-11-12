using System;
using System.Drawing;
using System.IO;
using MirrorStudio.Image.Adjustments;
using MirrorStudio.Image.Brushes;
using MirrorStudio.Image.Export;
using MirrorStudio.Image.Models;
using MirrorStudio.Image.Rendering;
using MirrorStudio.Image.Tools;
using NUnit.Framework;
using SkiaSharp;

namespace MirrorStudio.Tests;

[TestFixture]
public class ImageEditorTests
{
    private static SKBitmap Make(int w, int h, byte r, byte g, byte b, byte a = 255)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var px = bmp.GetPixels();
        unsafe
        {
            var s = new Span<byte>(px.ToPointer(), w * h * 4);
            for (int i = 0; i < s.Length; i += 4) { s[i] = r; s[i + 1] = g; s[i + 2] = b; s[i + 3] = a; }
        }
        return bmp;
    }

    private static (byte r, byte g, byte b, byte a) At(SKBitmap bmp, int x, int y)
    {
        var px = bmp.GetPixels();
        unsafe
        {
            var s = new Span<byte>(px.ToPointer(), bmp.Width * bmp.Height * 4);
            int o = (y * bmp.Width + x) * 4;
            return (s[o], s[o + 1], s[o + 2], s[o + 3]);
        }
    }

    [Test]
    public void Brightness_Lightens()
    {
        var bmp = Make(2, 2, 100, 100, 100);
        AdjustmentProcessor.Apply(bmp, new Adjustment { Type = Adjustment.Brightness, Parameters = { ["Amount"] = 0.5 } });
        var (r, _, _, a) = At(bmp, 0, 0);
        Assert.That(r, Is.GreaterThan(200));
        Assert.That(a, Is.EqualTo(255));
    }

    [Test]
    public void Invert_FlipsChannels()
    {
        var bmp = Make(2, 2, 10, 20, 30);
        AdjustmentProcessor.Apply(bmp, new Adjustment { Type = Adjustment.Invert });
        var (r, g, b, _) = At(bmp, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(r, Is.EqualTo(245));
            Assert.That(g, Is.EqualTo(235));
            Assert.That(b, Is.EqualTo(225));
        });
    }

    [Test]
    public void Grayscale_EqualisesChannels()
    {
        var bmp = Make(2, 2, 200, 100, 50);
        AdjustmentProcessor.Apply(bmp, new Adjustment { Type = Adjustment.Grayscale });
        var (r, g, b, _) = At(bmp, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(r, Is.EqualTo(g));
            Assert.That(g, Is.EqualTo(b));
        });
    }

    [Test]
    public void SaturationDownToZero_Grayscales()
    {
        var bmp = Make(2, 2, 200, 100, 50);
        AdjustmentProcessor.Apply(bmp, new Adjustment { Type = Adjustment.Saturation, Parameters = { ["Amount"] = -1 } });
        var (r, g, b, _) = At(bmp, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(r, Is.EqualTo(g));
            Assert.That(g, Is.EqualTo(b));
        });
    }

    [Test]
    public void Levels_BlackPointClips()
    {
        var bmp = Make(2, 2, 60, 180, 90);
        AdjustmentProcessor.Apply(bmp, new Adjustment
        {
            Type = Adjustment.Levels,
            Parameters = { ["BlackPoint"] = 128, ["WhitePoint"] = 255, ["Gamma"] = 1 }
        });
        var (r, _, _, _) = At(bmp, 0, 0);
        Assert.That(r, Is.EqualTo(0)); // 60 < 128 -> 0
    }

    [Test]
    public void RectangularSelection_FillsInterior()
    {
        var sel = new ImageSelection
        {
            Mode = ImageSelection.SelectionMode.Rectangle,
            Bounds = new Rectangle(2, 2, 4, 4)
        };
        var mask = sel.BuildMask(10, 10);
        Assert.Multiple(() =>
        {
            Assert.That(mask!.GetAt(4, 4), Is.EqualTo(255));
            Assert.That(mask.GetAt(0, 0), Is.EqualTo(0));
        });
    }

    [Test]
    public void EllipseSelection_ExcludesCorners()
    {
        var sel = new ImageSelection
        {
            Mode = ImageSelection.SelectionMode.Ellipse,
            Bounds = new Rectangle(0, 0, 10, 10)
        };
        var mask = sel.BuildMask(10, 10);
        Assert.Multiple(() =>
        {
            Assert.That(mask!.GetAt(5, 5), Is.EqualTo(255));
            Assert.That(mask.GetAt(0, 0), Is.EqualTo(0));
        });
    }

    [Test]
    public void PolygonSelection_CoversCenter()
    {
        var sel = new ImageSelection
        {
            Mode = ImageSelection.SelectionMode.Polygon,
            Points = new() { new(0, 0), new(10, 0), new(10, 10), new(0, 10) }
        };
        var mask = sel.BuildMask(10, 10);
        Assert.That(mask!.GetAt(5, 5), Is.EqualTo(255));
    }

    [Test]
    public void Document_AddRemoveReorder()
    {
        var doc = new ImageDocument { Width = 10, Height = 10 };
        var a = new ImageLayer { Name = "A" };
        var b = new ImageLayer { Name = "B" };
        doc.AddLayer(a);
        doc.AddLayer(b); // top
        Assert.That(doc.Layers.Count, Is.EqualTo(2));
        Assert.That(doc.TopLayer, Is.EqualTo(b));

        doc.MoveLayer(b.Id, 1); // move down
        Assert.That(doc.Layers[1], Is.EqualTo(b));

        Assert.That(doc.RemoveLayer(a.Id), Is.True);
        Assert.That(doc.Layers.Count, Is.EqualTo(1));
    }

    [Test]
    public void Document_Crop_ShrinksAndOffsets()
    {
        var doc = new ImageDocument { Width = 20, Height = 20 };
        var layer = new ImageLayer { Kind = ImageLayerKind.Raster, Bitmap = Make(20, 20, 255, 0, 0) };
        doc.AddLayer(layer);
        doc.Crop(new Rectangle(5, 5, 10, 10));
        Assert.Multiple(() =>
        {
            Assert.That(doc.Width, Is.EqualTo(10));
            Assert.That(doc.Height, Is.EqualTo(10));
        });
    }

    [Test]
    public void Document_Rotate90_SwapsDimensions()
    {
        var doc = new ImageDocument { Width = 20, Height = 10 };
        doc.AddLayer(new ImageLayer { Kind = ImageLayerKind.Raster, Bitmap = Make(20, 10, 0, 255, 0) });
        doc.Rotate(90);
        Assert.Multiple(() =>
        {
            Assert.That(doc.Width, Is.EqualTo(10));
            Assert.That(doc.Height, Is.EqualTo(20));
        });
    }

    [Test]
    public void Compositor_TopOpaqueLayerWins()
    {
        var doc = new ImageDocument { Width = 4, Height = 4, BackgroundColor = "#000000FF" };
        doc.AddLayer(new ImageLayer { Kind = ImageLayerKind.Raster, Bitmap = Make(4, 4, 0, 0, 255) });      // bottom blue
        doc.AddLayer(new ImageLayer { Kind = ImageLayerKind.Raster, Bitmap = Make(4, 4, 255, 0, 0) });      // top red
        var outB = ImageCompositor.Composite(doc);
        var (r, _, _, _) = At(outB, 0, 0);
        Assert.That(r, Is.EqualTo(255));
    }

    [Test]
    public void Compositor_MultiplyBlendMode()
    {
        var doc = new ImageDocument { Width = 2, Height = 2 };
        doc.AddLayer(new ImageLayer { Kind = ImageLayerKind.Raster, Bitmap = Make(2, 2, 200, 200, 200) });
        doc.AddLayer(new ImageLayer
        {
            Kind = ImageLayerKind.Raster,
            BlendMode = "Multiply",
            Bitmap = Make(2, 2, 100, 100, 100)
        });
        var outB = ImageCompositor.Composite(doc);
        var (r, _, _, _) = At(outB, 0, 0);
        Assert.That(r, Is.LessThan(200)); // multiply darkens
    }

    [Test]
    public void BlendModeParser_KnownValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ImageCompositor.ParseBlend("Normal"), Is.EqualTo(SKBlendMode.SrcOver));
            Assert.That(ImageCompositor.ParseBlend("Multiply"), Is.EqualTo(SKBlendMode.Multiply));
            Assert.That(ImageCompositor.ParseBlend("Overlay"), Is.EqualTo(SKBlendMode.Overlay));
            Assert.That(ImageCompositor.ParseBlend("Bogus"), Is.EqualTo(SKBlendMode.SrcOver));
        });
    }

    [Test]
    public void Export_Png_RoundTrips()
    {
        var doc = new ImageDocument { Width = 8, Height = 8 };
        doc.AddLayer(new ImageLayer { Kind = ImageLayerKind.Raster, Bitmap = Make(8, 8, 11, 22, 33) });
        var bmp = ImageCompositor.Composite(doc);
        var path = Path.Combine(Path.GetTempPath(), $"ms_img_{Guid.NewGuid():N}.png");
        ImageExportService.Export(bmp, path);
        try
        {
            Assert.That(File.Exists(path), Is.True);
            using var img = SKImage.FromEncodedData(path);
            Assert.Multiple(() =>
            {
                Assert.That(img.Width, Is.EqualTo(8));
                Assert.That(img.Height, Is.EqualTo(8));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Export_Tiff_WritesLittleEndianHeader()
    {
        var doc = new ImageDocument { Width = 4, Height = 4 };
        doc.AddLayer(new ImageLayer { Kind = ImageLayerKind.Raster, Bitmap = Make(4, 4, 255, 255, 255) });
        var bmp = ImageCompositor.Composite(doc);
        var path = Path.Combine(Path.GetTempPath(), $"ms_img_{Guid.NewGuid():N}.tiff");
        ImageExportService.Export(bmp, path, new ExportOptions { Dpi = 300 });
        try
        {
            var bytes = File.ReadAllBytes(path);
            Assert.Multiple(() =>
            {
                Assert.That(bytes[0], Is.EqualTo((byte)'I'));
                Assert.That(bytes[1], Is.EqualTo((byte)'I'));
                Assert.That(bytes[2], Is.EqualTo(42));
                Assert.That(bytes.Length, Is.GreaterThan(8 + 4 + 4 * 4));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void BrushStamp_PaintsPixel()
    {
        var bmp = Make(20, 20, 0, 0, 0, 0); // transparent
        var brush = new Brush { Size = 10, Hardness = 1, Opacity = 1, Flow = 1 };
        BrushEngine.Stamp(bmp, brush, 10, 10, SKColors.Red, erase: false, mask: null);
        var (r, _, _, a) = At(bmp, 10, 10);
        Assert.Multiple(() =>
        {
            Assert.That(r, Is.EqualTo(255));
            Assert.That(a, Is.EqualTo(255));
        });
    }

    [Test]
    public void BrushStamp_RespectsMask()
    {
        var bmp = Make(20, 20, 0, 0, 0, 0);
        var mask = new ImageMask(20, 20);
        mask.Fill(0); // fully masked out
        var brush = new Brush { Size = 10, Hardness = 1, Opacity = 1, Flow = 1 };
        BrushEngine.Stamp(bmp, brush, 10, 10, SKColors.Red, erase: false, mask);
        var (_, _, _, a) = At(bmp, 10, 10);
        Assert.That(a, Is.EqualTo(0));
    }
}
