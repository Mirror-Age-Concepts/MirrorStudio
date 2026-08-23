using System.Text.Json;
using MirrorStudio.Core.Models;

namespace MirrorStudio.Project.Services;

/// <summary>
/// Serializes the full project graph (settings, media assets, tracks, clips,
/// effects, transitions, titles/keyframes) to and from JSON (spec M7 §13/§15).
/// </summary>
public class ProjectSerializer
{
    private readonly JsonSerializerOptions _options =
        new()
        {
            WriteIndented = true
        };

    public async Task SaveAsync(
        ProjectModel project,
        string filePath)
    {
        var json =
            JsonSerializer.Serialize(
                project,
                _options);

        await File.WriteAllTextAsync(
            filePath,
            json);
    }

    public async Task<ProjectModel?> LoadAsync(
        string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var json =
            await File.ReadAllTextAsync(
                filePath);

        return JsonSerializer.Deserialize<ProjectModel>(
            json,
            _options);
    }
}
