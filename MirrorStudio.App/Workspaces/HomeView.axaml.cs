using Avalonia.Controls;
using Avalonia.Interactivity;
using MirrorStudio.Project;

namespace MirrorStudio.App.Workspaces
{
    public class TemplateVm
    {
        public string Name { get; set; } = string.Empty;
        public string Specs { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
    }

    public partial class HomeView : UserControl
    {
        private readonly SessionState _session;
        private readonly RecentProjectsService _recents = new();
        private readonly AppSettingsService _settingsService = new();
        private readonly ProjectService _projectService = new();

        public System.Func<System.Threading.Tasks.Task>? OnNewProjectRequested { get; set; }
        public System.Func<System.Threading.Tasks.Task>? OnOpenProjectRequested { get; set; }
        public System.Action<string>? OnOpenRecentRequested { get; set; }
        public System.Action<WorkspaceKind>? OnNavigateRequested { get; set; }

        public HomeView(SessionState session)
        {
            InitializeComponent();
            _session = session;
            TemplatesList.ItemsSource = BuildTemplates();
            LoadSettingsIntoUi();
            RefreshRecents();
            _session.ProjectChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshRecents);
        }

        private static System.Collections.Generic.List<TemplateVm> BuildTemplates() => new()
        {
            new TemplateVm { Name = "Blank Project", Specs = "1920 x 1080 @ 25fps", Description = "Empty Full HD project.", Width = 1920, Height = 1080, FrameRate = 25 },
            new TemplateVm { Name = "YouTube Video", Specs = "1920 x 1080 @ 30fps", Description = "Standard landscape video for YouTube.", Width = 1920, Height = 1080, FrameRate = 30 },
            new TemplateVm { Name = "Shorts / Reels / TikTok", Specs = "1080 x 1920 @ 30fps", Description = "Vertical format for social platforms.", Width = 1080, Height = 1920, FrameRate = 30 },
            new TemplateVm { Name = "Cinema Film", Specs = "2048 x 858 @ 24fps", Description = "DCI 2K scope aspect for film work.", Width = 2048, Height = 858, FrameRate = 24 },
            new TemplateVm { Name = "Instagram Square", Specs = "1080 x 1080 @ 30fps", Description = "Square post format.", Width = 1080, Height = 1080, FrameRate = 30 },
            new TemplateVm { Name = "Podcast / Audio", Specs = "1280 x 720 @ 25fps", Description = "Lightweight project focused on audio tracks.", Width = 1280, Height = 720, FrameRate = 25 }
        };

        public void RefreshRecents()
        {
            _recents.PurgeMissing();
            RecentsList.ItemsSource = _recents.Load().ToList();
        }

        private void LoadSettingsIntoUi()
        {
            FfmpegPathBox.Text = _session.Settings.FFmpegPath == "ffmpeg" ? "" : _session.Settings.FFmpegPath;
            FfprobePathBox.Text = _session.Settings.FFprobePath == "ffprobe" ? "" : _session.Settings.FFprobePath;
            AutoSaveCheck.IsChecked = _session.Settings.AutoSave;
            HardwareAccelCheck.IsChecked = _session.Settings.HardwareAcceleration;
        }

        private async void OnNewProjectClick(object? sender, RoutedEventArgs e)
        {
            if (OnNewProjectRequested != null) await OnNewProjectRequested();
        }

        private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
        {
            if (OnOpenProjectRequested != null) await OnOpenProjectRequested();
        }

        private void OnOpenRecentClick(object? sender, RoutedEventArgs e)
        {
            if (RecentsList.SelectedItem is string path && !string.IsNullOrWhiteSpace(path))
                OnOpenRecentRequested?.Invoke(path);
        }

        private void OnRemoveRecentClick(object? sender, RoutedEventArgs e)
        {
            if (RecentsList.SelectedItem is string path && !string.IsNullOrWhiteSpace(path))
            {
                _recents.Remove(path);
                RefreshRecents();
            }
        }

        private async void OnTemplateClick(object? sender, RoutedEventArgs e)
        {
            var owner = this.VisualRoot as Window;
            var btn = sender as Button;
            var template = btn?.DataContext as TemplateVm;
            if (owner == null || template == null) return;

            var dlg = new OpenFolderDialog
            {
                Title = $"Choose folder for '{template.Name}'"
            };
            var folder = await dlg.ShowAsync(owner);
            if (string.IsNullOrWhiteSpace(folder)) return;

            folder = System.IO.Path.Combine(folder, SanitizeName(template.Name));
            try
            {
                var project = _projectService.Create(template.Name, template.Width, template.Height, template.FrameRate);
                var ok = await _projectService.CreateProjectAsync(project, folder);
                if (ok)
                {
                    _session.SetProject(project, folder);
                    _recents.Add(System.IO.Path.Combine(folder, "project.json"));
                    OnNavigateRequested?.Invoke(WorkspaceKind.Video);
                }
                else
                {
                    await MessageBox.Show(owner, "Template error", "Could not create the project folder.");
                }
            }
            catch (System.Exception ex)
            {
                await MessageBox.Show(owner, "Template error", ex.Message);
            }
        }

        private static string SanitizeName(string name)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(" ", "");
        }

        private void OnSaveSettings(object? sender, RoutedEventArgs e)
        {
            var settings = _session.Settings;
            settings.FFmpegPath = string.IsNullOrWhiteSpace(FfmpegPathBox.Text) ? "ffmpeg" : FfmpegPathBox.Text!.Trim();
            settings.FFprobePath = string.IsNullOrWhiteSpace(FfprobePathBox.Text) ? "ffprobe" : FfprobePathBox.Text!.Trim();
            settings.AutoSave = AutoSaveCheck.IsChecked == true;
            settings.HardwareAcceleration = HardwareAccelCheck.IsChecked == true;
            _settingsService.Save(settings);
            _session.SetSettings(settings);
            SettingsStatusText.Text = "Settings saved to %AppData%\\MirrorStudio\\settings.json";
        }

        private void OnDetectMediaEngine(object? sender, RoutedEventArgs e)
        {
            var ffmpeg = MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg("ffmpeg");
            var ffprobe = MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfprobe("ffprobe");

            var ffmpegOk = new MirrorStudio.Video.Services.FfmpegService(ffmpeg).IsAvailable();

            FfmpegPathBox.Text = ffmpeg == "ffmpeg" ? "" : ffmpeg;
            FfprobePathBox.Text = ffprobe == "ffprobe" ? "" : ffprobe;

            SettingsStatusText.Text = ffmpegOk
                ? $"Media engine found:\n  ffmpeg: {ffmpeg}\n  ffprobe: {ffprobe}\nPress Save Settings to keep."
                : "FFmpeg not found on this machine.\nInstall FFmpeg (winget install Gyan.FFmpeg) or set the full path above.";
        }
    }
}
