using CommunityToolkit.Mvvm.ComponentModel;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.ViewModels;

public partial class TimelineViewModel
    : ObservableObject
{
    [ObservableProperty]
    private double currentTime;

    [ObservableProperty]
    private double zoom = 1.0;

    public TimelineModel Model { get; }

    public TimelineViewModel(
        TimelineModel model)
    {
        Model = model;
        currentTime = model.CurrentTime;
        zoom = model.Zoom;
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
}
