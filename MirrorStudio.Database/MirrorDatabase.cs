using MirrorStudio.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MirrorStudio.Database
{
    public sealed class ProjectSummary
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    /// <summary>MirrorStudio.db — SQLite persistence for projects and all editing data.</summary>
    public sealed class MirrorDatabase : IDisposable
    {
        private readonly LiteDb _db;

        public string Path { get; }

        public MirrorDatabase(string path)
        {
            Path = path;
            _db = new LiteDb(path);
            CreateSchema();
        }

        private void CreateSchema()
        {
            _db.Exec(@"
CREATE TABLE IF NOT EXISTS Projects (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Location TEXT NOT NULL DEFAULT '',
    Width INTEGER NOT NULL DEFAULT 1920,
    Height INTEGER NOT NULL DEFAULT 1080,
    FrameRate REAL NOT NULL DEFAULT 25.0,
    AspectRatio TEXT NOT NULL DEFAULT '16:9',
    AudioSampleRateHz INTEGER NOT NULL DEFAULT 48000,
    DefaultExportFormat TEXT NOT NULL DEFAULT 'MP4 H.264',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS MediaAssets (
    Id TEXT PRIMARY KEY,
    ProjectId TEXT NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
    FileName TEXT NOT NULL,
    RelativePath TEXT NOT NULL,
    MediaType TEXT NOT NULL,
    DurationSec REAL NOT NULL DEFAULT 0,
    Width INTEGER NOT NULL DEFAULT 0,
    Height INTEGER NOT NULL DEFAULT 0,
    AddedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Sequences (
    Id TEXT PRIMARY KEY,
    ProjectId TEXT NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
    Name TEXT NOT NULL,
    Width INTEGER NOT NULL,
    Height INTEGER NOT NULL,
    FrameRate REAL NOT NULL,
    DurationSec REAL NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS Tracks (
    Id TEXT PRIMARY KEY,
    SequenceId TEXT NOT NULL REFERENCES Sequences(Id) ON DELETE CASCADE,
    Kind TEXT NOT NULL,
    TrackIndex INTEGER NOT NULL,
    Name TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Clips (
    Id TEXT PRIMARY KEY,
    SequenceId TEXT NOT NULL REFERENCES Sequences(Id) ON DELETE CASCADE,
    TrackId TEXT NOT NULL,
    Name TEXT NOT NULL,
    SourcePath TEXT NOT NULL DEFAULT '',
    MediaType TEXT NOT NULL,
    StartTime REAL NOT NULL,
    Duration REAL NOT NULL,
    InPoint REAL NOT NULL,
    OutPoint REAL NOT NULL,
    PositionX REAL NOT NULL DEFAULT 0,
    PositionY REAL NOT NULL DEFAULT 0,
    ScaleX REAL NOT NULL DEFAULT 100,
    ScaleY REAL NOT NULL DEFAULT 100,
    Rotation REAL NOT NULL DEFAULT 0,
    Opacity REAL NOT NULL DEFAULT 100
);
CREATE TABLE IF NOT EXISTS Effects (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClipId TEXT NOT NULL REFERENCES Clips(Id) ON DELETE CASCADE,
    Type TEXT NOT NULL,
    Amount REAL NOT NULL DEFAULT 0,
    SortOrder INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS Transitions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClipId TEXT NOT NULL REFERENCES Clips(Id) ON DELETE CASCADE,
    Kind TEXT NOT NULL,
    DurationSec REAL NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS TextElements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClipId TEXT NOT NULL REFERENCES Clips(Id) ON DELETE CASCADE,
    Text TEXT NOT NULL,
    FontFamily TEXT NOT NULL,
    FontSize REAL NOT NULL,
    Bold INTEGER NOT NULL DEFAULT 0,
    Italic INTEGER NOT NULL DEFAULT 0,
    Alignment TEXT NOT NULL DEFAULT 'Center',
    X REAL NOT NULL DEFAULT 50,
    Y REAL NOT NULL DEFAULT 50,
    ColorHex TEXT NOT NULL DEFAULT '#FFFFFF',
    Opacity REAL NOT NULL DEFAULT 100,
    Shadow INTEGER NOT NULL DEFAULT 0,
    Outline INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS AudioSettings (
    ClipId TEXT PRIMARY KEY REFERENCES Clips(Id) ON DELETE CASCADE,
    Volume REAL NOT NULL DEFAULT 100,
    Pan REAL NOT NULL DEFAULT 0,
    GainDb REAL NOT NULL DEFAULT 0,
    FadeIn REAL NOT NULL DEFAULT 0,
    FadeOut REAL NOT NULL DEFAULT 0,
    IsMuted INTEGER NOT NULL DEFAULT 0,
    IsSolo INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS ExportProfiles (
    Id TEXT PRIMARY KEY,
    ProjectId TEXT NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
    Name TEXT NOT NULL,
    Format TEXT NOT NULL,
    VideoCodec TEXT NOT NULL,
    AudioCodec TEXT NOT NULL,
    Width INTEGER NOT NULL,
    Height INTEGER NOT NULL,
    FrameRate REAL NOT NULL
);
CREATE TABLE IF NOT EXISTS Keyframes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClipId TEXT NOT NULL REFERENCES Clips(Id) ON DELETE CASCADE,
    PropertyName TEXT NOT NULL,
    TimeSec REAL NOT NULL,
    Value REAL NOT NULL
);
CREATE TABLE IF NOT EXISTS Settings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_MediaAssets_Project ON MediaAssets(ProjectId);
CREATE INDEX IF NOT EXISTS IX_Clips_Sequence ON Clips(SequenceId);
CREATE INDEX IF NOT EXISTS IX_Effects_Clip ON Effects(ClipId);
");

            // migrations: add probed-metadata columns for DBs created before they existed
            TryAddColumn("MediaAssets", "FrameRate", "REAL NOT NULL DEFAULT 0");
            TryAddColumn("MediaAssets", "Codec", "TEXT NOT NULL DEFAULT ''");
            TryAddColumn("MediaAssets", "ThumbnailPath", "TEXT NOT NULL DEFAULT ''");
        }

        private void TryAddColumn(string table, string column, string definition)
        {
            var exists = _db.Query($"PRAGMA table_info({table})")
                .Any(r => string.Equals(r["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                _db.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        }

        // ---------- project sync ----------

        /// <summary>Full upsert of a project model: replaces all child rows.</summary>
        public void SyncProject(ProjectModel p, string location)
        {
            var now = DateTime.UtcNow.ToString("o");
            var seqId = "seq_" + p.Id;

            _db.Execute("BEGIN");

            try
            {
                _db.Execute(@"INSERT INTO Projects(Id,Name,Location,Width,Height,FrameRate,AspectRatio,AudioSampleRateHz,DefaultExportFormat,CreatedAt,UpdatedAt)
                              VALUES(?,?,?,?,?,?,?,?,?,?,?)
                              ON CONFLICT(Id) DO UPDATE SET
                                Name=?, Location=?, Width=?, Height=?, FrameRate=?, AspectRatio=?,
                                AudioSampleRateHz=?, DefaultExportFormat=?, UpdatedAt=?",
                    p.Id, p.Name, location ?? "", p.Width, p.Height, p.FrameRate, p.AspectRatio,
                    p.AudioSampleRateHz, p.DefaultExportFormat,
                    p.CreatedAt.ToUniversalTime().ToString("o"), now,
                    p.Name, location ?? "", p.Width, p.Height, p.FrameRate, p.AspectRatio,
                    p.AudioSampleRateHz, p.DefaultExportFormat, now);

                double duration = p.Timeline?.Clips.Count == 0 ? 0 : (p.Timeline?.SequenceDuration ?? 0);

                _db.Execute(@"INSERT INTO Sequences(Id,ProjectId,Name,Width,Height,FrameRate,DurationSec)
                              VALUES(?,?,?,?,?,?,?)
                              ON CONFLICT(Id) DO UPDATE SET
                                Name=?, Width=?, Height=?, FrameRate=?, DurationSec=?",
                    seqId, p.Id, "Sequence 1", p.Width, p.Height, p.FrameRate, duration,
                    "Sequence 1", p.Width, p.Height, p.FrameRate, duration);

                // wipe children of the sequence, then rebuild
                _db.Execute(@"DELETE FROM Effects WHERE ClipId IN (SELECT Id FROM Clips WHERE SequenceId=?)", seqId);
                _db.Execute(@"DELETE FROM Transitions WHERE ClipId IN (SELECT Id FROM Clips WHERE SequenceId=?)", seqId);
                _db.Execute(@"DELETE FROM TextElements WHERE ClipId IN (SELECT Id FROM Clips WHERE SequenceId=?)", seqId);
                _db.Execute(@"DELETE FROM AudioSettings WHERE ClipId IN (SELECT Id FROM Clips WHERE SequenceId=?)", seqId);
                _db.Execute(@"DELETE FROM Keyframes WHERE ClipId IN (SELECT Id FROM Clips WHERE SequenceId=?)", seqId);
                _db.Execute("DELETE FROM Clips WHERE SequenceId=?", seqId);
                _db.Execute("DELETE FROM Tracks WHERE SequenceId=?", seqId);

                for (int i = 0; i < TimelineData.VideoTrackCount; i++)
                    _db.Execute("INSERT OR REPLACE INTO Tracks(Id,SequenceId,Kind,TrackIndex,Name) VALUES(?,?,?,?,?)",
                        $"{seqId}:V{i}", seqId, "Video", i, $"V{i + 1}");
                for (int i = 0; i < TimelineData.AudioTrackCount; i++)
                    _db.Execute("INSERT OR REPLACE INTO Tracks(Id,SequenceId,Kind,TrackIndex,Name) VALUES(?,?,?,?,?)",
                        $"{seqId}:A{i}", seqId, "Audio", i, $"A{i + 1}");

                foreach (var c in p.Timeline?.Clips ?? new List<ClipModel>())
                {
                    string trackId = $"{seqId}:{(c.TrackKind == TrackKind.Video ? "V" : "A")}{c.TrackIndex}";
                    _db.Execute(@"INSERT OR REPLACE INTO Clips(Id,SequenceId,TrackId,Name,SourcePath,MediaType,StartTime,Duration,InPoint,OutPoint,
                                  PositionX,PositionY,ScaleX,ScaleY,Rotation,Opacity)
                                  VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                        c.Id, seqId, trackId, c.DisplayName, c.SourcePath ?? "", c.MediaType,
                        c.StartTime, c.Duration, c.InPoint, c.OutPoint,
                        c.PositionX, c.PositionY, c.ScaleX, c.ScaleY, c.Rotation, c.Opacity);

                    int order = 0;
                    foreach (var fx in c.Effects)
                        _db.Execute("INSERT INTO Effects(ClipId,Type,Amount,SortOrder) VALUES(?,?,?,?)",
                            c.Id, fx.Type, fx.Amount, order++);

                    if (c.TransitionIn.Kind != TransitionKind.Cut)
                        _db.Execute("INSERT INTO Transitions(ClipId,Kind,DurationSec) VALUES(?,?,?)",
                            c.Id, c.TransitionIn.Kind.ToString(), 0);

                    if (c.Title != null)
                        _db.Execute(@"INSERT INTO TextElements(ClipId,Text,FontFamily,FontSize,Bold,Italic,Alignment,X,Y,ColorHex,Opacity,Shadow,Outline)
                                      VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)",
                            c.Id, c.Title.Text, c.Title.FontFamily, c.Title.FontSize,
                            c.Title.Bold ? 1 : 0, c.Title.Italic ? 1 : 0, c.Title.Alignment,
                            c.Title.X, c.Title.Y, c.Title.ColorHex, c.Title.Opacity,
                            c.Title.Shadow ? 1 : 0, c.Title.Outline ? 1 : 0);

                    _db.Execute(@"INSERT OR REPLACE INTO AudioSettings(ClipId,Volume,Pan,GainDb,FadeIn,FadeOut,IsMuted,IsSolo)
                                  VALUES(?,?,?,?,?,?,?,?)",
                        c.Id, c.Volume, c.Pan, c.GainDb, c.FadeIn, c.FadeOut,
                        c.IsMuted ? 1 : 0, c.IsSolo ? 1 : 0);
                }

                // media assets — replace-all is fine at V1 scale
                _db.Execute("DELETE FROM MediaAssets WHERE ProjectId=?", p.Id);
                foreach (var m in p.Media)
                {
                    _db.Execute(@"INSERT OR REPLACE INTO MediaAssets(Id,ProjectId,FileName,RelativePath,MediaType,DurationSec,Width,Height,FrameRate,Codec,ThumbnailPath,AddedAt)
                                  VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                        string.IsNullOrEmpty(m.Id) ? Guid.NewGuid().ToString() : m.Id,
                        p.Id, m.FileName, m.RelativePath, m.MediaType,
                        m.DurationSec, m.Width, m.Height, m.FrameRate, m.Codec ?? "",
                        m.ThumbnailRelativePath ?? "", now);
                }

                _db.Execute("DELETE FROM ExportProfiles WHERE ProjectId=?", p.Id);
                foreach (var e in p.ExportPresets)
                {
                    _db.Execute(@"INSERT OR REPLACE INTO ExportProfiles(Id,ProjectId,Name,Format,VideoCodec,AudioCodec,Width,Height,FrameRate)
                                  VALUES(?,?,?,?,?,?,?,?,?)",
                        Guid.NewGuid().ToString(), p.Id, e.Name, e.Format, e.VideoCodec, e.AudioCodec,
                        e.Width, e.Height, e.FrameRate);
                }

                _db.Execute("COMMIT");
            }
            catch
            {
                try { _db.Execute("ROLLBACK"); } catch { /* ignore */ }
                throw;
            }
        }

        // ---------- settings / queries ----------

        /// <summary>Raw read-only query passthrough for facade classes.</summary>
        public List<Dictionary<string, object?>> QueryRaw(string sql) => _db.Query(sql);

        public string GetSetting(string key, string def = "")
        {
            var rows = _db.Query("SELECT Value FROM Settings WHERE Key=?", key);
            return rows.Count > 0 && rows[0]["Value"] != null ? rows[0]["Value"]!.ToString()! : def;
        }

        public void SetSetting(string key, string value) =>
            _db.Execute("INSERT OR REPLACE INTO Settings(Key,Value) VALUES(?,?)", key, value);

        public List<ProjectSummary> ListProjects()
        {
            return _db.Query("SELECT Id,Name,Location,UpdatedAt FROM Projects ORDER BY UpdatedAt DESC")
                .Select(r => new ProjectSummary
                {
                    Id = r["Id"]?.ToString() ?? "",
                    Name = r["Name"]?.ToString() ?? "",
                    Location = r["Location"]?.ToString() ?? "",
                    UpdatedAt = r["UpdatedAt"]?.ToString() ?? ""
                })
                .ToList();
        }

        public void Dispose() => _db.Dispose();
    }
}
