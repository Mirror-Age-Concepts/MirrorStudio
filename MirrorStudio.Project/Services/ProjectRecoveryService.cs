using MirrorStudio.Core.Models;

namespace MirrorStudio.Project.Services;

/// <summary>
/// Crash recovery via debounced auto-save (spec M7 §16):
///
///   editing → change detected → debounce 5s → auto-save → MyFilm.autosave
///
/// On restart the app detects recovery candidates and offers "Recover MyFilm?".
/// </summary>
public class ProjectRecoveryService
{
    private readonly ProjectSerializer _serializer = new();

    /// <summary>Documents\Mirror Studio\Autosave</summary>
    public static string AutosaveRoot =>
        Path.Combine(ProjectService.WorkspaceRoot, "Autosave");

    public string AutosavePathFor(string projectName)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            projectName = projectName.Replace(c, '_');
        return Path.Combine(AutosaveRoot, projectName + ".autosave");
    }

    /// <summary>Writes (or overwrites) the recovery file for a project.</summary>
    public async Task<string?> WriteAutosaveAsync(ProjectModel project)
    {
        try
        {
            Directory.CreateDirectory(AutosaveRoot);
            var path = AutosavePathFor(project.Name);
            await _serializer.SaveAsync(project, path);
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>All pending recovery candidates (name + autosave path).</summary>
    public IReadOnlyList<(string ProjectName, string Path)> FindRecoveryCandidates()
    {
        var list = new List<(string, string)>();
        try
        {
            if (!Directory.Exists(AutosaveRoot)) return list;
            foreach (var file in Directory.GetFiles(AutosaveRoot, "*.autosave"))
                list.Add((Path.GetFileNameWithoutExtension(file), file));
        }
        catch { /* best effort */ }
        return list;
    }

    /// <summary>Loads an autosave file back into a project.</summary>
    public Task<ProjectModel?> LoadRecoveryAsync(string autosavePath) =>
        _serializer.LoadAsync(autosavePath);

    /// <summary>Removes the recovery file once recovered or dismissed.</summary>
    public void DiscardRecovery(string autosavePath)
    {
        try { File.Delete(autosavePath); } catch { /* best effort */ }
    }
}
