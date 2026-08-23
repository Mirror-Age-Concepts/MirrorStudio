using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MirrorStudio.Core.Models;

namespace MirrorStudio.Database
{
    /// <summary>
    /// EF-Core-shaped persistence facade over MirrorStudio.db.
    /// (EF Core itself requires a NuGet restore that isn't available in this offline
    /// environment, so this exposes the same Projects/MediaAssets/Tracks/Clips sets
    /// on top of the bundled raw-SQLite engine instead.)
    /// </summary>
    public class MirrorStudioDbContext : IDisposable
    {
        private readonly MirrorDatabase _db;

        public string DatabasePath { get; }

        public MirrorStudioDbContext()
        {
            DatabasePath = Path.Combine(AppContext.BaseDirectory, "MirrorStudio.db");
            _db = new MirrorDatabase(DatabasePath);
        }

        public MirrorStudioDbContext(string databasePath)
        {
            DatabasePath = databasePath;
            _db = new MirrorDatabase(databasePath);
        }

        // ---------- "DbSets" ----------

        public IEnumerable<ProjectSummary> Projects => _db.ListProjects();

        public IReadOnlyList<MediaAsset> MediaAssets(string projectId) =>
            LoadAssets(projectId);

        // ---------- write path ----------

        /// <summary>Persists the full project graph (project row + sequence + tracks + clips + media).</summary>
        public void SaveProject(ProjectModel project, string location)
        {
            _db.SyncProject(project, location);
        }

        // ---------- read path ----------

        public List<ProjectSummary> ListProjects() => _db.ListProjects();

        public string GetSetting(string key, string defaultValue = "") => _db.GetSetting(key, defaultValue);

        public void SetSetting(string key, string value) => _db.SetSetting(key, value);

        private List<MediaAsset> LoadAssets(string projectId)
        {
            var assets = new List<MediaAsset>();
            foreach (var m in Query($"SELECT Id,FileName,RelativePath,MediaType,DurationSec,Width,Height,FrameRate,Codec,ThumbnailPath FROM MediaAssets WHERE ProjectId='{Esc(projectId)}'"))
            {
                var rel = Str(m, "RelativePath");
                var category = Str(m, "MediaType");
                assets.Add(new MediaAsset
                {
                    Id = Guid.TryParse(Str(m, "Id"), out var g) ? g : Guid.NewGuid(),
                    Type = MediaAsset.CategoryToType(category),
                    FileName = Str(m, "FileName"),
                    RelativePath = rel,
                    FilePath = rel,
                    Duration = Dbl(m, "DurationSec"),
                    Width = (int)Dbl(m, "Width"),
                    Height = (int)Dbl(m, "Height"),
                    FrameRate = Dbl(m, "FrameRate"),
                    Codec = Str(m, "Codec"),
                    ThumbnailPath = Str(m, "ThumbnailPath"),
                    HasAudio = category is "Audio" or "Video",
                    HasVideo = category != "Audio"
                });
            }
            return assets;
        }

        // helpers — raw rows from the underlying engine
        private List<Dictionary<string, object?>> Query(string sql) => _db.QueryRaw(sql);

        private static string Esc(string s) => s.Replace("'", "''");

        internal static string Str(Dictionary<string, object?> row, string col) => row.TryGetValue(col, out var v) && v != null ? v.ToString() ?? "" : "";

        private static double Dbl(Dictionary<string, object?> row, string col) =>
            row.TryGetValue(col, out var v) && v != null && double.TryParse(v.ToString(), out var d) ? d : 0;

        public void Dispose() => _db.Dispose();
    }
}
