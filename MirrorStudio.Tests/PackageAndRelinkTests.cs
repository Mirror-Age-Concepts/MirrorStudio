using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using MirrorStudio.Core.Models;
using MirrorStudio.Core.Projects;
using MirrorStudio.Media.Services;
using MirrorStudio.Project.Services;
using Project = MirrorStudio.Core.Projects.Project;
using ProjectPackageService = MirrorStudio.Project.Services.ProjectPackageService;

namespace MirrorStudio.Tests;

[TestFixture]
public class ProjectPackageServiceTests
{
    private static MirrorStudio.Core.Projects.Project MakeCanonicalProject()
    {
        var project = new MirrorStudio.Core.Projects.Project
        {
            Name = "PackageTest",
            Settings = { Width = 1280, Height = 720, FrameRate = 24 },
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        };
        var mediaId = Guid.NewGuid();
        project.Media.Add(new ProjectMedia
        {
            Id = mediaId,
            FileName = "clip",
            FilePath = "Media/Video/clip.mp4",
            Duration = 12.5,
            Width = 1280,
            Height = 720,
            FrameRate = 24,
        });
        project.Tracks.Add(new TimelineTrackData
        {
            Name = "V1",
            Type = "video",
            Index = 0,
            Clips =
            {
                new TimelineClipData
                {
                    MediaAssetId = mediaId,
                    Name = "clip",
                    Start = 2,
                    Duration = 8,
                    SourceStart = 1,
                    SourceEnd = 9,
                },
            },
        });
        return project;
    }

    [Test]
    public async Task SaveAndLoad_RoundTripsProject()
    {
        var service = new ProjectPackageService();
        var path = Path.Combine(Path.GetTempPath(), $"ms_pkg_{Guid.NewGuid():N}.msp");

        try
        {
            var original = MakeCanonicalProject();
            await service.SaveAsync(original, path);

            Assert.That(Path.GetExtension(path), Is.EqualTo(".msp"));
            using (var zip = ZipFile.OpenRead(path))
            {
                Assert.That(zip.Entries.Any(e => e.FullName == "project.json"), Is.True);
            }

            var loaded = await service.LoadAsync(path);

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Name, Is.EqualTo("PackageTest"));
                Assert.That(loaded.Settings.Width, Is.EqualTo(1280));
                Assert.That(loaded.Settings.FrameRate, Is.EqualTo(24));
                Assert.That(loaded.Media, Has.Count.EqualTo(1));
                Assert.That(loaded.Media[0].FilePath, Is.EqualTo("Media/Video/clip.mp4"));
                Assert.That(loaded.Tracks[0].Clips[0].Start, Is.EqualTo(2));
                Assert.That(loaded.Tracks[0].Clips[0].SourceEnd, Is.EqualTo(9));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void LoadAsync_NonZipFile_ThrowsInvalidData()
    {
        var service = new ProjectPackageService();
        var path = Path.Combine(Path.GetTempPath(), $"ms_bad_{Guid.NewGuid():N}.msp");
        File.WriteAllText(path, "this is not a zip archive");

        try
        {
            Assert.ThrowsAsync<InvalidDataException>(async () => await service.LoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void LoadAsync_MissingFile_ThrowsFileNotFound()
    {
        var service = new ProjectPackageService();
        var path = Path.Combine(Path.GetTempPath(), $"ms_missing_{Guid.NewGuid():N}.msp");

        Assert.ThrowsAsync<FileNotFoundException>(async () => await service.LoadAsync(path));
    }
}

[TestFixture]
public class ProjectMapperTests
{
    [Test]
    public void ToProject_MapsTracksByKindAndSettings()
    {
        var model = new ProjectModel
        {
            Name = "Mapped",
            Width = 3840,
            Height = 2160,
            FrameRate = 60,
            AudioSampleRateHz = 44100,
        };
        model.Media.Add(new MediaEntry
        {
            FileName = "a",
            RelativePath = "Media/a.mp4",
            MediaType = "video",
            DurationSec = 10,
            Probed = true,
        });
        model.Media.Add(new MediaEntry
        {
            FileName = "b",
            RelativePath = "Media/b.wav",
            MediaType = "audio",
            DurationSec = 8,
            Probed = true,
        });
        model.Timeline.Clips.Add(new ClipModel
        {
            Name = "a.mp4",
            SourcePath = "Media/a.mp4",
            MediaType = "video",
            TrackKind = TrackKind.Video,
            TrackIndex = 0,
            StartTime = 0,
            Duration = 5,
            InPoint = 0,
            OutPoint = 5,
            Volume = 80,
            Opacity = 100,
        });
        model.Timeline.Clips.Add(new ClipModel
        {
            Name = "b.wav",
            SourcePath = "Media/b.wav",
            MediaType = "audio",
            TrackKind = TrackKind.Audio,
            TrackIndex = 0,
            StartTime = 1,
            Duration = 4,
            InPoint = 0.5,
            OutPoint = 4.5,
            Volume = 50,
        });

        var canonical = model.ToProject(rel => rel);

        Assert.Multiple(() =>
        {
            Assert.That(canonical.Name, Is.EqualTo("Mapped"));
            Assert.That(canonical.Settings.Width, Is.EqualTo(3840));
            Assert.That(canonical.Settings.AudioSampleRate, Is.EqualTo("44100"));

            var video = canonical.Tracks.Single(t => t.Type == "video");
            var audio = canonical.Tracks.Single(t => t.Type == "audio");
            Assert.That(video.Clips, Has.Count.EqualTo(1));
            Assert.That(audio.Clips, Has.Count.EqualTo(1));

            Assert.That(video.Clips[0].SourceStart, Is.EqualTo(0));
            Assert.That(video.Clips[0].Opacity, Is.EqualTo(1.0).Within(0.001));

            Assert.That(audio.Clips[0].SourceStart, Is.EqualTo(0.5));
            Assert.That(audio.Clips[0].Volume, Is.EqualTo(0.5).Within(0.001));

            // media assets are deduplicated by relative path and referenced by id
            Assert.That(canonical.Media, Has.Count.EqualTo(2));
            Assert.That(
                video.Clips[0].MediaAssetId,
                Is.EqualTo(canonical.Media.First(m => m.FilePath == "Media/a.mp4").Id));
        });
    }

    [Test]
    public async Task RoundTrip_ModelToCanonicalToModel_PreservesTimelineFacts()
    {
        var model = new ProjectModel { Name = "Round", Width = 1920, Height = 1080, FrameRate = 25 };
        model.Media.Add(new MediaEntry
        {
            FileName = "c",
            RelativePath = "Media/c.mp4",
            MediaType = "video",
            DurationSec = 20,
            Probed = true,
        });
        model.Timeline.Clips.Add(new ClipModel
        {
            Name = "c",
            SourcePath = "Media/c.mp4",
            MediaType = "video",
            TrackKind = TrackKind.Video,
            TrackIndex = 1,
            StartTime = 3,
            Duration = 6,
            InPoint = 2,
            OutPoint = 8,
            Volume = 90,
        });

        var canonical = model.ToProject(rel => rel);
        var back = ProjectMapper.ToProjectModel(canonical);
        var clip = back.Timeline.Clips.Single();

        Assert.Multiple(() =>
        {
            Assert.That(back.Name, Is.EqualTo("Round"));
            Assert.That(clip.SourcePath, Is.EqualTo("Media/c.mp4"));
            Assert.That(clip.TrackKind, Is.EqualTo(TrackKind.Video));
            Assert.That(clip.TrackIndex, Is.EqualTo(1));
            Assert.That(clip.StartTime, Is.EqualTo(3));
            Assert.That(clip.Duration, Is.EqualTo(6));
            Assert.That(clip.InPoint, Is.EqualTo(2));
            Assert.That(clip.OutPoint, Is.EqualTo(8));
            Assert.That(clip.Volume, Is.EqualTo(90).Within(0.01));
            Assert.That(back.Media.Single().RelativePath, Is.EqualTo("Media/c.mp4"));
        });
    }

    [Test]
    public void TotalDuration_TakesMaxClipEnd()
    {
        var model = new ProjectModel();
        model.Timeline.Clips.Add(new ClipModel
        {
            SourcePath = "x", TrackKind = TrackKind.Video, TrackIndex = 0, StartTime = 10, Duration = 4,
        });

        var canonical = ProjectMapper.ToProject(model);

        Assert.That(ProjectMapper.TotalDuration(canonical), Is.EqualTo(TimeSpan.FromSeconds(14)));
    }
}

[TestFixture]
public class MediaRelinkServiceTests : IDisposable
{
    private readonly string _root;

    public MediaRelinkServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ms_relink_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MirrorStudio.Core.Projects.Project ProjectWithOffline(string fileName)
    {
        var project = new MirrorStudio.Core.Projects.Project();
        project.Media.Add(new ProjectMedia
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            FilePath = @"Z:\nowhere\" + fileName + ".mp4",
        });
        return project;
    }

    [Test]
    public void FindOffline_ListsMissingFilesOnly()
    {
        var project = ProjectWithOffline("gone");
        var presentPath = Path.Combine(_root, "here.mp4");
        File.WriteAllText(presentPath, "x");
        project.Media.Add(new ProjectMedia { Id = Guid.NewGuid(), FileName = "here", FilePath = presentPath });

        var service = new MediaRelinkService();

        Assert.Multiple(() =>
        {
            Assert.That(service.FindOffline(project).Select(m => m.FileName), Is.EqualTo(new[] { "gone" }));
            Assert.That(service.IsOffline(project.Media[0]), Is.True);
            Assert.That(service.IsOffline(project.Media[1]), Is.False);
        });
    }

    [Test]
    public async Task RelinkAsync_UpdatesPathWhenFileExists()
    {
        var project = ProjectWithOffline("gone");
        var newPath = Path.Combine(_root, "found.mp4");
        File.WriteAllText(newPath, "x");

        var service = new MediaRelinkService();
        bool ok = await service.RelinkAsync(project.Media[0], newPath);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(project.Media[0].FilePath, Is.EqualTo(Path.GetFullPath(newPath)));
            Assert.That(project.Media[0].FileName, Is.EqualTo("found"));
            Assert.That(service.IsOffline(project.Media[0]), Is.False);
        });
    }

    [Test]
    public async Task SearchFolderAsync_MatchesByNameRecursively()
    {
        var project = ProjectWithOffline("wanted");
        Directory.CreateDirectory(Path.Combine(_root, "sub", "deep"));
        var match = Path.Combine(_root, "sub", "deep", "wanted.mkv");
        File.WriteAllText(match, "x");
        project.Media.Add(new ProjectMedia
        {
            Id = Guid.NewGuid(),
            FileName = "unfindable",
            FilePath = @"Z:\nowhere\unfindable.mp4",
        });

        var service = new MediaRelinkService();
        var relinked = await service.SearchFolderAsync(project, _root);

        Assert.Multiple(() =>
        {
            Assert.That(relinked.Select(m => m.FileName), Is.EqualTo(new[] { "wanted" }));
            Assert.That(project.Media[0].FilePath, Is.EqualTo(Path.GetFullPath(match)));
            Assert.That(project.Media[1].FilePath, Is.EqualTo(@"Z:\nowhere\unfindable.mp4"));
        });
    }
}
