using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;

namespace MirrorStudio.App.Workspaces
{
    public partial class ExportView : UserControl
    {
        private readonly SessionState _session;
        private bool _rendering;

        public ExportView(SessionState session)
        {
            InitializeComponent();
            _session = session;
            _session.ProjectChanged += OnProjectChanged;
            RefreshSources();
            UpdateOutputHint();
        }

        private void OnProjectChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshSources();
                UpdateOutputHint();
            });
        }

        private void RefreshSources()
        {
            var project = _session.CurrentProject;
            if (project == null)
            {
                SourceCombo.ItemsSource = new[] { "(no project loaded)" };
                SourceCombo.SelectedIndex = 0;
                return;
            }
            var sources = project.Media.Select(m => m.FileName).ToList();
            if (sources.Count == 0) sources.Add("(project has no media)");
            SourceCombo.ItemsSource = sources;
            SourceCombo.SelectedIndex = 0;
        }

        private string? _customOutputPath;

        private void OnPlatformSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (PresetsList == null || ResolutionCombo == null) return;
            var tag = (PlatformList.SelectedItem as ListBoxItem)?.Tag as string;
            SelectPresetByTag("mp4_h264");
            switch (tag)
            {
                case "youtube":
                    SelectResolution("1920x1080"); SelectFps("30"); QualitySlider.Value = 20;
                    break;
                case "instagram":
                    SelectResolution("1080x1080"); SelectFps("30"); QualitySlider.Value = 22;
                    break;
                case "tiktok":
                    SelectResolution("1080x1920"); SelectFps("30"); QualitySlider.Value = 22;
                    break;
                case "facebook":
                    SelectResolution("1280x720"); SelectFps("30"); QualitySlider.Value = 23;
                    break;
                default:
                    break; // Custom — leave as user set
            }
            UpdateOutputHint();
        }

        private void SelectPresetByTag(string tag)
        {
            for (int i = 0; i < PresetsList.Items.Count; i++)
                if (PresetsList.Items[i] is ListBoxItem it && (it.Tag as string) == tag)
                { PresetsList.SelectedIndex = i; return; }
        }

        private void SelectResolution(string tag)
        {
            for (int i = 0; i < ResolutionCombo.Items.Count; i++)
                if (ResolutionCombo.Items[i] is ComboBoxItem it && (it.Tag as string) == tag)
                { ResolutionCombo.SelectedIndex = i; return; }
        }

        private void SelectFps(string tag)
        {
            for (int i = 0; i < FpsCombo.Items.Count; i++)
                if (FpsCombo.Items[i] is ComboBoxItem it && (it.Tag as string) == tag)
                { FpsCombo.SelectedIndex = i; return; }
        }

        private bool IsSequenceMode => ModeCombo?.SelectedIndex == 0;

        private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (SourcePanel != null) SourcePanel.IsVisible = !IsSequenceMode;
        }

        private void OnRefreshSources(object? sender, RoutedEventArgs e)
        {
            RefreshSources();
        }

        private string DefaultOutputName()
        {
            var project = _session.CurrentProject;
            return string.IsNullOrWhiteSpace(OutputNameBox.Text)
                ? (project?.Name ?? "export")
                : OutputNameBox.Text!;
        }

        private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var ext = ExtensionForPreset();
            var dlg = new SaveFileDialog
            {
                Title = "Choose output file",
                InitialFileName = DefaultOutputName() + ext
            };
            var path = await dlg.ShowAsync(owner);
            if (!string.IsNullOrWhiteSpace(path))
            {
                _customOutputPath = path;
                OutputPathHint.Text = path;
            }
        }

        private void UpdateOutputHint()
        {
            var project = _session.CurrentProject;
            var name = string.IsNullOrWhiteSpace(OutputNameBox.Text) ? (project?.Name ?? "export") : OutputNameBox.Text!;
            OutputNameBox.Text = name;
            OutputPathHint.Text = project == null
                ? "Open a project to enable export."
                : System.IO.Path.Combine(_session.ProjectFolder, "Exports", name + ExtensionForPreset());
        }

        private static string ExtensionForTag(string tag) => tag switch
        {
            "mp4_h264" or "mp4_hevc" => ".mp4",
            "webm_vp9" => ".webm",
            "mov_prores" => ".mov",
            "png_seq" => "_%05d.png",
            "wav" => ".wav",
            "mp3" => ".mp3",
            _ => ".mp4"
        };

        private string ExtensionForPreset()
        {
            var item = PresetsList.SelectedItem as ListBoxItem;
            return ExtensionForTag(item?.Tag as string ?? "mp4_h264");
        }

        private void OnPresetSelected(object? sender, SelectionChangedEventArgs e)
        {
            UpdateOutputHint();
        }

        private async void OnExport(object? sender, RoutedEventArgs e)
        {
            if (_rendering) return;

            var owner = this.VisualRoot as Window;
            var project = _session.CurrentProject;

            if (owner == null || project == null || !_session.HasProject)
            {
                ExportStatusText.Text = "No project loaded.";
                return;
            }

            var exportsDir = System.IO.Path.Combine(_session.ProjectFolder, "Exports");
            System.IO.Directory.CreateDirectory(exportsDir);

            var presetItem = PresetsList.SelectedItem as ListBoxItem;
            var tag = presetItem?.Tag as string ?? "mp4_h264";
            int crf = (int)QualitySlider.Value;
            bool includeAudio = IncludeAudioChk?.IsChecked != false;

            string outputName = DefaultOutputName() + ExtensionForTag(tag);
            string outputPath = _customOutputPath ?? System.IO.Path.Combine(exportsDir, outputName);

            string args;
            if (IsSequenceMode)
            {
                var tl = _session.Timeline;
                if (tl == null || tl.Clips.Count == 0)
                {
                    ExportStatusText.Text = "Timeline is empty — add clips in the Video workspace.";
                    return;
                }
                if (!MirrorStudio.Video.SequenceExporter.TryBuild(
                        project, tl, _session.ResolvePath, tag, crf, includeAudio, outputPath,
                        out args, out var err))
                {
                    ExportStatusText.Text = err;
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(SourceCombo.SelectedItem as string) ||
                    (SourceCombo.SelectedItem as string)!.StartsWith("("))
                {
                    ExportStatusText.Text = "Import media into the project first (Video workspace).";
                    return;
                }

                var entry = project.Media.FirstOrDefault(m => m.FileName == (string)SourceCombo.SelectedItem);
                if (entry == null)
                {
                    ExportStatusText.Text = "Source media not found.";
                    return;
                }

                var sourcePath = System.IO.Path.Combine(_session.ProjectFolder, entry.RelativePath);
                args = BuildArgs(tag, sourcePath, outputPath);
            }

            _rendering = true;
            ExportBtn.IsEnabled = false;
            ExportProgress.IsIndeterminate = true;
            ExportProgress.Value = 0;
            ExportStatusText.Text = IsSequenceMode ? "Rendering sequence..." : "Rendering...";
            LogBox.Text = $"ffmpeg {args}\n\n";

            var ffmpeg = new MirrorStudio.Video.Services.FfmpegService(_session.Settings.FFmpegPath);
            if (!ffmpeg.IsAvailable())
            {
                ExportProgress.IsIndeterminate = false;
                ExportBtn.IsEnabled = true;
                _rendering = false;
                ExportStatusText.Text = "FFmpeg not found. Set its path in Home > Settings.";
                LogBox.Text += "[error] ffmpeg executable not available";
                return;
            }

            var result = await ffmpeg.RunAsync(args);

            _rendering = false;
            ExportProgress.IsIndeterminate = false;
            ExportProgress.Value = result.Success ? 100 : 0;
            ExportBtn.IsEnabled = true;

            var tail = result.Output.Length > 1500 ? result.Output[^1500..] : result.Output;
            LogBox.Text += tail;
            ExportStatusText.Text = result.Success ? $"Done: {outputPath}" : "Render failed - see log.";
        }

        private string BuildArgs(string tag, string source, string output)
        {
            int crf = (int)QualitySlider.Value;
            string scale = "";
            if (ResolutionCombo.SelectedItem is ComboBoxItem resItem && resItem.Tag as string != "source")
            {
                var parts = (resItem.Tag as string)!.Split('x');
                scale = $" -vf \"scale={parts[0]}:-2\"";
            }
            else if (_session.CurrentProject != null)
            {
                scale = $" -vf \"scale={_session.CurrentProject.Width}:-2\"";
            }

            double fps = 0;
            if (FpsCombo.SelectedItem is ComboBoxItem fpsItem && fpsItem.Tag as string != "source")
            {
                fps = double.Parse((fpsItem.Tag as string)!);
            }
            string fpsArg = fps > 0 ? $" -r {fps.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "";

            return tag switch
            {
                "mp4_h264" => $"-y -i \"{source}\" -c:v libx264 -preset medium -crf {crf}{scale}{fpsArg} -pix_fmt yuv420p -c:a aac -b:a 192k \"{output}\"",
                "mp4_hevc" => $"-y -i \"{source}\" -c:v libx265 -preset medium -crf {crf + 2}{scale}{fpsArg} -pix_fmt yuv420p -c:a aac -b:a 160k \"{output}\"",
                "webm_vp9" => $"-y -i \"{source}\" -c:v libvpx-vp9 -crf {crf} -b:v 0{scale}{fpsArg} -c:a libopus -b:a 128k \"{output}\"",
                "mov_prores" => $"-y -i \"{source}\" -c:v prores_ks -profile:v 3{scale}{fpsArg} -c:a pcm_s16le \"{output}\"",
                "png_seq" => $"-y -i \"{source}\"{scale}{fpsArg} \"{output}\"",
                "wav" => $"-y -i \"{source}\" -vn -c:a pcm_s16le \"{output}\"",
                "mp3" => $"-y -i \"{source}\" -vn -c:a libmp3lame -q:a 2 \"{output}\"",
                _ => $"-y -i \"{source}\" -c:v libx264 -crf {crf}{scale}{fpsArg} \"{output}\""
            };
        }
    }
}
