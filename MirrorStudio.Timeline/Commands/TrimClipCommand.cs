using MirrorStudio.Core.Commands;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Commands;

public class TrimClipCommand : IEditCommand
{
    private readonly TimelineClip _clip;

    private readonly double _newStart;
    private readonly double _newDuration;

    private double _oldStart;
    private double _oldDuration;

    public TrimClipCommand(
        TimelineClip clip,
        double newStart,
        double newDuration)
    {
        _clip = clip;

        _newStart = newStart;
        _newDuration = newDuration;
    }

    public void Execute()
    {
        _oldStart = _clip.Start;
        _oldDuration = _clip.Duration;

        _clip.Start =
            Math.Max(0, _newStart);

        _clip.Duration =
            Math.Max(0.01, _newDuration);
    }

    public void Undo()
    {
        _clip.Start = _oldStart;
        _clip.Duration = _oldDuration;
    }
}
