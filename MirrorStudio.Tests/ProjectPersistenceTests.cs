using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using MirrorStudio.Core.Models;
using MirrorStudio.Project.Services;

namespace MirrorStudio.Tests;

[TestFixture]
public class ProjectPersistenceTests
{
    // ---------- ProjectSerializer ----------

    [Test]
    public async Task ProjectSerializer_RoundTripsProjectGraph()
    {
        var serializer = new ProjectSerializer();
        var path = Path.Combine(Path.GetTempPath(), $"ms_serializer_{Guid.NewGuid():N}.json");

        try
        {
            var project = new ProjectModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = "RoundTrip",
                Width = 1920,
                Height = 1080,
                FrameRate = 30,
            };
            project.Timeline.Clips.Add(new ClipModel
            {
                Name = "Interview.mp4",
                SourcePath = "Media/Video/Interview.mp4",
                StartTime = 1.5,
                Duration = 7.25,
                InPoint = 0.5,
                OutPoint = 7.75,
                TrackIndex = 2
            });

            await serializer.SaveAsync(project, path);
            Assert.That(File.Exists(path), Is.True);

            var loaded = await serializer.LoadAsync(path);
            Assert.That(loaded, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(loaded!.Name, Is.EqualTo("RoundTrip"));
                Assert.That(loaded.Width, Is.EqualTo(1920));
                Assert.That(loaded.Timeline.Clips, Has.Count.EqualTo(1));
                Assert.That(loaded.Timeline.Clips[0].Name, Is.EqualTo("Interview.mp4"));
                Assert.That(loaded.Timeline.Clips[0].Duration, Is.EqualTo(7.25));
                Assert.That(loaded.Timeline.Clips[0].TrackIndex, Is.EqualTo(2));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ProjectSerializer_LoadMissing_ReturnsNull()
    {
        var serializer = new ProjectSerializer();
        var loaded = await serializer.LoadAsync(
            Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json"));
        Assert.That(loaded, Is.Null);
    }

    // ---------- ProjectRecoveryService ----------

    [Test]
    public async Task ProjectRecoveryService_WriteFindDiscard()
    {
        var recovery = new ProjectRecoveryService();
        var project = new ProjectModel { Name = $"RecoverTest_{Guid.NewGuid():N}" };

        var path = await recovery.WriteAutosaveAsync(project);
        Assert.That(path, Is.Not.Null);
        Assert.That(File.Exists(path), Is.True);

        var candidates = recovery.FindRecoveryCandidates();
        Assert.That(candidates.Any(c => c.Path == path), Is.True);

        var loaded = await recovery.LoadRecoveryAsync(path!);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Name, Is.EqualTo(project.Name));

        recovery.DiscardRecovery(path!);
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void ProjectRecoveryService_SanitizesFileName()
    {
        var recovery = new ProjectRecoveryService();
        var fileName = Path.GetFileName(recovery.AutosavePathFor("my: bad/file\u0000name"));

        Assert.That(fileName, Does.EndWith(".autosave"));
        foreach (var c in Path.GetInvalidFileNameChars())
            Assert.That(fileName.Contains(c), Is.False, $"invalid char {(int)c} leaked into file name");
    }
}
