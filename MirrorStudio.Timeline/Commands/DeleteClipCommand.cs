using MirrorStudio.Core.Commands;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Commands;

public class DeleteClipCommand : IEditCommand
{
    private readonly TimelineTrack _track;
    private readonly TimelineClip _clip;

    private int _index;

    public DeleteClipCommand(
        TimelineTrack track,
        TimelineClip clip)
    {
        _track = track;
        _clip = clip;
    }

    public void Execute()
    {
        _index =
            _track.Clips.IndexOf(_clip);

        if (_index >= 0)
            _track.Clips.Remove(_clip);
    }

    public void Undo()
    {
        if (_index < 0)
            return;

        _track.Clips.Insert(
            _index,
            _clip);
    }
}
