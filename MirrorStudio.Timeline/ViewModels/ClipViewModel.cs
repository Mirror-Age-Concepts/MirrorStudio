using CommunityToolkit.Mvvm.ComponentModel;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Timeline.ViewModels;

public partial class ClipViewModel : ObservableObject
{
    public TimelineClip Model { get; }

    [ObservableProperty]
    private bool isSelected;

    /// <summary>Pixels per second at current timeline zoom (set by the view).</summary>
    private double _pixelsPerSecond = 30.0;

    public ClipViewModel(TimelineClip model)
    {
        Model = model;
    }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public double Start => Model.Start;

    public double Duration => Model.Duration;

    public double Left => Model.Start * _pixelsPerSecond;

    public double Width => Math.Max(4, Model.Duration * _pixelsPerSecond);

    public void SetPixelsPerSecond(double pps)
    {
        if (Math.Abs(_pixelsPerSecond - pps) < 0.001) return;
        _pixelsPerSecond = pps;
        Refresh();
    }

    /// <summary>Re-notify computed properties after the model mutates.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Start));
        OnPropertyChanged(nameof(Duration));
    }
}
