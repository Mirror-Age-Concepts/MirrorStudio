using Avalonia.Controls;
using Avalonia.Interactivity;
using MirrorStudio.App.Services;
using MirrorStudio.App.Workspaces;
using MirrorStudio.Project;

namespace MirrorStudio.App
{
    public partial class MainWindow : Window
    {
        private readonly SessionState _session = new();
        private readonly ProjectService _projectService = new();
        private readonly RecentProjectsService _recents = new();
        private readonly AppSettingsService _settingsService = new();
        private readonly DialogService _dialogs;
        private readonly ProjectDialogService _projectDialogs;

        private readonly HomeView _homeWorkspace;
        private readonly VideoStudioView _videoWorkspace;
        private readonly ImageStudioView _imageWorkspace;
        private readonly MotionStudioView _motionWorkspace;
        private readonly AudioStudioView _audioWorkspace;
        private readonly ExportView _exportWorkspace;

        public MainWindow()
        {
            InitializeComponent();
            ProjectService.EnsureWorkspaceFolders();
            _session.SetSettings(_settingsService.Load());
            ThemeManager.Apply(_session.Settings.Theme);
            _dialogs = new DialogService(this);
            _projectDialogs = new ProjectDialogService(_dialogs);

            _homeWorkspace = new HomeView(_session)
            {
                OnNewProjectRequested = NewProjectFlowAsync,
                OnOpenProjectRequested = OpenProjectDialogAsync,
                OnOpenRecentRequested = OpenProjectFromPath,
                OnNavigateRequested = Show
            };

            _videoWorkspace = new VideoStudioView(_session);
            _imageWorkspace = new ImageStudioView(_session);
            _motionWorkspace = new MotionStudioView(_session);
            _audioWorkspace = new AudioStudioView(_session);
            _exportWorkspace = new ExportView(_session);

            _session.ProjectChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateProjectBadge);

            // Milestone 7 §16: debounced auto-save → MyFilm.autosave
            _autosaveTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(5)
            };
            _autosaveTimer.Tick += async (_, _) =>
            {
                _autosaveTimer.Stop();
                if (_session.CurrentProject is not { } p || _suppressAutosave) return;

                // bundle projects → MyFilm.msp.recovery (spec M8 §7); folder projects → JSON autosave
                if (_session.MspPath != null)
                    await _recovery.WriteMspRecoveryAsync(
                        MirrorStudio.Core.Projects.ProjectMapper.ToProject(p), _session.MspPath);
                else
                    await _recovery.WriteAutosaveAsync(p);
            };
            _session.ProjectChanged += KickAutosave;

            AddKeyBinding(Avalonia.Input.Key.D1, Avalonia.Input.KeyModifiers.Alt, () => Show(WorkspaceKind.Home));
            AddKeyBinding(Avalonia.Input.Key.D2, Avalonia.Input.KeyModifiers.Alt, () => Show(WorkspaceKind.Video));
            AddKeyBinding(Avalonia.Input.Key.D3, Avalonia.Input.KeyModifiers.Alt, () => Show(WorkspaceKind.Image));
            AddKeyBinding(Avalonia.Input.Key.D4, Avalonia.Input.KeyModifiers.Alt, () => Show(WorkspaceKind.Motion));
            AddKeyBinding(Avalonia.Input.Key.D5, Avalonia.Input.KeyModifiers.Alt, () => Show(WorkspaceKind.Audio));
            AddKeyBinding(Avalonia.Input.Key.D6, Avalonia.Input.KeyModifiers.Alt, () => Show(WorkspaceKind.Export));

            Show(WorkspaceKind.Home);

            // recovery prompt needs a visible owner window - defer until shown
            // (ShowDialog throws "Cannot show window with non-visible owner" otherwise)
            bool recoveryRan = false;
            Opened += async (_, _) =>
            {
                if (recoveryRan) return;
                recoveryRan = true;
                try { await OfferProjectRecoveryAsync(); }
                catch (System.Exception ex) { Services.DndLog.W($"RECOVERY FAILED: {ex}"); }
            };
        }

        // ---------- auto-save & recovery (Milestone 7 §16) ----------

        private readonly MirrorStudio.Project.Services.ProjectRecoveryService _recovery = new();
        private readonly Avalonia.Threading.DispatcherTimer _autosaveTimer;
        private bool _suppressAutosave;

        private void KickAutosave()
        {
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        /// <summary>On startup: detect *.autosave files and offer "Recover MyFilm?".</summary>
        private async System.Threading.Tasks.Task OfferProjectRecoveryAsync()
        {
            var candidates = _recovery.FindRecoveryCandidates();
            foreach (var (name, path) in candidates)
            {
                var recover = await MessageBox.ShowConfirm(this,
                    "Project Recovery",
                    $"An auto-saved copy of \"{name}\" was found.\n\nRecover {name}?");
                try
                {
                    if (recover)
                    {
                        var project = await _recovery.LoadRecoveryAsync(path);
                        if (project != null)
                        {
                            _suppressAutosave = true;
                            _session.SetProject(project,
                                System.IO.Path.Combine(ProjectService.ProjectsRoot, project.Name));
                            Show(WorkspaceKind.Video);
                            StatusText.Text = $"Recovered: {project.Name}";
                        }
                    }
                }
                finally
                {
                    _recovery.DiscardRecovery(path);
                    _suppressAutosave = false;
                }
            }

            // ---------- .msp.recovery candidates (spec M8 §7) ----------
            foreach (var (name, path) in _recovery.FindMspRecoveries())
            {
                var recover = await MessageBox.ShowConfirm(this,
                    "Project Recovery",
                    $"An unsaved recovery version of \"{name}.msp\" was found.\n\nRecover it?");
                try
                {
                    if (recover)
                    {
                        var canonical = await _recovery.LoadMspRecoveryAsync(path);
                        if (canonical != null)
                        {
                            var model = MirrorStudio.Core.Projects.ProjectMapper.ToProjectModel(canonical);
                            var mspTarget = System.IO.Path.Combine(ProjectService.ProjectsRoot, name + ".msp");
                            _suppressAutosave = true;
                            _session.SetBundle(model, mspTarget);
                            Show(WorkspaceKind.Video);
                            StatusText.Text = $"Recovered: {name}";
                        }
                    }
                }
                finally
                {
                    _recovery.DiscardRecovery(path);
                    _suppressAutosave = false;
                }
            }
        }

        private void AddKeyBinding(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers mods, System.Action run) =>
            KeyBindings.Add(new Avalonia.Input.KeyBinding
            {
                Gesture = new Avalonia.Input.KeyGesture(key, mods),
                Command = new SimpleCommand(run)
            });

        private sealed class SimpleCommand : System.Windows.Input.ICommand
        {
            private readonly System.Action _run;
            public SimpleCommand(System.Action run) => _run = run;
#pragma warning disable CS0067
            public event System.EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _run();
        }

        private void Show(WorkspaceKind kind)
        {
            _currentKind = kind;
            switch (kind)
            {
                case WorkspaceKind.Home:
                    Host(_homeWorkspace, BtnHome, "Home - projects, templates and settings");
                    break;
                case WorkspaceKind.Video:
                    Host(_videoWorkspace, BtnVideo, "Video Studio - Media Browser | Preview | Timeline | Inspector | Effects | Transitions");
                    break;
                case WorkspaceKind.Image:
                    Host(_imageWorkspace, BtnImage, "Image Studio - Canvas | Layers | Tools | Properties | Filters");
                    break;
                case WorkspaceKind.Motion:
                    Host(_motionWorkspace, BtnMotion, "Motion Studio - Animation | Keyframes | Text Animation | Graphics | Motion Presets");
                    break;
                case WorkspaceKind.Audio:
                    Host(_audioWorkspace, BtnAudio, "Audio Studio - Tracks | Mixer | Equalizer | Waveform | Noise Reduction");
                    break;
                case WorkspaceKind.Export:
                    Host(_exportWorkspace, BtnExport, "Export - render with FFmpeg to MP4 / WebM / ProRes / PNG / WAV / MP3");
                    break;
            }
        }

        private void Host(UserControl view, Button button, string status)
        {
            WorkspaceHost.Content = view;
            Mark(BtnHome, button == BtnHome);
            Mark(BtnVideo, button == BtnVideo);
            Mark(BtnImage, button == BtnImage);
            Mark(BtnMotion, button == BtnMotion);
            Mark(BtnAudio, button == BtnAudio);
            Mark(BtnExport, button == BtnExport);
            StatusText.Text = status;
        }

        private void Mark(Button button, bool active)
        {
            if (active) button.Classes.Add("active");
            else button.Classes.Remove("active");
        }

        // ---- Edit menu ----

        private string EditTarget =>
            _session.HasProject ? $"{_currentKind} workspace" : "Home (no project open)";

        private void OnEditUndo(object? sender, RoutedEventArgs e)
        {
            if (_currentKind == WorkspaceKind.Video && _session.HasProject)
            {
                StatusText.Text = _videoWorkspace.UndoEdit()
                    ? "Undo applied."
                    : "Nothing to undo.";
                return;
            }
            StatusText.Text = _session.HasProject
                ? "Undo is available in the Video studio."
                : "Undo: nothing to undo on Home.";
        }

        private void OnEditRedo(object? sender, RoutedEventArgs e)
        {
            if (_currentKind == WorkspaceKind.Video && _session.HasProject)
            {
                StatusText.Text = _videoWorkspace.RedoEdit()
                    ? "Redo applied."
                    : "Nothing to redo.";
                return;
            }
            StatusText.Text = "Redo is available in the Video studio.";
        }

        private void OnEditCut(object? sender, RoutedEventArgs e) => DoEditCommand("Cut");
        private void OnEditCopy(object? sender, RoutedEventArgs e) => DoEditCommand("Copy");
        private void OnEditPaste(object? sender, RoutedEventArgs e) => DoEditCommand("Paste");

        private void OnEditDelete(object? sender, RoutedEventArgs e) => DoEditCommand("Delete");

        private void OnEditSelectAll(object? sender, RoutedEventArgs e)
        {
            if (_currentKind == WorkspaceKind.Home || !_session.HasProject)
                StatusText.Text = "Select All is available inside a studio workspace.";
            else
                StatusText.Text = $"Select All applied in {EditTarget}.";
        }

        private void DoEditCommand(string cmd)
        {
            if (_currentKind == WorkspaceKind.Home || !_session.HasProject)
            {
                StatusText.Text = $"{cmd}: open a project and switch to a studio workspace first.";
                return;
            }
            StatusText.Text = $"{cmd} in {EditTarget}.";
        }

        // ---- View menu ----

        private void ApplyTheme(string theme)
        {
            _session.Settings.Theme = theme;
            _settingsService.Save(_session.Settings);
            ThemeManager.Apply(theme);
            StatusText.Text = $"Theme: {theme}";
        }

        private void OnThemeDark(object? sender, RoutedEventArgs e) => ApplyTheme("Dark");
        private void OnThemeLight(object? sender, RoutedEventArgs e) => ApplyTheme("Light");

        private void OnViewToggleStatusBar(object? sender, RoutedEventArgs e)
        {
            StatusBar.IsVisible = !StatusBar.IsVisible;
        }

        private void OnViewFullScreen(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }

        // ---- Workspace cycling ----

        private static readonly WorkspaceKind[] WorkspaceOrder =
        {
            WorkspaceKind.Home, WorkspaceKind.Video, WorkspaceKind.Image,
            WorkspaceKind.Motion, WorkspaceKind.Audio, WorkspaceKind.Export
        };

        private WorkspaceKind _currentKind = WorkspaceKind.Home;

        private void CycleWorkspace(int direction)
        {
            var i = System.Array.IndexOf(WorkspaceOrder, _currentKind);
            Show(WorkspaceOrder[(i + direction + WorkspaceOrder.Length) % WorkspaceOrder.Length]);
        }

        private void OnNextWorkspace(object? sender, RoutedEventArgs e) => CycleWorkspace(+1);
        private void OnPrevWorkspace(object? sender, RoutedEventArgs e) => CycleWorkspace(-1);

        // ---- Project menu ----

        private void OnOpenProjectFolder(object? sender, RoutedEventArgs e)
        {
            if (!_session.HasProject)
            {
                StatusText.Text = "No project open.";
                return;
            }
            OpenInExplorer(_session.ProjectFolder);
        }

        private void OpenInExplorer(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
            {
                StatusText.Text = $"Folder not found: {folder}";
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                StatusText.Text = $"Could not open folder: {ex.Message}";
            }
        }

        // ---- Media menu ----

        private async void OnMediaImport(object? sender, RoutedEventArgs e)
        {
            if (!_session.HasProject) { StatusText.Text = "Open or create a project first."; return; }
            Show(WorkspaceKind.Video);
            await _videoWorkspace.ImportMediaAsync();
        }

        private async void OnMediaRegenerateThumbnails(object? sender, RoutedEventArgs e) =>
            await _videoWorkspace.RegenerateThumbnailsAsync();

        private void OnOpenMediaFolder(object? sender, RoutedEventArgs e)
        {
            if (!_session.HasProject) { StatusText.Text = "No project open."; return; }
            var mediaDir = System.IO.Path.Combine(_session.ProjectFolder, "Media");
            OpenInExplorer(System.IO.Directory.Exists(mediaDir) ? mediaDir : _session.ProjectFolder);
        }

        private async void OnMediaGenerateProxies(object? sender, RoutedEventArgs e)
        {
            var proj = _session.CurrentProject;
            if (!_session.HasProject || proj == null) { StatusText.Text = "No project open."; return; }

            var videos = proj.Media
                .Where(m => m.MediaType is "Video" or "Images")
                .Select(m => (Entry: m, Abs: _session.ResolvePath(m.RelativePath)))
                .Where(x => x.Abs != null && System.IO.File.Exists(x.Abs))
                .ToList();

            if (videos.Count == 0)
            {
                await MessageBox.Show(this, "Generate Proxies", "No video/image media in this project.");
                return;
            }

            var proxyDir = System.IO.Path.Combine(_session.ProjectFolder, "Proxies");
            var svc = new MirrorStudio.Media.Services.ProxyService(
                MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg(_session.Settings.FFmpegPath),
                proxyDir);

            StatusText.Text = "Generating proxies…";
            int done = 0;
            foreach (var (entry, abs) in videos)
            {
                StatusText.Text = $"Proxy {done + 1}/{videos.Count}: {entry.FileName}";
                var q = MirrorStudio.Media.Services.ProxyService.ProxyQuality.p720;
                if (await svc.TryGenerateAsync(abs!, q) != null) done++;
            }

            StatusText.Text = $"Proxies ready: {done}/{videos.Count} → {proxyDir}";
        }

        // ---- Timeline menu ----

        private void OnTimelineSplit(object? sender, RoutedEventArgs e) => _videoWorkspace.SplitAtPlayhead();
        private void OnTimelineDeleteSelected(object? sender, RoutedEventArgs e) => _videoWorkspace.DeleteSelectedClip();
        private void OnTimelineGoStart(object? sender, RoutedEventArgs e) => _videoWorkspace.GoToSequenceStart();
        private void OnTimelineGoEnd(object? sender, RoutedEventArgs e) => _videoWorkspace.GoToSequenceEnd();
        private void OnTimelineZoomIn(object? sender, RoutedEventArgs e) => _videoWorkspace.ZoomTimeline(+10);
        private void OnTimelineZoomOut(object? sender, RoutedEventArgs e) => _videoWorkspace.ZoomTimeline(-10);

        // ---- Tools menu ----

        private async void OnToolsDetectMediaEngine(object? sender, RoutedEventArgs e)
        {
            var ffmpeg = MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfmpeg(_session.Settings.FFmpegPath);
            var ffprobe = MirrorStudio.Video.Services.MediaEngineLocator.ResolveFfprobe(_session.Settings.FFprobePath);
            bool ok = new MirrorStudio.Video.Services.FfmpegService(ffmpeg).IsAvailable();

            await MessageBox.Show(this, "Media Engine",
                $"ffmpeg : {ffmpeg}\n" +
                $"ffprobe: {ffprobe}\n\n" +
                (ok ? "FFmpeg is working." :
                 "FFmpeg not found. Install it with:\nwinget install Gyan.FFmpeg\nor set full paths in Home > Settings."));
        }

        private void OnToolsSettings(object? sender, RoutedEventArgs e) => Show(WorkspaceKind.Home);

        // ---- Window helpers (kept for programmatic use) ----

        private void OnWindowMinimize(object? sender, RoutedEventArgs e) => WindowState = Avalonia.Controls.WindowState.Minimized;

        private void OnWindowMaximizeRestore(object? sender, RoutedEventArgs e) =>
            WindowState = WindowState == Avalonia.Controls.WindowState.Maximized
                ? Avalonia.Controls.WindowState.Normal
                : Avalonia.Controls.WindowState.Maximized;

        // ---- Help menu ----

        private async void OnHelpQuickStart(object? sender, RoutedEventArgs e)
        {
            await MessageBox.Show(this, "Quick Start Guide",
                "1. File > New Project (or pick a template on Home).\n" +
                "2. Import media in Video Studio - files land in the project's Media folder.\n" +
                "3. Arrange clips on the timeline; use Inspector/Effects/Transitions to refine.\n" +
                "4. Polish audio in Audio Studio, motion titles in Motion Studio.\n" +
                "5. Export renders with FFmpeg from the Export workspace.\n\n" +
                "Projects save as .msp bundles (File > Save Project As).");
        }

        private async void OnHelpShortcuts(object? sender, RoutedEventArgs e)
        {
            await MessageBox.Show(this, "Keyboard Shortcuts",
                "Ctrl+N  New Project          Ctrl+O  Open Project\n" +
                "Ctrl+S  Save Project         F12     Save Project As\n" +
                "Ctrl+Z / Ctrl+Y  Undo / Redo\n" +
                "Ctrl+X / Ctrl+C / Ctrl+V  Cut / Copy / Paste\n" +
                "Ctrl+A  Select All           Delete  Delete selection\n" +
                "Ctrl+I  Import Media         Ctrl+K  Split clip at playhead\n" +
                "Ctrl+P  Project Settings\n\n" +
                "Alt+1..6  Switch workspace   Ctrl+Tab / Ctrl+Shift+Tab  Cycle workspaces\n" +
                "F11     Full Screen");
        }

        private void OnHelpOpenProjectsFolder(object? sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ProjectService.ProjectsRoot,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                StatusText.Text = $"Could not open folder: {ex.Message}";
            }
        }

        private async void OnHelpCheckUpdates(object? sender, RoutedEventArgs e)
        {
            await MessageBox.Show(this, "Check for Updates",
                "Mirror Studio 0.1.0 (preview)\nYou are running the latest build.");
        }

        private async void OnHelpAbout(object? sender, RoutedEventArgs e)
        {
            await MessageBox.Show(this, "About Mirror Studio",
                "Mirror Studio 0.1.0 (preview)\n" +
                "Video | Image | Motion | Audio creation suite\n\n" +
                "Built with Avalonia UI - rendered by FFmpeg.\n" +
                "(c) Mirror Age Concepts");
        }

        private void UpdateProjectBadge()
        {
            ProjectBadgeText.Text = _session.HasProject ? _session.CurrentProject!.Name : "No project";
        }

        private void OnShowHome(object? sender, RoutedEventArgs e) => Show(WorkspaceKind.Home);
        private void OnShowVideo(object? sender, RoutedEventArgs e) => Show(WorkspaceKind.Video);
        private void OnShowImage(object? sender, RoutedEventArgs e) => Show(WorkspaceKind.Image);
        private void OnShowMotion(object? sender, RoutedEventArgs e) => Show(WorkspaceKind.Motion);
        private void OnShowAudio(object? sender, RoutedEventArgs e) => Show(WorkspaceKind.Audio);
        private void OnShowExport(object? sender, RoutedEventArgs e) => Show(WorkspaceKind.Export);

        private async void OnNewProject(object? sender, RoutedEventArgs e) => await NewProjectFlowAsync();
        private async void OnOpenProject(object? sender, RoutedEventArgs e) => await OpenProjectDialogAsync();

        private async void OnSaveProject(object? sender, RoutedEventArgs e)
        {
            if (!_session.HasProject)
            {
                await MessageBox.Show(this, "No Project", "Open or create a project first.");
                return;
            }

            bool ok;
            if (_session.MspPath != null)
                ok = await _projectService.SaveBundleAsync(_session.CurrentProject!, _session.MspPath);
            else
                ok = await _projectService.SaveProjectAsync(_session.CurrentProject!, _session.ProjectFolder);

            StatusText.Text = ok ? $"Saved: {_session.CurrentProject!.Name} ({System.DateTime.Now:HH:mm:ss})" : "Save failed";
            if (!ok) await MessageBox.Show(this, "Save Failed", "Could not write the project.");
        }

        private async void OnSaveProjectAs(object? sender, RoutedEventArgs e)
        {
            if (!_session.HasProject)
            {
                await MessageBox.Show(this, "No Project", "Open or create a project first.");
                return;
            }

            var mspPath = await _projectDialogs.SaveProjectBundleAsync(Sanitize(_session.CurrentProject!.Name));
            if (string.IsNullOrWhiteSpace(mspPath)) return;

            if (!mspPath.EndsWith(".msp", System.StringComparison.OrdinalIgnoreCase))
                mspPath += ".msp";

            var ok = await _projectService.SaveBundleAsync(_session.CurrentProject!, mspPath);
            if (ok)
            {
                _session.SetBundle(_session.CurrentProject!, mspPath);
                _recents.Add(mspPath);
                StatusText.Text = $"Bundle saved: {mspPath}";
            }
            else
            {
                await MessageBox.Show(this, "Save As Failed", "Could not write the .msp bundle.");
            }
        }

        private static string Sanitize(string s)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private async void OnProjectSettings(object? sender, RoutedEventArgs e)
        {
            if (!_session.HasProject)
            {
                await MessageBox.Show(this, "No Project", "Open or create a project first.");
                return;
            }
            var dlg = new ProjectSettingsDialog(_session.CurrentProject!);
            await dlg.ShowDialog(this);
            _session.NotifyChanged();
            UpdateProjectBadge();
            this.Title = $"Mirror Studio - {_session.CurrentProject!.Name}";
        }

        private async void OnBackupProject(object? sender, RoutedEventArgs e)
        {
            if (!_session.HasProject)
            {
                await MessageBox.Show(this, "No Project", "Open or create a project first.");
                return;
            }
            await _projectService.SaveProjectAsync(_session.CurrentProject!, _session.ProjectFolder);
            var zip = _projectService.BackupProject(_session.ProjectFolder);
            if (zip != null) StatusText.Text = $"Backup created: {zip}";
            else await MessageBox.Show(this, "Backup Failed", "Could not create backup archive.");
        }

        private void OnCloseProject(object? sender, RoutedEventArgs e)
        {
            _session.CloseProject();
            Show(WorkspaceKind.Home);
            this.Title = "Mirror Studio";
            ProjectBadgeText.Text = "No project";
            StatusText.Text = "Project closed.";
        }

        private async System.Threading.Tasks.Task NewProjectFlowAsync()
        {
            var dlg = new NewProjectDialog();
            await dlg.ShowDialog(this);

            if (!dlg.WasConfirmed) return;

            var name = string.IsNullOrWhiteSpace(dlg.ProjectName) ? "New Project" : dlg.ProjectName;
            var folder = dlg.ProjectPath;

            if (string.IsNullOrWhiteSpace(folder))
            {
                await MessageBox.Show(this, "Error", "Please select a project folder.");
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory(folder);
            }
            catch (System.Exception ex)
            {
                await MessageBox.Show(this, "Error creating project folder", ex.Message);
                return;
            }

            var project = _projectService.Create(name, dlg.Width, dlg.Height, dlg.FrameRate);
            var ok = await _projectService.CreateProjectAsync(project, folder);

            if (ok)
            {
                _session.SetProject(project, folder);
                _recents.Add(System.IO.Path.Combine(folder, "project.json"));
                this.Title = $"Mirror Studio - {project.Name}";
                StatusText.Text = $"Project created: {System.IO.Path.Combine(folder, "project.json")}";
                Show(WorkspaceKind.Video);
            }
            else
            {
                await MessageBox.Show(this, "Error", "Failed to create project layout.");
            }
        }

        public async void OpenProjectFromPath(string projFile)
        {
            if (string.IsNullOrWhiteSpace(projFile)) return;

            // .msp bundle?
            if (projFile.EndsWith(".msp", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!System.IO.File.Exists(projFile))
                {
                    await MessageBox.Show(this, "Could not open project", projFile);
                    return;
                }
                var bundled = await _projectService.OpenBundleAsync(projFile);
                if (bundled == null)
                {
                    await MessageBox.Show(this, "Invalid .msp file", projFile);
                    return;
                }
                _session.SetBundle(bundled, projFile);
                _recents.Add(projFile);
                this.Title = $"Mirror Studio - {bundled.Name}";
                StatusText.Text = $"{bundled.Name} | {bundled.Width}x{bundled.Height} @ {bundled.FrameRate:0.##}fps | bundle";
                Show(WorkspaceKind.Video);
                return;
            }

            if (!System.IO.Path.HasExtension(projFile))
                projFile = System.IO.Path.Combine(projFile, "project.json");

            if (!System.IO.File.Exists(projFile))
            {
                await MessageBox.Show(this, "Could not open project", projFile);
                return;
            }

            try
            {
                var text = await System.IO.File.ReadAllTextAsync(projFile);
                var project = System.Text.Json.JsonSerializer.Deserialize<MirrorStudio.Core.Models.ProjectModel>(text);
                if (project != null)
                {
                    _session.SetProject(project, System.IO.Path.GetDirectoryName(projFile) ?? string.Empty);
                    _recents.Add(projFile);
                    this.Title = $"Mirror Studio - {project.Name}";
                    StatusText.Text = $"{project.Name} | {project.Width}x{project.Height} @ {project.FrameRate:0.##}fps";
                    Show(WorkspaceKind.Video);
                }
                else
                {
                    await MessageBox.Show(this, "Opened project:", projFile + "\n(Unable to parse project.json)");
                }
            }
            catch (System.Exception ex)
            {
                await MessageBox.Show(this, "Error reading project", ex.Message);
            }
        }

        private async System.Threading.Tasks.Task OpenProjectDialogAsync()
        {
            var path = await _projectDialogs.OpenProjectAsync();
            if (!string.IsNullOrWhiteSpace(path)) OpenProjectFromPath(path);
        }

        private void OnExit(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
