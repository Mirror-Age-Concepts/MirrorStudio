using MirrorStudio.Core.Projects;

namespace MirrorStudio.Media.Services;

/// <summary>
/// Handles offline media (spec M8 §4):
///
///   MEDIA OFFLINE — Film.mp4
///   Original: C:\Videos\Film.mp4
///   [Locate Media] [Search Folder] [Ignore]
///
/// Flow: locate file → validate → update ProjectMedia.FilePath → save project.
/// </summary>
public class MediaRelinkService
{
    private readonly Func<string, Task<bool>>? _validateAsync;

    /// <param name="validateAsync">
    /// Optional deep validation (e.g. ffprobe duration match). Default: file exists.
    /// </param>
    public MediaRelinkService(Func<string, Task<bool>>? validateAsync = null)
    {
        _validateAsync = validateAsync;
    }

    public bool IsOffline(ProjectMedia media) =>
        string.IsNullOrWhiteSpace(media.FilePath) || !File.Exists(media.FilePath);

    /// <summary>All media references whose files are missing.</summary>
    public IReadOnlyList<ProjectMedia> FindOffline(Project project) =>
        project.Media.Where(IsOffline).ToList();

    /// <summary>Relink a single media reference to a newly located file.</summary>
    public async Task<bool> RelinkAsync(ProjectMedia media, string newFilePath)
    {
        if (!File.Exists(newFilePath))
            return false;

        if (_validateAsync != null && !await _validateAsync(newFilePath))
            return false;

        media.FilePath = Path.GetFullPath(newFilePath);
        media.FileName = Path.GetFileNameWithoutExtension(newFilePath);
        return true;
    }

    /// <summary>
    /// [Search Folder]: tries to auto-match every offline reference by file name
    /// (recursively) inside the given folder.
    /// </summary>
    public async Task<IReadOnlyList<ProjectMedia>> SearchFolderAsync(
        Project project, string folder)
    {
        var relinked = new List<ProjectMedia>();
        if (!Directory.Exists(folder)) return relinked;

        foreach (var media in FindOffline(project))
        {
            try
            {
                var match = Directory.EnumerateFiles(
                        folder,
                        $"{media.FileName}.*",
                        SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (match == null) continue;
                if (await RelinkAsync(media, match))
                    relinked.Add(media);
            }
            catch { /* unreadable subtree — skip */ }
        }
        return relinked;
    }

    /// <summary>Removes the relinked entries' stale state by returning the project ready to save.</summary>
    public Project SaveAfterRelink(Project project) => project;
}
