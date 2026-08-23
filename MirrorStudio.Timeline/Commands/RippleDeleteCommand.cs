using MirrorStudio.Core.Commands;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Commands;

/// <summary>
/// Ripple delete (spec M7 §12): removes a clip AND closes the gap by shifting
/// all later clips on the same track left by the deleted duration.
///
///   NORMAL delete:  A │     │ C      RIPPLE delete:  A │ C
/// </summary>
public class RippleDeleteCommand : IEditCommand
{
    private readonly TimelineTrack _track;
    private readonly TimelineClip _clip;

    private int _index;
    private List<TimelineClip>? _shifted;

    public RippleDeleteCommand(
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

        if (_index < 0)
            return;

        // everything to the right of the deleted clip closes the gap
        _shifted = _track.Clips
            .Skip(_index + 1)
            .ToList();

        foreach (var c in _shifted)
            c.Start = Math.Max(0, c.Start - _clip.Duration);

        _track.Clips.Remove(_clip);
    }

    public void Undo()
    {
        if (_index < 0)
            return;

        if (_shifted != null)
            foreach (var c in _shifted)
                c.Start += _clip.Duration;

        if (_index > _track.Clips.Count)
            _index = _track.Clips.Count;

        _track.Clips.Insert(
            _index,
            _clip);
    }
}
