using CommunityToolkit.Mvvm.ComponentModel;

namespace MirrorStudio.App.ViewModels;

public partial class MainWindowViewModel
    : ObservableObject
{
    [ObservableProperty]
    private double currentTime;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string timeDisplay =
        "00:00:00:00";

    public void UpdateTime(
        double seconds,
        double frameRate = 25)
    {
        CurrentTime = seconds;

        var fps = Math.Max(1.0, frameRate);

        var totalFrames =
            (int)Math.Round(seconds * fps);

        var frames =
            totalFrames %
            (int)fps;

        var totalSeconds =
            totalFrames /
            (int)fps;

        var secondsPart =
            totalSeconds % 60;

        var minutes =
            totalSeconds / 60;

        var minutesPart =
            minutes % 60;

        var hours =
            minutes / 60;

        TimeDisplay =
            $"{hours:00}:" +
            $"{minutesPart:00}:" +
            $"{secondsPart:00}:" +
            $"{frames:00}";
    }
}
