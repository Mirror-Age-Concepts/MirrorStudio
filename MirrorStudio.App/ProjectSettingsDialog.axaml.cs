using Avalonia.Controls;
using Avalonia.Interactivity;
using MirrorStudio.Core.Models;

namespace MirrorStudio.App
{
    public partial class ProjectSettingsDialog : Window
    {
        private readonly ProjectModel _project;

        public ProjectSettingsDialog(ProjectModel project)
        {
            InitializeComponent();
            _project = project;
            NameBox.Text = project.Name;
            WidthBox.Text = project.Width.ToString();
            HeightBox.Text = project.Height.ToString();
            AspectText.Text = project.AspectRatio;
            FpsCombo.SelectedIndex = FpsIndexFor(project.FrameRate);
            SampleRateCombo.SelectedIndex = project.AudioSampleRateHz switch
            {
                44100 => 0,
                96000 => 2,
                _ => 1
            };
            ExportFormatCombo.SelectedIndex = System.Math.Max(0, System.Array.FindIndex(
                new[] { "MP4 H.264", "MP4 H.265", "WebM VP9", "MOV ProRes" },
                f => f == project.DefaultExportFormat));

            WidthBox.TextChanged += (_, _) => UpdateAspect();
            HeightBox.TextChanged += (_, _) => UpdateAspect();
        }

        private static int FpsIndexFor(double fps) => fps switch
        {
            23.976 => 0,
            24 => 1,
            30 => 3,
            50 => 4,
            60 => 5,
            _ => 2
        };

        private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (WidthBox == null || HeightBox == null || PresetCombo.SelectedItem is not ComboBoxItem item) return;
            var parts = (item.Tag as string)?.Split('x');
            if (parts == null || parts.Length != 2) return;
            WidthBox.Text = parts[0];
            HeightBox.Text = parts[1];
            UpdateAspect();
        }

        private void UpdateAspect()
        {
            if (!int.TryParse(WidthBox.Text, out int w)) return;
            if (!int.TryParse(HeightBox.Text, out int h)) return;
            AspectText.Text = ProjectModel.ComputeAspectRatio(w, h);
        }

        private void OnApply(object? sender, RoutedEventArgs e)
        {
            if (int.TryParse(WidthBox.Text, out int w)) _project.Width = w;
            if (int.TryParse(HeightBox.Text, out int h)) _project.Height = h;
            _project.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _project.Name : NameBox.Text!.Trim();
            if (double.TryParse((FpsCombo.SelectedItem as ComboBoxItem)?.Content as string,
                System.Globalization.CultureInfo.InvariantCulture, out double fps))
            {
                _project.FrameRate = fps;
            }
            _project.AspectRatio = ProjectModel.ComputeAspectRatio(_project.Width, _project.Height);
            _project.AudioSampleRateHz = SampleRateCombo.SelectedIndex switch { 0 => 44100, 2 => 96000, _ => 48000 };
            _project.DefaultExportFormat = (ExportFormatCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "MP4 H.264";
            Close();
        }

        private void OnCancel(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
