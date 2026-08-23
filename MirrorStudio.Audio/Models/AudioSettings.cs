namespace MirrorStudio.Audio.Models;

/// <summary>Project-wide audio format (spec M9 §4). Film standard: 48 kHz.</summary>
public class AudioSettings
{
    public int SampleRate { get; set; } = 48000;

    public int Channels { get; set; } = 2;

    public int BitsPerSample { get; set; } = 32;
}
