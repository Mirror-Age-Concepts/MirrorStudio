using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace MirrorStudio.App.Workspaces
{
    public partial class MotionStudioView : UserControl
    {
        private readonly SessionState _session;
        private readonly ObservableCollection<string> _elements = new();
        private readonly ObservableCollection<string> _keyframes = new();
        private int _titleCounter;
        private int _shapeCounter;
        private int _imageElementCounter;
        private bool _playing;
        private bool _hasTitle;
        private string _titleText = "MIRROR STUDIO";

        public MotionStudioView(SessionState session)
        {
            InitializeComponent();
            _session = session;
            ElementsList.ItemsSource = _elements;
            KeyframesList.ItemsSource = _keyframes;
            ElementsList.SelectionChanged += OnElementSelectionChanged;
            _session.ProjectChanged += OnProjectChanged;
        }

        private void OnProjectChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_session.CurrentProject != null)
                {
                    StageText.Text = $"MOTION STAGE\n{_session.CurrentProject.Width} x {_session.CurrentProject.Height}";
                }
            });
        }

        private void OnElementSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var selected = ElementsList.SelectedItem as string ?? "";
            _hasTitle = selected.StartsWith("Title");
            StageOverlayText.Text = _hasTitle ? _titleText : "";
        }

        private void OnAddTitle(object? sender, RoutedEventArgs e)
        {
            _titleText = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? "MIRROR STUDIO" : TitleTextBox.Text!;
            _titleCounter++;
            var name = $"Title {_titleCounter}: {_titleText}";
            _elements.Add(name);
            ElementsList.SelectedIndex = _elements.Count - 1;
            _hasTitle = true;
            StageOverlayText.Text = _titleText;
        }

        private async void OnTypewriter(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text)) return;
            _titleText = TitleTextBox.Text;
            StageOverlayText.Text = "";
            for (int i = 1; i <= _titleText.Length; i++)
            {
                StageOverlayText.Text = _titleText.Substring(0, i);
                await System.Threading.Tasks.Task.Delay(60);
            }
        }

        private void OnAddShape(object? sender, RoutedEventArgs e)
        {
            _shapeCounter++;
            _elements.Add($"Shape {_shapeCounter}");
            ElementsList.SelectedIndex = _elements.Count - 1;
        }

        private void OnAddImageElement(object? sender, RoutedEventArgs e)
        {
            _imageElementCounter++;
            _elements.Add($"Image Element {_imageElementCounter}");
            ElementsList.SelectedIndex = _elements.Count - 1;
        }

        private void OnPlayheadChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (PlayheadText == null || PlayheadSlider == null) return;
            PlayheadText.Text = $"{PlayheadSlider.Value:F2} s / {PlayheadSlider.Maximum:F2} s";
        }

        private async void RunPreset(string preset)
        {
            if (_playing) return;
            _playing = true;
            const double duration = 1.0;
            const double step = 0.033;
            double t = 0;

            var timer = new Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(step * 1000) };
            timer.Tick += (s, args) =>
            {
                t += step;
                double p = System.Math.Min(t / duration, 1.0);
                switch (preset)
                {
                    case "FadeIn":
                        StageOverlayText.Opacity = p;
                        break;
                    case "SlideUp":
                        StageOverlayText.Margin = new Avalonia.Thickness(20, 20 + 80 * (1 - Ease(p)), 20, 0);
                        break;
                    case "ZoomIn":
                        StageOverlayText.FontSize = 16 + 16 * Ease(p);
                        break;
                    case "Bounce":
                        double b = System.Math.Abs(System.Math.Sin(p * System.Math.PI * 3)) * (1 - p) * 40;
                        StageOverlayText.Margin = new Avalonia.Thickness(20, 20 + b, 20, 0);
                        break;
                }

                if (p >= 1.0 || !_playing)
                {
                    timer.Stop();
                    _playing = false;
                    if (!_playing && preset != "FadeIn") StageOverlayText.Opacity = 1;
                }
            };
            if (preset == "FadeIn") StageOverlayText.Opacity = 0;
            timer.Start();
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private static double Ease(double p) => 1 - (1 - p) * (1 - p);

        private void OnPresetFadeIn(object? sender, RoutedEventArgs e) => RunPreset("FadeIn");
        private void OnPresetSlideUp(object? sender, RoutedEventArgs e) => RunPreset("SlideUp");
        private void OnPresetZoomIn(object? sender, RoutedEventArgs e) => RunPreset("ZoomIn");
        private void OnPresetBounce(object? sender, RoutedEventArgs e) => RunPreset("Bounce");

        private async void OnPlayAnimation(object? sender, RoutedEventArgs e)
        {
            if (_playing) return;
            _playing = true;
            PlayheadSlider.Value = 0;
            StageOverlayText.Opacity = 1;

            var timer = new Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(33) };
            timer.Tick += (s, args) =>
            {
                PlayheadSlider.Value += 0.033;
                if (!_playing || PlayheadSlider.Value >= PlayheadSlider.Maximum)
                {
                    timer.Stop();
                    _playing = false;
                }
                else
                {
                    double t = PlayheadSlider.Value / PlayheadSlider.Maximum;
                    if (_hasTitle) StageOverlayText.Opacity = 1.0 - t * 0.6;
                }
            };
            timer.Start();
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void OnStopAnimation(object? sender, RoutedEventArgs e)
        {
            _playing = false;
            PlayheadSlider.Value = 0;
            StageOverlayText.Opacity = 1;
            StageOverlayText.FontSize = 32;
            StageOverlayText.Margin = new Avalonia.Thickness(20, 20, 20, 0);
        }

        private void OnAddKeyframe(object? sender, RoutedEventArgs e)
        {
            var element = ElementsList.SelectedItem as string ?? "No element";
            var kf = $"{element} @ {PlayheadSlider.Value:F2}s";
            _keyframes.Add(kf);
            KeyframesList.SelectedIndex = _keyframes.Count - 1;
        }

        private void OnClearKeys(object? sender, RoutedEventArgs e)
        {
            _keyframes.Clear();
        }
    }
}
