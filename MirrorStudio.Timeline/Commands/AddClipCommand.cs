using MirrorStudio.Core.Commands;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Commands;

/// <summary>Adds a pre-built clip to a track at its recorded index.</summary>
public class AddClipCommand : IEditCommand
{
    private readonly TimelineTrack _track;
    private readonly TimelineClip _clip;

    private int _index;

    public AddClipCommand(
        TimelineTrack track,
        TimelineClip clip)
    {
        _track = track;
        _clip = clip;
    }

    public void Execute()
    {
        if (_track.Clips.Contains(_clip))
            return;

        _index = _track.Clips.Count;
        _track.Clips.Add(_clip);
    }

    public void Undo()
    {
        _track.Clips.Remove(_clip);
    }
}
