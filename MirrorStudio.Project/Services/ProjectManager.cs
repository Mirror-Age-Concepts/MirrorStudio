using MirrorStudio.Core.Models;

namespace MirrorStudio.Project.Services;

/// <summary>
/// Facade over project creation, JSON serialization and .msp bundle I/O
/// (spec M7 §13–§15). The original media is never embedded in the project.
/// </summary>
public class ProjectManager
{
    private readonly ProjectService _projects = new();
    private readonly ProjectSerializer _serializer = new();

    public ProjectSerializer Serializer => _serializer;

    public ProjectModel CreateNew(string name, int width, int height, double frameRate) =>
        _projects.Create(name, width, height, frameRate);

    /// <summary>Saves a folder-based project (project.json + database.db).</summary>
    public Task<bool> SaveFolderProjectAsync(ProjectModel project, string folderPath) =>
        _projects.SaveProjectAsync(project, folderPath);

    /// <summary>Saves a portable MyFilm.msp ZIP package.</summary>
    public Task<bool> SaveBundleAsync(ProjectModel project, string mspPath) =>
        _projects.SaveBundleAsync(project, mspPath);

    /// <summary>Loads a folder-based project.json.</summary>
    public async Task<ProjectModel?> LoadAsync(string folderPath)
    {
        var project = await _serializer.LoadAsync(Path.Combine(folderPath, "project.json"));
        return project;
    }

    /// <summary>Loads a project from inside a .msp container without extraction.</summary>
    public Task<ProjectModel?> OpenBundleAsync(string mspPath) =>
        _projects.OpenBundleAsync(mspPath);
}
