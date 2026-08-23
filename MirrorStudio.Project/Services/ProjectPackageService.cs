using System.IO.Compression;
using System.Text.Json;
using MirrorStudio.Core.Projects;

namespace MirrorStudio.Project.Services;

/// <summary>
/// Saves/loads the canonical <see cref="Project"/> as a .msp ZIP package
/// (project.json inside the container — spec M8 §5/§6).
/// </summary>
public class ProjectPackageService
{
    private readonly JsonSerializerOptions _options =
        new()
        {
            WriteIndented = true
        };

    public async Task SaveAsync(
        MirrorStudio.Core.Projects.Project project,
        string filePath)
    {
        project.ModifiedAt =
            DateTime.UtcNow;

        var temp =
            filePath + ".tmp";

        if (File.Exists(temp))
            File.Delete(temp);

        using (var archive =
                   ZipFile.Open(
                       temp,
                       ZipArchiveMode.Create))
        {
            var entry =
                archive.CreateEntry(
                    "project.json");

            await using var stream =
                entry.Open();

            await JsonSerializer.SerializeAsync(
                stream,
                project,
                _options);
        }

        if (File.Exists(filePath))
            File.Delete(filePath);

        File.Move(temp, filePath);
    }

    public async Task<MirrorStudio.Core.Projects.Project> LoadAsync(
        string filePath)
    {
        using var archive =
            ZipFile.OpenRead(filePath);

        var entry =
            archive.GetEntry(
                "project.json")
            ?? throw new InvalidDataException(
                "Invalid Mirror Studio project.");

        await using var stream =
            entry.Open();

        var project =
            await JsonSerializer
                .DeserializeAsync<MirrorStudio.Core.Projects.Project>(
                    stream,
                    _options);

        return project
            ?? throw new InvalidDataException(
                "Project data is empty.");
    }
}
