using CommunityToolkit.Mvvm.ComponentModel;
using MirrorStudio.Core.Commands;
using MirrorStudio.Timeline.Commands;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.ViewModels;

public partial class TimelineViewModel
    : ObservableObject
{
    [ObservableProperty]
    private double currentTime;

    [ObservableProperty]
    private double zoom = 1.0;

    /// <summary>The clip the user clicked — drives the Inspector (spec M7 §9).</summary>
    [ObservableProperty]
    private TimelineClip? selectedClip;

    /// <summary>Ripple editing mode (spec M7 §12): delete closes gaps on the same track.</summary>
    [ObservableProperty]
    private bool rippleMode;

    public CommandManager Commands { get; } = new();

    public TimelineModel Model { get; }

    public bool CanUndo => Commands.CanUndo;

    public bool CanRedo => Commands.CanRedo;

    public TimelineViewModel(
        TimelineModel model)
    {
        Model = model;
        currentTime = model.CurrentTime;
        zoom = model.Zoom;

        Commands.HistoryChanged += () =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            RefreshDuration();
        };
    }

    partial void OnCurrentTimeChanged(
        double value)
    {
        Model.CurrentTime = value;
    }

    partial void OnZoomChanged(
        double value)
    {
        Model.Zoom = value;
    }

    // ---------- editing operations ----------

    /// <summary>Move the selected clip to a new start time (undoable, spec M7 §2/§10).</summary>
    public void MoveSelectedClip(
        double newStart)
    {
        if (SelectedClip == null)
            return;

        var track =
            TrackOf(SelectedClip);

        if (track == null || track.IsLocked)
            return;

        Commands.Execute(
            new MoveClipCommand(
                SelectedClip,
                newStart));
    }

    /// <summary>Delete the selected clip; ripple mode closes the gap (spec M7 §3/§12).</summary>
    public void DeleteSelectedClip()
    {
        if (SelectedClip == null)
            return;

        var track =
            TrackOf(SelectedClip);

        if (track == null || track.IsLocked)
            return;

        IEditCommand command =
            RippleMode
                ? new RippleDeleteCommand(track, SelectedClip)
                : new DeleteClipCommand(track, SelectedClip);

        Commands.Execute(command);

        SelectedClip = null;
    }

    /// <summary>Split the selected clip at the playhead (spec M7 §8).</summary>
    public void SplitSelectedClip()
    {
        if (SelectedClip == null)
            return;

        var track =
            TrackOf(SelectedClip);

        if (track == null || track.IsLocked)
            return;

        var command =
            new SplitClipCommand(
                track,
                SelectedClip,
                Model.CurrentTime);

        Commands.Execute(command);

        // focus follows the right half, like professional editors
        if (command.Right != null)
            SelectedClip = command.Right;
    }

    /// <summary>Trim the selected clip (undoable, spec M7 §4/§11).</summary>
    public void TrimSelectedClip(
        double newStart,
        double newDuration)
    {
        if (SelectedClip == null)
            return;

        var track =
            TrackOf(SelectedClip);

        if (track == null || track.IsLocked)
            return;

        Commands.Execute(
            new TrimClipCommand(
                SelectedClip,
                newStart,
                newDuration));
    }

    /// <summary>Add a clip to a track (undoable).</summary>
    public void AddClip(
        int trackIndex,
        TimelineClip clip)
    {
        if (trackIndex < 0 || trackIndex >= Model.Tracks.Count)
            return;

        Commands.Execute(
            new AddClipCommand(
                Model.Tracks[trackIndex],
                clip));
    }

    // ---------- undo / redo ----------

    public void Undo() => Commands.Undo();

    public void Redo() => Commands.Redo();

    // ---------- helpers ----------

    /// <summary>Track containing the given clip, or null.</summary>
    public TimelineTrack? TrackOf(TimelineClip clip) =>
        Model.Tracks.FirstOrDefault(
            x => x.Clips.Contains(clip));

    /// <summary>Keeps Model.Duration in sync after structural edits.</summary>
    public void RefreshDuration() =>
        Model.Duration = Model.Tracks
            .SelectMany(t => t.Clips)
            .Select(c => c.Start + c.Duration)
            .DefaultIfEmpty(0)
            .Max();
}
