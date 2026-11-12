using SkiaSharp;
using System.IO;
using MirrorStudio.Image.Models;

namespace MirrorStudio.Image.Export;

/// <summary>Writes a composited bitmap to disk in a specific format.</summary>
public interface IImageExporter
{
    string Format { get; }
    void Export(SKBitmap bmp, string path, ExportOptions options);
}

public sealed class ExportOptions
{
    public int Quality { get; set; } = 92;     // JPEG / WebP
    public double Dpi { get; set; } = 96;
    public bool FlattenAlpha { get; set; } = true;
}

public sealed class PngExporter : IImageExporter
{
    public string Format => "PNG";
    public void Export(SKBitmap bmp, string path, ExportOptions o)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var d = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        d.SaveTo(fs);
    }
}

public sealed class JpegExporter : IImageExporter
{
    public string Format => "JPEG";
    public void Export(SKBitmap bmp, string path, ExportOptions o)
    {
        using var img = SKImage.FromBitmap(ImageExportService.Prep(bmp, o));
        using var d = img.Encode(SKEncodedImageFormat.Jpeg, o.Quality);
        using var fs = File.Create(path);
        d.SaveTo(fs);
    }
}

public sealed class WebpExporter : IImageExporter
{
    public string Format => "WebP";
    public void Export(SKBitmap bmp, string path, ExportOptions o)
    {
        using var img = SKImage.FromBitmap(ImageExportService.Prep(bmp, o));
        using var d = img.Encode(SKEncodedImageFormat.Webp, o.Quality);
        using var fs = File.Create(path);
        d.SaveTo(fs);
    }
}

public sealed class BmpExporter : IImageExporter
{
    public string Format => "BMP";
    public void Export(SKBitmap bmp, string path, ExportOptions o)
    {
        using var img = SKImage.FromBitmap(ImageExportService.Prep(bmp, o));
        using var d = img.Encode(SKEncodedImageFormat.Bmp, 100);
        using var fs = File.Create(path);
        d.SaveTo(fs);
    }
}

public sealed class TiffExporter : IImageExporter
{
    public string Format => "TIFF";
    public void Export(SKBitmap bmp, string path, ExportOptions o) =>
        TiffWriter.Write(bmp, path, o.Dpi, o.FlattenAlpha);
}

/// <summary>Picks an exporter by format name or file extension.</summary>
public static class ImageExportService
{
    public static IImageExporter ForFormat(string format) =>
        format.Trim().ToUpperInvariant() switch
        {
            "JPEG" or "JPG" => new JpegExporter(),
            "WEBP" => new WebpExporter(),
            "BMP" => new BmpExporter(),
            "TIFF" or "TIF" => new TiffExporter(),
            _ => new PngExporter()
        };

    public static IImageExporter ForPath(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return ext switch
        {
            "JPG" or "JPEG" => new JpegExporter(),
            "WEBP" => new WebpExporter(),
            "BMP" => new BmpExporter(),
            "TIF" or "TIFF" => new TiffExporter(),
            _ => new PngExporter()
        };
    }

    public static void Export(SKBitmap bmp, string path, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        ForPath(path).Export(bmp, path, options);
    }

    internal static SKBitmap Prep(SKBitmap bmp, ExportOptions o)
    {
        if (!o.FlattenAlpha) return bmp;
        var outB = ImageDocument.MakeBitmap(bmp.Width, bmp.Height);
        using var s = SKSurface.Create(outB.Info, outB.GetPixels());
        s.Canvas.Clear(SKColors.White);
        using var p = new SKPaint { IsAntialias = true };
        s.Canvas.DrawBitmap(bmp, 0, 0, p);
        s.Flush();
        return outB;
    }
}

/// <summary>Minimal uncompressed 24-bit RGB little-Endian TIFF writer.</summary>
internal static class TiffWriter
{
    public static void Write(SKBitmap bmp, string path, double dpi, bool flatten)
    {
        int w = bmp.Width, h = bmp.Height;
        var px = bmp.GetPixels();
        byte[] pixels = new byte[w * h * 3];
        if (px != IntPtr.Zero)
        {
            unsafe
            {
                var src = new Span<byte>(px.ToPointer(), w * h * 4);
                for (int i = 0, j = 0; i < src.Length; i += 4, j += 3)
                {
                    byte a = src[i + 3];
                    double r = src[i], g = src[i + 1], b = src[i + 2];
                    if (a < 255 && a > 0) { double inv = 255.0 / a; r *= inv; g *= inv; b *= inv; }
                    if (a < 255 && flatten) { double add = 255 - a; r += add; g += add; b += add; }
                    pixels[j] = (byte)Math.Min(255, r);
                    pixels[j + 1] = (byte)Math.Min(255, g);
                    pixels[j + 2] = (byte)Math.Min(255, b);
                }
            }
        }

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        int ifdOffset = 8;
        int ifdSize = 2 + 11 * 12 + 4;
        int bitsOffset = ifdOffset + ifdSize;
        int pixelOffset = bitsOffset + 6;
        int rationalOffset = pixelOffset + pixels.Length;

        bw.Write((ushort)0x4949);   // little-endian "II"
        bw.Write((ushort)42);
        bw.Write((uint)ifdOffset);

        bw.Write((ushort)11);
        WriteEntry(bw, 256, 4, 1, (uint)w);              // ImageWidth
        WriteEntry(bw, 257, 4, 1, (uint)h);              // ImageLength
        WriteEntry(bw, 258, 3, 3, (uint)bitsOffset);      // BitsPerSample -> 8,8,8
        WriteEntry(bw, 259, 3, 1, 1);                    // Compression = none
        WriteEntry(bw, 262, 3, 1, 2);                    // Photometric = RGB
        WriteEntry(bw, 273, 4, 1, (uint)pixelOffset);    // StripOffsets
        WriteEntry(bw, 277, 3, 1, 3);                    // SamplesPerPixel
        WriteEntry(bw, 278, 4, 1, (uint)h);              // RowsPerStrip
        WriteEntry(bw, 279, 4, 1, (uint)pixels.Length);  // StripByteCounts
        WriteEntry(bw, 282, 5, 1, 0);                    // XResolution (patched later)
        WriteEntry(bw, 283, 5, 1, 0);                    // YResolution (patched later)
        WriteEntry(bw, 296, 3, 1, 2);                    // ResolutionUnit = inch
        bw.Write((uint)0);                               // next IFD

        // BitsPerSample values (3 shorts)
        bw.Write((ushort)8); bw.Write((ushort)8); bw.Write((ushort)8);

        // Pixels
        bw.Seek(pixelOffset, SeekOrigin.Begin);
        bw.Write(pixels);

        // Resolution rationals (num/den pairs) after pixels
        bw.Seek(rationalOffset, SeekOrigin.Begin);
        uint res = (uint)Math.Round(dpi);
        bw.Write(res); bw.Write((uint)1);
        bw.Write(res); bw.Write((uint)1);

        // Point the X/Y resolution entries at the rational block
        PatchEntryValue(fs, ifdOffset + 2 + 9 * 12 + 8, (uint)rationalOffset);
        PatchEntryValue(fs, ifdOffset + 2 + 10 * 12 + 8, (uint)rationalOffset);

        static void WriteEntry(BinaryWriter w, ushort tag, ushort type, uint count, uint value)
        {
            w.Write(tag); w.Write(type); w.Write(count); w.Write(value);
        }
    }

    private static void PatchEntryValue(FileStream fs, long pos, uint value)
    {
        long cur = fs.Position;
        fs.Position = pos;
        var bw = new BinaryWriter(fs);
        bw.Write(value);
        fs.Position = cur;
    }
}
