using MirrorStudio.Core.Commands;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Commands;

public class MoveClipCommand : IEditCommand
{
    private readonly TimelineClip _clip;
    private readonly double _newStart;

    private double _oldStart;

    public MoveClipCommand(
        TimelineClip clip,
        double newStart)
    {
        _clip = clip;
        _newStart = newStart;
    }

    public void Execute()
    {
        _oldStart = _clip.Start;

        _clip.Start =
            Math.Max(0, _newStart);
    }

    public void Undo()
    {
        _clip.Start = _oldStart;
    }
}
