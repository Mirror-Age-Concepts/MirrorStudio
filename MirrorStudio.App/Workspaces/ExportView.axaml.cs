using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Models;
using MirrorStudio.Export.Services;
using System.Linq;

namespace MirrorStudio.App.Workspaces
{
    public partial class ExportView : UserControl
    {
        private readonly SessionState _session;
        private bool _rendering;
        private string? _customOutputPath;
        private readonly ExportQueueService _queue;
        private readonly FfmpegRenderEngine _engine;

        public ExportView(SessionState session)
        {
            // must precede InitializeComponent: the XAML's initial SelectedIndex
            // assignments raise SelectionChanged during population
            _session = session;
            InitializeComponent();
            _session.ProjectChanged += OnProjectChanged;

            var ffmpeg = MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg(_session.Settings.FFmpegPath);
            _engine = new FfmpegRenderEngine(
                new FfmpegProcessRunner(ffmpeg),
                new FilterGraphBuilder(),
                new HardwareEncoderDetector(ffmpeg));
            _queue = new ExportQueueService(new RenderEngine(_engine));
            _queue.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshQueue);

            RefreshSources();
            RefreshQueue();
            UpdateOutputHint();
            UpdateSizeEstimate();
        }

        private void OnProjectChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshSources();
                UpdateOutputHint();
                UpdateSizeEstimate();
            });
        }

        // ---------- queue ----------

        private void RefreshQueue()
        {
            var jobs = _queue.Snapshot();
            QueueList.ItemsSource = jobs.Select(j =>
                $"{StateGlyph(j.State)} {j.Name}  {Math.Round(j.Percentage)}%  {j.StatusText}").ToList();

            var active = jobs.FirstOrDefault(j => j.State == ExportJobState.Rendering);
            if (active != null)
            {
                ExportProgress.IsIndeterminate = false;
                ExportProgress.Value = active.Percentage;
                ProgressText.Text = $"Rendering {active.Name}...";
                TimeText.Text = active.StatusText;
                CancelBtn.IsEnabled = true;
                _rendering = true;
                ExportBtn.IsEnabled = false;
            }
            else
            {
                var last = jobs.LastOrDefault();
                CancelBtn.IsEnabled = false;
                ExportBtn.IsEnabled = true;
                _rendering = last is { State: ExportJobState.Waiting };

                switch (last?.State)
                {
                    case ExportJobState.Completed:
                        ExportProgress.Value = 100;
                        ExportStatusText.Text = "Done.";
                        ProgressText.Text = "Done";
                        TimeText.Text = "";
                        break;
                    case ExportJobState.Failed:
                        ExportProgress.Value = 0;
                        ExportStatusText.Text = "Render failed - see log.";
                        ProgressText.Text = "Failed";
                        break;
                    case ExportJobState.Cancelled:
                        ExportStatusText.Text = "Cancelled.";
                        ProgressText.Text = "Cancelled";
                        break;
                    case ExportJobState.Waiting:
                        ProgressText.Text = "Queued...";
                        break;
                }
            }
        }

        private static string StateGlyph(ExportJobState s) => s switch
        {
            ExportJobState.Rendering => "▶",
            ExportJobState.Completed => "✔",
            ExportJobState.Failed => "✖",
            ExportJobState.Cancelled => "⊘",
            _ => "…"
        };

        private void OnCancelRender(object? sender, RoutedEventArgs e)
        {
            var job = _queue.Snapshot().FirstOrDefault(j =>
                j.State is ExportJobState.Rendering or ExportJobState.Waiting);
            if (job != null) _queue.Cancel(job.Id);
        }

        private void OnClearFinished(object? sender, RoutedEventArgs e) => _queue.ClearFinished();

        private void OnQueueDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            // reserved: inspect job details
        }

        // ---------- sources ----------

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

        // ---------- presets ----------

        private void OnPlatformSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (PresetsList == null || ResolutionCombo == null) return;
            var tag = (PlatformList.SelectedItem as ListBoxItem)?.Tag as string;
            SelectPresetByTag("mp4_h264");
            switch (tag)
            {
                case "youtube":
                    SelectResolution("1920x1080"); SelectFps("30"); QualitySlider.Value = 18;
                    break;
                case "youtube4k":
                    SelectResolution("3840x2160"); SelectFps("30"); QualitySlider.Value = 20; PresetTagOverride = "mp4_hevc";
                    break;
                case "instagram":
                    SelectResolution("1080x1080"); SelectFps("30"); QualitySlider.Value = 20;
                    break;
                case "tiktok":
                    SelectResolution("1080x1920"); SelectFps("30"); QualitySlider.Value = 20;
                    break;
                case "film":
                    SelectResolution("3840x2160"); SelectFps("24"); QualitySlider.Value = 12; PresetTagOverride = "mp4_h264";
                    break;
                default:
                    break; // Custom — leave as user set
            }
            UpdateOutputHint();
            UpdateSizeEstimate();
        }

        /// <summary>Preset-forced format (YouTube 4K → HEVC).</summary>
        private string? PresetTagOverride;

        private void OnPresetSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (PresetsList == null) return;
            UpdateOutputHint();
            UpdateSizeEstimate();
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

        // ---------- mode / settings ----------

        private bool IsSequenceMode => ModeCombo?.SelectedIndex == 0;

        private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (SourcePanel != null) SourcePanel.IsVisible = !IsSequenceMode;
        }

        private void OnSettingChanged(object? sender, SelectionChangedEventArgs e) => UpdateSizeEstimate();

        private void OnQualityChanged(object? sender, RangeBaseValueChangedEventArgs e) => UpdateSizeEstimate();

        private void OnRefreshSources(object? sender, RoutedEventArgs e)
        {
            RefreshSources();
        }

        // ---------- output path / size ----------

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
            if (OutputNameBox == null || OutputPathHint == null) return;

            var project = _session.CurrentProject;
            var name = string.IsNullOrWhiteSpace(OutputNameBox.Text) ? (project?.Name ?? "export") : OutputNameBox.Text!;
            OutputNameBox.Text = name;
            OutputPathHint.Text = project == null
                ? "Open a project to enable export."
                : _customOutputPath ?? System.IO.Path.Combine(_session.ProjectFolder, "Exports", name + ExtensionForPreset());
        }

        private double EstimatedDurationSeconds()
        {
            var project = _session?.CurrentProject;
            var tl = project?.Timeline;
            if (tl == null || tl.Clips.Count == 0) return 0;
            if (!IsSequenceMode)
            {
                var selected = SourceCombo?.SelectedItem as string;
                var entry = project!.Media.FirstOrDefault(m => m.FileName == selected);
                return entry?.DurationSec ?? 0;
            }
            return tl.Clips.Max(c => c.StartTime + c.Duration);
        }

        private void UpdateSizeEstimate()
        {
            if (SizeHint == null) return;

            double seconds = EstimatedDurationSeconds();
            if (seconds <= 0) { SizeHint.Text = ""; return; }

            int crf = QualitySlider == null ? 20 : (int)QualitySlider.Value;
            double mbps = crf switch { <= 16 => 24, <= 20 => 14, <= 24 => 8, _ => 4 };
            if (AudioOnlyChk?.IsChecked == true) mbps = 0.2;

            double gb = seconds * mbps / 8192.0;
            SizeHint.Text = $"Estimated size: {(gb >= 1 ? $"{gb:0.0} GB" : $"{gb * 1024:0} MB")} ({System.TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss})";
        }

        // ---------- export ----------

        private static string ExtensionForTag(string tag) => tag switch
        {
            "mp4_h264" or "mp4_hevc" => ".mp4",
            "wav" => ".wav",
            "mp3" => ".mp3",
            _ => ".mp4"
        };

        private string ExtensionForPreset() => ExtensionForTag(PresetTagOverride ?? FormatTag());

        private string FormatTag() =>
            (PresetsList.SelectedItem as ListBoxItem)?.Tag as string ?? "mp4_h264";

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

            if (IsSequenceMode && _session.Timeline is not { Clips.Count: > 0 })
            {
                ExportStatusText.Text = "Timeline is empty — add clips in the Video workspace.";
                return;
            }

            var ffmpeg = MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg(_session.Settings.FFmpegPath);
            if (!new MirrorStudio.Video.Services.FfmpegService(_session.Settings.FFmpegPath).IsAvailable())
            {
                ExportStatusText.Text = "FFmpeg not found. Set its path in Home > Settings.";
                return;
            }

            var tag = PresetTagOverride ?? FormatTag();
            int crf = (int)QualitySlider.Value;
            bool includeAudio = IncludeAudioChk?.IsChecked != false;

            string outputPath = _customOutputPath
                ?? System.IO.Path.Combine(_session.ProjectFolder, "Exports",
                     DefaultOutputName() + ExtensionForTag(tag));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath)!);

            if (!IsSequenceMode)
            {
                await LegacySingleFileExportAsync(project, tag, crf, includeAudio, outputPath);
                return;
            }

            // ---------- real timeline render via the export engine ----------
            var canonical = project.ToProject(_session.ResolvePath);

            var settings = BuildExportSettings(tag, crf, includeAudio, outputPath);

            LogBox.Text = "";
            AppendLog($"[queue] {settings.OutputPath}");
            AppendLog($"[preset] {settings.PresetName}; hw={settings.UseHardwareEncoding}");

            var planPreview = new FilterGraphBuilder().Build(canonical, settings);
            foreach (var off in planPreview.OfflineMedia.Distinct())
                AppendLog($"[warn] offline media substituted with placeholder: {off}");

            var job = _queue.Enqueue(canonical, settings);
            _rendering = true;
            ExportProgress.IsIndeterminate = false;
            ExportProgress.Value = 0;
            ExportStatusText.Text = "Queued: rendering timeline sequence...";

            // transparency: log the exact ffmpeg command the engine will run
            var argsPreview = _engine.BuildArguments(settings, planPreview);
            AppendLog("[cmd] ffmpeg " + argsPreview);
        }

        private ExportSettings BuildExportSettings(string tag, int crf, bool includeAudio, string outputPath)
        {
            var project = _session.CurrentProject!;
            var resItem = ResolutionCombo.SelectedItem as ComboBoxItem;
            var fpsItem = FpsCombo.SelectedItem as ComboBoxItem;

            int w = project.Width, h = project.Height;
            if (resItem?.Tag as string is { } res && res != "source")
            {
                var parts = res.Split('x');
                w = int.Parse(parts[0]); h = int.Parse(parts[1]);
            }

            double fps = project.FrameRate <= 0 ? 25 : project.FrameRate;
            if (fpsItem?.Tag as string is { } f && f != "source")
                fps = double.Parse(f);

            bool audioOnly = tag is "wav" or "mp3" || AudioOnlyChk?.IsChecked == true;
            string audioCodec = tag == "mp3" ? "libmp3lame" : "aac";
            if (tag == "wav") audioCodec = "pcm_s16le";

            // duck music under A1 dialogue when requested (spec M9 §22)
            var ducking = new MirrorStudio.Audio.Models.DuckingSettings
            {
                Enabled = DuckingChk?.IsChecked == true && !audioOnly,
                KeyTrackIndexes = [0],
                AmountDb = -12,
                AttackSec = 0.15,
                ReleaseSec = 0.50,
            };

            return new ExportSettings
            {
                OutputPath = outputPath,
                Format = tag.StartsWith("mp4") ? "mp4" : tag,
                VideoCodec = tag == "mp4_hevc" ? "libx265" : "libx264",
                AudioCodec = audioCodec,
                Crf = crf,
                VideoBitrate = crf <= 16 ? 24000 : crf <= 20 ? 14000 : 8000,
                AudioBitrate = 192,
                Width = w,
                Height = h,
                FrameRate = fps,
                UseHardwareEncoding = HwChk?.IsChecked == true,
                ExportAudioOnly = audioOnly || !includeAudio,
                Ducking = ducking,
            };
        }

        // ---------- legacy single-file path (unchanged behaviour) ----------

        private async Task LegacySingleFileExportAsync(
            MirrorStudio.Core.Models.ProjectModel project, string tag, int crf, bool includeAudio, string outputPath)
        {
            var entry = project.Media.FirstOrDefault(m => m.FileName == (string)SourceCombo.SelectedItem);
            if (entry == null)
            {
                ExportStatusText.Text = "Source media not found.";
                return;
            }

            var sourcePath = System.IO.Path.Combine(_session.ProjectFolder, entry.RelativePath);
            string args = BuildLegacyArgs(tag, sourcePath, outputPath);

            _rendering = true;
            ExportBtn.IsEnabled = false;
            ExportProgress.IsIndeterminate = false;
            ExportProgress.Value = 0;
            ExportStatusText.Text = "Rendering...";
            LogBox.Text = $"ffmpeg {args}\n\n";

            var ffmpeg = new MirrorStudio.Video.Services.FfmpegService(_session.Settings.FFmpegPath);
            var result = await ffmpeg.RunAsync(args);

            _rendering = false;
            ExportProgress.IsIndeterminate = false;
            ExportProgress.Value = result.Success ? 100 : 0;
            ExportBtn.IsEnabled = true;

            var tail = result.Output.Length > 1500 ? result.Output[^1500..] : result.Output;
            LogBox.Text += tail;
            ExportStatusText.Text = result.Success ? $"Done: {outputPath}" : "Render failed - see log.";
        }

        private string BuildLegacyArgs(string tag, string source, string output)
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
                "wav" => $"-y -i \"{source}\" -vn -c:a pcm_s16le \"{output}\"",
                "mp3" => $"-y -i \"{source}\" -vn -c:a libmp3lame -q:a 2 \"{output}\"",
                _ => $"-y -i \"{source}\" -c:v libx264 -crf {crf}{scale}{fpsArg} \"{output}\""
            };
        }

        private void AppendLog(string line)
        {
            LogBox.Text = LogBox.Text.Length > 20000 ? line + "\n" : LogBox.Text + line + "\n";
        }
    }
}
