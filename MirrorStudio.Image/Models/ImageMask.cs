namespace MirrorStudio.Image.Models;

/// <summary>
/// An 8-bit alpha mask (0 = fully masked out, 255 = fully visible), one byte
/// per pixel. Used both for layer masks and for active edit selections.
/// </summary>
public sealed class ImageMask
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public ImageMask(int width, int height)
    {
        Width = width;
        Height = height;
        Data = new byte[width * height];
    }

    public ImageMask(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        Data = data;
        if (Data.Length != width * height)
            throw new System.ArgumentException("Mask data length mismatch");
    }

    public int Index(int x, int y) => y * Width + x;

    public byte GetAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return 0;
        return Data[Index(x, y)];
    }

    public void SetAt(int x, int y, byte value)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        Data[Index(x, y)] = value;
    }

    /// <summary>Fill the whole mask with a single alpha value (255 = all visible).</summary>
    public void Fill(byte value) => System.Array.Fill(Data, value);

    public ImageMask Clone() => new(Width, Height, (byte[])Data.Clone());
}
