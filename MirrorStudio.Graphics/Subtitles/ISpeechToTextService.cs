namespace MirrorStudio.Graphics.Subtitles;

/// <summary>
/// Provider-independent speech-recognition contract (Milestone 11 §20).
/// Implementations: whisper.cpp / Azure Speech / local Vosk — the editor
/// only ever sees Subtitle items back.
/// </summary>
public interface ISpeechToTextService
{
    Task<IReadOnlyList<Subtitle>> TranscribeAsync(
        string audioFile,
        CancellationToken cancellationToken);
}
