using System.Drawing;

namespace MirrorStudio.Image.Models;

/// <summary>Active editing selection. Resolves to an <see cref="ImageMask"/>.</summary>
public sealed class ImageSelection
{
    public enum SelectionMode
    {
        None,
        Rectangle,
        Ellipse,
        Lasso,
        Polygon,
        MagicWand
    }

    public SelectionMode Mode { get; set; } = SelectionMode.None;
    public Rectangle Bounds { get; set; }
    public List<Point> Points { get; set; } = new();
    public int Tolerance { get; set; } = 24;     // magic-wand colour tolerance
    public int Feather { get; set; }             // soft edge in pixels
    public bool Invert { get; set; }

    public bool IsEmpty => Mode == SelectionMode.None;

    public void Clear() => Mode = SelectionMode.None;

    public ImageSelection Clone()
    {
        return new ImageSelection
        {
            Mode = Mode,
            Bounds = Bounds,
            Points = new List<Point>(Points),
            Tolerance = Tolerance,
            Feather = Feather,
            Invert = Invert
        };
    }

    /// <summary>Build the alpha mask for a document of the given size.</summary>
    public ImageMask? BuildMask(int width, int height)
    {
        if (Mode == SelectionMode.None) return null;

        var mask = new ImageMask(width, height);
        if (Mode is SelectionMode.Rectangle or SelectionMode.Ellipse)
        {
            int x0 = Bounds.X, y0 = Bounds.Y;
            int x1 = Bounds.Right, y1 = Bounds.Bottom;
            for (int y = Math.Max(0, y0); y < Math.Min(height, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
            {
                bool inside = Mode == SelectionMode.Rectangle
                    || InEllipse(x, y, Bounds);
                if (inside) mask.SetAt(x, y, 255);
            }
        }
        else if (Mode is SelectionMode.Lasso or SelectionMode.Polygon)
        {
            RasterizePolygon(mask, Points, width, height);
        }

        if (Feather > 0) FeatherMask(mask, Feather);
        if (Invert)
            for (int i = 0; i < mask.Data.Length; i++)
                mask.Data[i] = (byte)(255 - mask.Data[i]);
        return mask;
    }

    private static bool InEllipse(int x, int y, Rectangle r)
    {
        double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
        double rx = r.Width / 2.0, ry = r.Height / 2.0;
        if (rx <= 0 || ry <= 0) return false;
        double nx = (x - cx) / rx, ny = (y - cy) / ry;
        return nx * nx + ny * ny <= 1.0;
    }

    private static void RasterizePolygon(ImageMask mask, List<Point> pts, int w, int h)
    {
        if (pts.Count < 3) return;
        int minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        minY = Math.Max(0, minY); maxY = Math.Min(h - 1, maxY);
        for (int y = minY; y <= maxY; y++)
        {
            var xs = new List<int>();
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                int yi = pts[i].Y, yj = pts[j].Y;
                if ((yi > y) != (yj > y))
                {
                    int xi = pts[i].X, xj = pts[j].X;
                    xs.Add(xi + (y - yi) * (xj - xi) / (yj - yi));
                }
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int xa = Math.Max(0, xs[k]), xb = Math.Min(w - 1, xs[k + 1]);
                for (int x = xa; x <= xb; x++) mask.SetAt(x, y, 255);
            }
        }
    }

    private static void FeatherMask(ImageMask mask, int radius)
    {
        var src = (byte[])mask.Data.Clone();
        int w = mask.Width, h = mask.Height;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // average neighbourhood to soften edges
            int sum = 0, n = 0;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                sum += src[ny * w + nx]; n++;
            }
            mask.Data[y * w + x] = (byte)(sum / n);
        }
    }
}
