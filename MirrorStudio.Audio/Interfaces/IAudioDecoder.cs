namespace MirrorStudio.Audio.Interfaces;

/// <summary>Decodes media files to raw mono PCM for analysis (waveforms, meters).</summary>
public interface IAudioDecoder
{
    /// <summary>
    /// Decodes the first audio stream as 16-bit mono PCM at the requested rate.
    /// Returns null when the file has no audio stream.
    /// </summary>
    Task<short[]?> DecodeMono16Async(string filePath, int sampleRateHz,
        CancellationToken cancellationToken = default);
}
