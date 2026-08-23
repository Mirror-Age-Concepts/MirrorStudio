namespace MirrorStudio.Audio.Services;

/// <summary>
/// Pure peak/RMS math for waveforms (spec M9 §11): millions of decoded samples
/// collapse to one peak + RMS point per pixel group, normalized to full scale.
/// </summary>
public static class PeakAnalyzer
{
    /// <summary>Group count for a PCM buffer at the given group size.</summary>
    public static int GroupCount(int sampleCount, int groupSize) =>
        Math.Max(1, (int)Math.Ceiling(sampleCount / (double)Math.Max(1, groupSize)));

    /// <summary>
    /// Computes normalized peak + RMS arrays. Peaks use max |sample| per group;
    /// RMS uses root-mean-square, both scaled by 1/32768.
    /// </summary>
    public static (float[] Peaks, float[] Rms) Compute(short[] pcm, int groupSize)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        if (pcm.Length == 0)
            return ([], []);

        int groups = GroupCount(pcm.Length, groupSize);
        var peaks = new float[groups];
        var rms = new float[groups];
        const double scale = 1.0 / 32768.0;

        for (int g = 0; g < groups; g++)
        {
            int from = g * groupSize;
            int to = Math.Min(pcm.Length, from + groupSize);

            double sumSquares = 0;
            short max = 0;
            for (int i = from; i < to; i++)
            {
                short s = pcm[i];
                short abs = s == short.MinValue ? short.MaxValue : Math.Abs(s);
                if (abs > max) max = abs;
                sumSquares += (double)s * s;
            }

            int count = to - from;
            peaks[g] = (float)(max * scale);
            rms[g] = (float)(Math.Sqrt(sumSquares / Math.Max(1, count)) * scale);
        }

        return (peaks, rms);
    }
}
