using MirrorStudio.Core.Commands;
using MirrorStudio.Core.Models;

namespace MirrorStudio.App.Services;

// ---------- clip lifecycle ----------

public sealed class AddClipCommand(TimelineData timeline, ClipModel clip) : IEditCommand
{
    public string Name => "Add Clip";
    public void Execute()
    {
        if (!timeline.Clips.Contains(clip)) timeline.Clips.Add(clip);
    }
    public void Undo() => timeline.Clips.Remove(clip);
}

public sealed class DeleteClipCommand(TimelineData timeline, ClipModel clip, bool ripple) : IEditCommand
{
    private readonly int _index = timeline.Clips.IndexOf(clip);
    private readonly List<(ClipModel Clip, double Start)> _rippled = ripple
        ? timeline.Clips
            .Where(c => c != clip && c.TrackKind == clip.TrackKind && c.TrackIndex == clip.TrackIndex && c.StartTime >= clip.StartTime)
            .Select(c => (c, c.StartTime))
            .ToList()
        : [];

    public string Name => "Delete Clip";

    public void Execute()
    {
        timeline.Clips.Remove(clip);
        if (ripple)
            foreach (var (c, _) in _rippled)
                c.StartTime = Math.Max(0, c.StartTime - clip.Duration);
    }

    public void Undo()
    {
        foreach (var (c, start) in _rippled) c.StartTime = start;
        _index.ClampInsert(timeline.Clips, clip);
    }
}

public sealed class SplitClipCommand(
    TimelineData timeline,
    ClipModel left,
    ClipModel right,
    double origStart, double origDuration, double origIn, double origOut) : IEditCommand
{
    // left/right are already mutated + inserted by TimelineEngine.SplitAt when pushed.
    public string Name => "Split Clip";

    public void Execute()
    {
        left.StartTime = origStart;                       // unchanged side
        left.Duration = right.StartTime - origStart;
        left.InPoint = origIn;
        left.OutPoint = origIn + left.Duration;
        if (!timeline.Clips.Contains(right)) timeline.Clips.Add(right);
    }

    public void Undo()
    {
        left.StartTime = origStart;
        left.Duration = origDuration;
        left.InPoint = origIn;
        left.OutPoint = origOut;
        timeline.Clips.Remove(right);
    }
}

// ---------- transforms ----------

public sealed class MoveClipCommand(
    ClipModel clip,
    double oldStart, int oldTrack,
    double newStart, int newTrack) : IEditCommand
{
    public string Name => "Move Clip";
    public void Execute()
    {
        clip.StartTime = newStart;
        clip.TrackIndex = newTrack;
    }
    public void Undo()
    {
        clip.StartTime = oldStart;
        clip.TrackIndex = oldTrack;
    }
}

public sealed class TrimClipCommand(
    ClipModel clip,
    double oldStart, double oldDuration, double oldIn, double oldOut,
    double newStart, double newDuration, double newIn, double newOut) : IEditCommand
{
    public string Name => "Trim Clip";
    public void Execute()
    {
        clip.StartTime = newStart; clip.Duration = newDuration;
        clip.InPoint = newIn; clip.OutPoint = newOut;
    }
    public void Undo()
    {
        clip.StartTime = oldStart; clip.Duration = oldDuration;
        clip.InPoint = oldIn; clip.OutPoint = oldOut;
    }
}

/// <summary>Batches inspector numeric changes into one undo step.</summary>
public sealed class PropertySetCommand(ClipModel clip) : IEditCommand
{
    private readonly Dictionary<string, (double Old, double New)> _changes = [];
    private readonly Dictionary<string, bool> _flagChanges = [];

    public string Name => "Change Properties";
    public IReadOnlyDictionary<string, (double Old, double New)> Changes => _changes;

    public PropertySetCommand Track(string property, double oldValue, double newValue)
    {
        if (!oldValue.Equals(newValue)) _changes[property] = (oldValue, newValue);
        return this;
    }

    public PropertySetCommand TrackFlag(string property, bool oldValue, bool newValue)
    {
        if (oldValue != newValue) _flagChanges[property] = newValue;
        return this;
    }

    public bool HasChanges => _changes.Count > 0 || _flagChanges.Count > 0;

    public void Execute()
    {
        foreach (var (key, (_, @new)) in _changes) ApplyValue(clip, key, @new);
        foreach (var (key, @new) in _flagChanges) ApplyFlag(clip, key, @new);
    }

    public void Undo()
    {
        foreach (var (key, (old, _)) in _changes) ApplyValue(clip, key, old);
        foreach (var (key, @new) in _flagChanges) ApplyFlag(clip, key, !@new);
    }

    internal static void ApplyValue(ClipModel c, string key, double v)
    {
        switch (key)
        {
            case nameof(ClipModel.PositionX): c.PositionX = v; break;
            case nameof(ClipModel.PositionY): c.PositionY = v; break;
            case nameof(ClipModel.ScaleX): c.ScaleX = v; break;
            case nameof(ClipModel.ScaleY): c.ScaleY = v; break;
            case nameof(ClipModel.Rotation): c.Rotation = v; break;
            case nameof(ClipModel.Opacity): c.Opacity = v; break;
            case nameof(ClipModel.Volume): c.Volume = v; break;
            case nameof(ClipModel.Pan): c.Pan = v; break;
            case nameof(ClipModel.GainDb): c.GainDb = v; break;
            case nameof(ClipModel.FadeIn): c.FadeIn = v; break;
            case nameof(ClipModel.FadeOut): c.FadeOut = v; break;
        }
    }

    internal static void ApplyFlag(ClipModel c, string key, bool v)
    {
        switch (key)
        {
            case nameof(ClipModel.IsMuted): c.IsMuted = v; break;
            case nameof(ClipModel.IsSolo): c.IsSolo = v; break;
        }
    }
}

internal static class UndoListExtensions
{
    /// <summary>Inserts at the recorded index, clamped to current bounds.</summary>
    public static void ClampInsert<T>(this int index, List<T> list, T item)
    {
        index = Math.Clamp(index, 0, list.Count);
        list.Insert(index, item);
    }
}
