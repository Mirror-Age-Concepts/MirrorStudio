using MirrorStudio.Core.Models;
using MirrorStudio.Project;

namespace MirrorStudio.App
{
    public enum WorkspaceKind
    {
        Home,
        Video,
        Image,
        Motion,
        Audio,
        Export
    }

    public class SessionState
    {
        public ProjectModel? CurrentProject { get; private set; }
        public string ProjectFolder { get; private set; } = string.Empty;
        public bool HasProject => CurrentProject != null;

        /// <summary>Set when the project was opened from / saved as a .msp bundle.</summary>
        public string? MspPath { get; private set; }

        public AppSettings Settings { get; private set; } = new();

        public double PlayheadSeconds { get; private set; }
        public ClipModel? SelectedClip { get; private set; }
        public string? SelectedMediaPath { get; private set; }

        public event Action? ProjectChanged;
        public event Action? PlayheadChanged;
        public event Action? SelectionChanged;

        public void SetSettings(AppSettings settings)
        {
            Settings = settings ?? new AppSettings();
        }

        public void SetProject(ProjectModel project, string folder)
        {
            CurrentProject = project;
            ProjectFolder = folder;
            MspPath = null;
            PlayheadSeconds = 0;
            SelectedClip = null;
            SelectedMediaPath = null;
            ProjectChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        public void SetBundle(ProjectModel project, string mspPath)
        {
            CurrentProject = project;
            MspPath = mspPath;
            ProjectFolder = System.IO.Path.GetDirectoryName(mspPath) ?? string.Empty;
            PlayheadSeconds = 0;
            SelectedClip = null;
            SelectedMediaPath = null;
            ProjectChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        public void NotifyChanged() => ProjectChanged?.Invoke();

        public void CloseProject()
        {
            CurrentProject = null;
            ProjectFolder = string.Empty;
            MspPath = null;
            PlayheadSeconds = 0;
            SelectedClip = null;
            SelectedMediaPath = null;
            ProjectChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        public TimelineData? Timeline => CurrentProject?.Timeline;

        public void SetPlayhead(double seconds)
        {
            var max = Timeline?.SequenceDuration ?? 0;
            PlayheadSeconds = System.Math.Clamp(seconds, 0, System.Math.Max(max, 3600));
            PlayheadChanged?.Invoke();
        }

        public void MovePlayhead(double deltaSeconds) => SetPlayhead(PlayheadSeconds + deltaSeconds);

        public void SelectClip(ClipModel? clip)
        {
            SelectedClip = clip;
            SelectedMediaPath = clip == null ? null : ResolvePath(clip.SourcePath);
            SelectionChanged?.Invoke();
        }

        public void SelectMedia(string fullPath, ClipModel? linkedClip = null)
        {
            SelectedMediaPath = fullPath;
            SelectedClip = linkedClip;
            SelectionChanged?.Invoke();
        }

        public string? ResolvePath(string relativeOrAbsolute)
        {
            if (string.IsNullOrEmpty(relativeOrAbsolute)) return null;
            if (System.IO.Path.IsPathRooted(relativeOrAbsolute)) return relativeOrAbsolute;
            return System.IO.Path.Combine(ProjectFolder, relativeOrAbsolute);
        }
    }
}
