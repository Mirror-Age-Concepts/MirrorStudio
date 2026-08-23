namespace MirrorStudio.Core.Enums;

public enum PreviewQuality
{
    Full,
    Half,
    Quarter,
    Proxy
}

public static class PreviewQualityExtensions
{
    /// <summary>Decode height for a preview quality; proxy decodes at 720p.</summary>
    public static int? DecodeHeight(this PreviewQuality quality, int sourceHeight) => quality switch
    {
        PreviewQuality.Full => null,                       // native resolution
        PreviewQuality.Half => Math.Max(2, Even(sourceHeight / 2)),
        PreviewQuality.Quarter => Math.Max(2, Even(sourceHeight / 4)),
        PreviewQuality.Proxy => 720,
        _ => null
    };

    private static int Even(int v) => v % 2 == 0 ? v : v + 1;
}
