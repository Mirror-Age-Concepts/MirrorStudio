using CommunityToolkit.Mvvm.ComponentModel;
using MirrorStudio.Core.Enums;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.ViewModels;

public partial class TrackViewModel : ObservableObject
{
    public TimelineTrack Model { get; }

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isLocked;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private bool isSolo;

    public string Name => Model.Name;

    public TrackType Type => Model.Type;

    public int Index => Model.Index;

    public IReadOnlyList<ClipViewModel> Clips { get; }

    public TrackViewModel(TimelineTrack model)
    {
        Model = model;
        isLocked = model.IsLocked;
        isMuted = model.IsMuted;
        isSolo = model.IsSolo;
        Clips = model.Clips.Select(c => new ClipViewModel(c)).ToList();
    }

    partial void OnIsLockedChanged(bool value) => Model.IsLocked = value;

    partial void OnIsMutedChanged(bool value) => Model.IsMuted = value;

    partial void OnIsSoloChanged(bool value) => Model.IsSolo = value;

    public ClipViewModel AddClip(TimelineClip clip)
    {
        Model.Clips.Add(clip);
        var vm = new ClipViewModel(clip);
        ((List<ClipViewModel>)Clips).Add(vm);
        return vm;
    }

    /// <summary>Refresh geometry-dependent bindings after external edits.</summary>
    public void RefreshClips()
    {
        foreach (var c in Clips)
            c.Refresh();
    }
}
