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

    // ---------- .msp.recovery (spec M8 §7) ----------

    private readonly ProjectPackageService _packages = new();

    /// <summary>
    /// Writes a temporary recovery version of a bundle project:
    /// MyFilm.msp.recovery (a .msp-format ZIP next to the target file).
    /// </summary>
    public async Task<string?> WriteMspRecoveryAsync(Core.Projects.Project project, string mspPath)
    {
        try
        {
            var recoveryPath = mspPath + ".recovery";
            await _packages.SaveAsync(project, recoveryPath);
            return recoveryPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>All *.msp.recovery files found next to known bundles.</summary>
    public IReadOnlyList<(string ProjectName, string Path)> FindMspRecoveries()
    {
        var list = new List<(string, string)>();
        try
        {
            if (!Directory.Exists(ProjectService.ProjectsRoot)) return list;
            foreach (var file in Directory.GetFiles(ProjectService.ProjectsRoot, "*.msp.recovery", SearchOption.AllDirectories))
                list.Add((Path.GetFileName(file).Replace(".msp.recovery", ""), file));
        }
        catch { /* best effort */ }
        return list;
    }

    /// <summary>Loads a .msp.recovery container back into a canonical project.</summary>
    public Task<Core.Projects.Project?> LoadMspRecoveryAsync(string recoveryPath) =>
        _packages.LoadAsync(recoveryPath);
}
