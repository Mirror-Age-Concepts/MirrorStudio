using MirrorStudio.Core.Models;
using MirrorStudio.Database;
using System;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MirrorStudio.Project
{
    public class ProjectService
    {
        /// <summary>Documents\Mirror Studio</summary>
        public static string WorkspaceRoot =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mirror Studio");

        public static string ProjectsRoot => System.IO.Path.Combine(WorkspaceRoot, "Projects");
        public static string TemplatesRoot => System.IO.Path.Combine(WorkspaceRoot, "Templates");

        public static void EnsureWorkspaceFolders()
        {
            try
            {
                System.IO.Directory.CreateDirectory(ProjectsRoot);
                System.IO.Directory.CreateDirectory(TemplatesRoot);
            }
            catch { /* best effort */ }
        }

        public ProjectModel Create(string name, int width, int height, double frameRate)
        {
            var now = DateTime.UtcNow;
            var project = new ProjectModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Width = width,
                Height = height,
                FrameRate = frameRate,
                CreatedAt = now,
                UpdatedAt = now,
            };

            // default export presets
            project.ExportPresets.Add(new ExportPreset { Name = "YouTube", Width = 1920, Height = 1080, FrameRate = frameRate, Format = "mp4", VideoCodec = "h264", AudioCodec = "aac" });
            project.ExportPresets.Add(new ExportPreset { Name = "Instagram", Width = 1080, Height = 1920, FrameRate = frameRate, Format = "mp4", VideoCodec = "h264", AudioCodec = "aac" });
            project.ExportPresets.Add(new ExportPreset { Name = "TikTok", Width = 1080, Height = 1920, FrameRate = frameRate, Format = "mp4", VideoCodec = "h264", AudioCodec = "aac" });

            return project;
        }

        public async Task<bool> CreateProjectAsync(ProjectModel project, string folderPath)
        {
            if (project == null || string.IsNullOrWhiteSpace(folderPath)) return false;

            try
            {
                ScaffoldProjectFolder(folderPath);

                var ok = await SaveProjectAsync(project, folderPath);
                return ok;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates the on-disk layout:
        /// Media/{Video,Audio,Images,Graphics}, Fonts, Sequences, Exports,
        /// Cache, Proxy, Thumbnail, Render (+ Backups created lazily by BackupProject).
        /// </summary>
        public void ScaffoldProjectFolder(string folderPath)
        {
            Directory.CreateDirectory(folderPath);

            foreach (var rel in new[]
            {
                "Media/Video", "Media/Audio", "Media/Images", "Media/Graphics",
                "Fonts",
                "Sequences", "Effects", "Transitions", "Titles", "Subtitles",
                "Exports",
                "Cache", "Proxy", "Thumbnail", "Render"
            })
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folderPath, rel));
            }
        }

        private static string JsonOf(ProjectModel project)
        {
            return System.Text.Json.JsonSerializer.Serialize(project,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private static void SyncDatabase(ProjectModel project, string folderOrBundleLocation)
        {
            var dbPath = System.IO.Path.Combine(folderOrBundleLocation, "database.db");
            using var db = new MirrorDatabase(dbPath);
            db.SyncProject(project, folderOrBundleLocation);
        }

        public async Task<bool> SaveProjectAsync(ProjectModel project, string folderPath)
        {
            if (project == null || string.IsNullOrWhiteSpace(folderPath)) return false;

            try
            {
                project.UpdatedAt = DateTime.UtcNow;
                var projectJsonPath = System.IO.Path.Combine(folderPath, "project.json");
                await System.IO.File.WriteAllTextAsync(projectJsonPath, JsonOf(project));

                SyncDatabase(project, folderPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------- .msp bundle ----------

        /// <summary>
        /// Saves a proprietary .msp container:
        ///   MyFilm.msp ── project.json / database.db / thumbnails/ / proxies/ / cache/
        /// Original media files are never copied inside the bundle.
        /// </summary>
        public async Task<bool> SaveBundleAsync(ProjectModel project, string mspPath)
        {
            if (project == null || string.IsNullOrWhiteSpace(mspPath)) return false;

            try
            {
                var tempDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "mirrorstudio_" + Guid.NewGuid().ToString("N"));
                try
                {
                    foreach (var d in new[] { "", "thumbnails", "proxies", "cache" })
                        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(tempDir, d));

                    project.UpdatedAt = DateTime.UtcNow;
                    await System.IO.File.WriteAllTextAsync(
                        System.IO.Path.Combine(tempDir, "project.json"), JsonOf(project));

                    SyncDatabase(project, tempDir); // writes tempDir/database.db

                    var tempZip = mspPath + ".tmp";
                    if (System.IO.File.Exists(tempZip)) System.IO.File.Delete(tempZip);
                    ZipFile.CreateFromDirectory(tempDir, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);

                    // atomic-ish replace
                    if (System.IO.File.Exists(mspPath))
                    {
                        var old = mspPath + ".old";
                        System.IO.File.Move(mspPath, old, true);
                        System.IO.File.Move(tempZip, mspPath, true);
                        System.IO.File.Delete(old);
                    }
                    else
                    {
                        System.IO.File.Move(tempZip, mspPath);
                    }
                    return true;
                }
                finally
                {
                    try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Reads a .msp container without extracting it to disk.</summary>
        public async Task<ProjectModel?> OpenBundleAsync(string mspPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(mspPath);
                var entry = archive.GetEntry("project.json");
                if (entry == null) return null;

                using var stream = entry.Open();
                using var reader = new System.IO.StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                return System.Text.Json.JsonSerializer.Deserialize<ProjectModel>(json);
            }
            catch
            {
                return null;
            }
        }

        public string? BackupProject(string folderPath)
        {
            try
            {
                var projectJsonPath = System.IO.Path.Combine(folderPath, "project.json");
                if (!System.IO.File.Exists(projectJsonPath)) return null;

                var backupsDir = System.IO.Path.Combine(folderPath, "Backups");
                System.IO.Directory.CreateDirectory(backupsDir);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var zipPath = System.IO.Path.Combine(backupsDir, $"backup_{stamp}.zip");

                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(projectJsonPath, "project.json");

                    var dbPath = System.IO.Path.Combine(folderPath, "database.db");
                    if (System.IO.File.Exists(dbPath))
                        archive.CreateEntryFromFile(dbPath, "database.db");
                }
                return zipPath;
            }
            catch
            {
                return null;
            }
        }
    }
}
