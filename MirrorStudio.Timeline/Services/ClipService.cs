using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.Services;

public class ClipService
{
    public void Move(
        TimelineClip clip,
        double newStart)
    {
        clip.Start =
            Math.Max(0, newStart);
    }

    public void TrimStart(
        TimelineClip clip,
        double amount)
    {
        if (amount <= 0)
            return;

        if (amount >= clip.Duration - 0.05)
            return;

        clip.Start += amount;
        clip.Duration -= amount;
        clip.SourceStart += amount;
    }

    public void TrimEnd(
        TimelineClip clip,
        double amount)
    {
        if (amount <= 0)
            return;

        if (amount >= clip.Duration - 0.05)
            return;

        clip.Duration -= amount;
        clip.SourceEnd -= amount;
    }

    public (TimelineClip Left, TimelineClip Right)?
        Split(
            TimelineClip clip,
            double splitTime)
    {
        if (splitTime <= clip.Start ||
            splitTime >= clip.Start + clip.Duration)
        {
            return null;
        }

        var leftDuration =
            splitTime - clip.Start;

        var rightDuration =
            clip.Duration - leftDuration;

        var left =
            new TimelineClip
            {
                MediaAssetId =
                    clip.MediaAssetId,

                Name =
                    clip.Name,

                Start =
                    clip.Start,

                Duration =
                    leftDuration,

                SourceStart =
                    clip.SourceStart,

                SourceEnd =
                    clip.SourceStart +
                    leftDuration
            };

        var right =
            new TimelineClip
            {
                MediaAssetId =
                    clip.MediaAssetId,

                Name =
                    clip.Name,

                Start =
                    splitTime,

                Duration =
                    rightDuration,

                SourceStart =
                    clip.SourceStart +
                    leftDuration,

                SourceEnd =
                    clip.SourceEnd
            };

        CopyTransform(clip, left);
        CopyTransform(clip, right);

        return (left, right);
    }

    private static void CopyTransform(TimelineClip source, TimelineClip target)
    {
        target.PositionX = source.PositionX;
        target.PositionY = source.PositionY;
        target.ScaleX = source.ScaleX;
        target.ScaleY = source.ScaleY;
        target.Rotation = source.Rotation;
        target.Opacity = source.Opacity;
        target.Volume = source.Volume;
    }
}
