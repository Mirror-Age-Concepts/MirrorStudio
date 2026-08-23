using System.Buffers.Binary;
using System.Text;

namespace MirrorStudio.Audio.Models;

/// <summary>
/// Downsampled waveform representation: one peak + RMS pair per pixel group.
/// Both arrays are normalized to 0..1 against full-scale audio.
/// </summary>
public class AudioWaveform
{
    private const uint Magic = 0x4D535746; // "MSWF"
    private const ushort Version = 1;

    public float[] Peaks { get; set; } = [];

    public float[] Rms { get; set; } = [];

    /// <summary>Source samples grouped per waveform point.</summary>
    public int GroupSize { get; set; }

    /// <summary>Sample rate the peaks were computed from.</summary>
    public int SampleRate { get; set; }

    public float this[int index] => Peaks[index];

    public int PointCount => Peaks.Length;

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream(Peaks.Length * 8 + 24);
        using var w = new BinaryWriter(ms);

        w.Write(Magic);
        w.Write(Version);
        w.Write(GroupSize);
        w.Write(SampleRate);
        w.Write(Peaks.Length);
        foreach (var p in Peaks) w.Write(p);
        foreach (var r in Rms) w.Write(r);
        w.Flush();
        return ms.ToArray();
    }

    public static AudioWaveform FromBytes(byte[] bytes)
    {
        try
        {
            return Parse(bytes ?? throw new InvalidDataException("Empty waveform data."));
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("Truncated waveform data.");
        }
    }

    private static AudioWaveform Parse(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes ?? throw new InvalidDataException("Empty waveform data."));
        using var r = new BinaryReader(ms);

        if (r.ReadUInt32() != Magic)
            throw new InvalidDataException("Not a MirrorStudio waveform file.");
        if (r.ReadUInt16() != Version)
            throw new InvalidDataException("Unsupported waveform version.");

        var wf = new AudioWaveform
        {
            GroupSize = r.ReadInt32(),
            SampleRate = r.ReadInt32(),
        };
        int count = r.ReadInt32();

        wf.Peaks = new float[count];
        for (int i = 0; i < count; i++) wf.Peaks[i] = r.ReadSingle();
        wf.Rms = new float[count];
        for (int i = 0; i < count; i++) wf.Rms[i] = r.ReadSingle();

        return wf;
    }
}
