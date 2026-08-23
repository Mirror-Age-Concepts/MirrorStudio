using System.Runtime.InteropServices;

namespace MirrorStudio.Video.Services;

/// <summary>
/// Managed BT.601 limited-range YUV420P → BGRA converter.
/// Used when libswscale is not linked; planes are supplied as pointers into
/// contiguous native or pinned-managed memory.
/// </summary>
public class Yuv420pPixelConverter : Video.Interfaces.IPixelConverter
{
    public byte[] ConvertToBgra(IntPtr frame, int width, int height)
    {
        int chromaW = (width + 1) / 2;
        int chromaH = (height + 1) / 2;
        int lumaStride = width;
        int chromaStride = chromaW;

        IntPtr yPlane = frame;
        IntPtr uPlane = IntPtr.Add(yPlane, lumaStride * height);
        IntPtr vPlane = IntPtr.Add(uPlane, chromaStride * chromaH);

        var bgra = new byte[width * height * 4];

        unsafe
        {
            var yPtr = (byte*)yPlane;
            var uPtr = (byte*)uPlane;
            var vPtr = (byte*)vPlane;

            for (int y = 0; y < height; y++)
            {
                int rowOut = y * width * 4;
                int uvRow = y / 2;

                for (int x = 0; x < width; x++)
                {
                    int uvIndex = uvRow * chromaStride + x / 2;

                    int yy = yPtr[y * lumaStride + x] - 16;
                    int uu = uPtr[uvIndex] - 128;
                    int vv = vPtr[uvIndex] - 128;

                    int c = 298 * yy;
                    int d = uu;
                    int e = vv;

                    int r = (c + 409 * e + 128) >> 8;
                    int g = (c - 100 * d - 208 * e + 128) >> 8;
                    int b = (c + 516 * d + 128) >> 8;

                    int o = rowOut + x * 4;
                    bgra[o] = (byte)Math.Clamp(b, 0, 255);
                    bgra[o + 1] = (byte)Math.Clamp(g, 0, 255);
                    bgra[o + 2] = (byte)Math.Clamp(r, 0, 255);
                    bgra[o + 3] = 255;
                }
            }
        }

        return bgra;
    }
}
