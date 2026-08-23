using MirrorStudio.Core.Commands;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Commands;

public class SplitClipCommand : IEditCommand
{
    private readonly TimelineTrack _track;
    private readonly TimelineClip _original;
    private readonly double _splitTime;

    private TimelineClip? _left;
    private TimelineClip? _right;

    private int _index;

    public SplitClipCommand(
        TimelineTrack track,
        TimelineClip original,
        double splitTime)
    {
        _track = track;
        _original = original;
        _splitTime = splitTime;
    }

    public void Execute()
    {
        _index =
            _track.Clips.IndexOf(_original);

        if (_index < 0)
            return;

        var localPosition =
            _splitTime - _original.Start;

        if (localPosition <= 0 ||
            localPosition >= _original.Duration)
            return;

        var leftDuration =
            localPosition;

        var rightDuration =
            _original.Duration -
            leftDuration;

        var sourceOffset =
            _original.SourceEnd - _original.SourceStart;

        var leftSourceLength =
            sourceOffset > 0
                ? sourceOffset * leftDuration / _original.Duration
                : leftDuration;

        _left = new TimelineClip
        {
            MediaAssetId =
                _original.MediaAssetId,

            Name =
                _original.Name,

            Start =
                _original.Start,

            Duration =
                leftDuration,

            SourceStart =
                _original.SourceStart,

            SourceEnd =
                _original.SourceStart +
                leftSourceLength,

            Opacity = _original.Opacity,
            Volume = _original.Volume,
            PositionX = _original.PositionX,
            PositionY = _original.PositionY,
            ScaleX = _original.ScaleX,
            ScaleY = _original.ScaleY,
            Rotation = _original.Rotation
        };

        _right = new TimelineClip
        {
            MediaAssetId =
                _original.MediaAssetId,

            Name =
                _original.Name,

            Start =
                _splitTime,

            Duration =
                rightDuration,

            SourceStart =
                _original.SourceStart +
                leftSourceLength,

            SourceEnd =
                _original.SourceEnd,

            Opacity = _original.Opacity,
            Volume = _original.Volume,
            PositionX = _original.PositionX,
            PositionY = _original.PositionY,
            ScaleX = _original.ScaleX,
            ScaleY = _original.ScaleY,
            Rotation = _original.Rotation
        };

        _track.Clips.RemoveAt(_index);

        _track.Clips.Insert(
            _index,
            _left);

        _track.Clips.Insert(
            _index + 1,
            _right);
    }

    /// <summary>The right half created by the split, or null when nothing was split.</summary>
    public TimelineClip? Right => _right;

    public void Undo()
    {
        if (_left == null ||
            _right == null)
            return;

        _track.Clips.Remove(_left);
        _track.Clips.Remove(_right);

        if (_index < 0 || _index > _track.Clips.Count)
            _index = _track.Clips.Count;

        _track.Clips.Insert(
            _index,
            _original);
    }
}
