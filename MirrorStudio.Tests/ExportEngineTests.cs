using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Models;
using MirrorStudio.Export.Services;
using Project = MirrorStudio.Core.Projects.Project;

namespace MirrorStudio.Tests;

[TestFixture]
public class FilterGraphBuilderTests
{
    private static (MirrorStudio.Core.Projects.Project project, ExportSettings settings) MakeProject(
        params (double start, double dur, double srcIn, double srcOut, bool offline, bool audio)[] clips)
    {
        var project = new MirrorStudio.Core.Projects.Project();
        var mediaIndex = 0;
        var videoTrack = new TimelineTrackData { Name = "V1", Type = "video", Index = 0 };
        var audioTrack = new TimelineTrackData { Name = "A1", Type = "audio", Index = 0 };

        foreach (var (start, dur, srcIn, srcOut, offline, audio) in clips)
        {
            Guid id = Guid.NewGuid();
            string path = offline ? @"Z:\gone\missing.mp4" : $@"C:\media\file{mediaIndex}.mp4";
            project.Media.Add(new ProjectMedia { Id = id, FileName = $"file{mediaIndex}", FilePath = path });
            mediaIndex++;

            (audio ? audioTrack : videoTrack).Clips.Add(new TimelineClipData
            {
                MediaAssetId = id,
                Name = "clip",
                Start = start,
                Duration = dur,
                SourceStart = srcIn,
                SourceEnd = srcOut,
            });
        }

        if (videoTrack.Clips.Count > 0) project.Tracks.Add(videoTrack);
        if (audioTrack.Clips.Count > 0) project.Tracks.Add(audioTrack);

        var settings = new ExportSettings
        {
            OutputPath = @"C:\out\film.mp4",
            Width = 1920,
            Height = 1080,
            FrameRate = 25,
        };
        return (project, settings);
    }

    [Test]
    public void MultiClip_BuildsTrimSetptsAndConcat()
    {
        var (project, settings) = MakeProject(
            (0, 5, 10, 15, false, false),
            (5, 4, 0, 4, false, false),
            (9, 6, 30, 36, false, false));

        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.Multiple(() =>
        {
            Assert.That(plan.VideoClipCount, Is.EqualTo(3));
            Assert.That(plan.HasVideo, Is.True);
            Assert.That(plan.HasAudio, Is.False);
            Assert.That(plan.FilterGraph, Does.Contain("trim=start=10:end=15,setpts=PTS-STARTPTS"));
            // per-boundary concatenation: two cuts → two 2-input concat stages
            Assert.That(plan.FilterGraph, Does.Contain("concat=n=2:v=1:a=0[vx1]"));
            Assert.That(plan.FilterGraph, Does.Contain("concat=n=2:v=1:a=0[vx2]"));
            Assert.That(plan.FilterGraph, Does.Contain("[vx2]null[outv]"));
            Assert.That(plan.Inputs, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void AudioClips_GetAtrimAsetptsAndAudioConcat()
    {
        var (project, settings) = MakeProject(
            (0, 5, 2, 7, false, true),
            (5, 5, 0, 5, false, true));

        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.Multiple(() =>
        {
            Assert.That(plan.AudioClipCount, Is.EqualTo(2));
            Assert.That(plan.HasAudio, Is.True);
            Assert.That(plan.FilterGraph, Does.Contain("atrim=start=2:end=7,asetpts=PTS-STARTPTS"));
            Assert.That(plan.FilterGraph, Does.Contain("concat=n=2:v=0:a=1[outa]"));
        });
    }

    [Test]
    public void OfflineMedia_IsReplacedWithPlaceholders()
    {
        var (project, settings) = MakeProject(
            (0, 3, 0, 3, true, false));

        var plan = new FilterGraphBuilder(_ => false).Build(project, settings);

        Assert.Multiple(() =>
        {
            Assert.That(plan.OfflineMedia, Has.Count.EqualTo(1));
            Assert.That(plan.Inputs[0].PreInputArgs, Does.Contain("-f lavfi"));
            Assert.That(plan.Inputs[0].Path, Does.Contain("color=c=black"));
            Assert.That(plan.VideoClipCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void AudioOnly_SkipsVideoChains()
    {
        var (project, settings) = MakeProject(
            (0, 5, 0, 5, false, true));
        settings.ExportAudioOnly = true;

        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.Multiple(() =>
        {
            Assert.That(plan.HasVideo, Is.False);
            Assert.That(plan.VideoClipCount, Is.EqualTo(0));
            Assert.That(plan.FilterGraph, Does.Not.Contain("[outv]"));
            Assert.That(plan.FilterGraph, Does.Contain("[outa]"));
        });
    }

    [Test]
    public void Volume_Adjustment_AppearsInChain()
    {
        var (project, settings) = MakeProject(
            (0, 4, 0, 4, false, true));
        project.Tracks[0].Clips[0].Volume = 0.5;

        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.That(plan.FilterGraph, Does.Contain("volume=0.5"));
    }
}

[TestFixture]
public class FfmpegRenderEngineArgumentTests
{
    [Test]
    public void BuildArguments_MapsOutputsAndCodecs()
    {
        var engine = new FfmpegRenderEngine(new FfmpegProcessRunner("ffmpeg"));
        var plan = new RenderPlan
        {
            HasVideo = true,
            HasAudio = true,
            VideoClipCount = 2,
            AudioClipCount = 1,
            TotalDuration = 10,
            Inputs = { new PlanInput { Path = "\"a.mp4\"" }, new PlanInput { Path = "\"b.mp4\"" } },
            FilterGraph = "[0:v]fps=25[v0];[1:v]fps=25[v1];[v0][v1]concat=n=2:v=1:a=0[outv]",
        };
        var settings = new ExportSettings
        {
            OutputPath = @"C:\out\final.mp4",
            VideoCodec = "libx264",
            Crf = 18,
            AudioBitrate = 192,
        };

        var args = engine.BuildArguments(settings, plan);

        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Contain("-map [outv]"));
            Assert.That(args, Does.Contain("-map [outa]"));
            Assert.That(args, Does.Contain("-c:v libx264 -preset medium -crf 18"));
            Assert.That(args, Does.Contain("-c:a aac -b:a 192k"));
            Assert.That(args, Does.Contain("-pix_fmt yuv420p"));
            Assert.That(args, Does.EndWith("\"C:\\out\\final.mp4\""));
        });
    }

    [Test]
    public void BuildArguments_NoAudio_AddsAnFlag()
    {
        var engine = new FfmpegRenderEngine(new FfmpegProcessRunner("ffmpeg"));
        var plan = new RenderPlan { HasVideo = true, HasAudio = false, VideoClipCount = 1 };
        var settings = new ExportSettings { OutputPath = "o.mp4" };

        var args = engine.BuildArguments(settings, plan);

        Assert.That(args, Does.Contain("-an "));
    }

    [Test]
    public void BuildArguments_AudioOnly_OmitsVideo()
    {
        var engine = new FfmpegRenderEngine(new FfmpegProcessRunner("ffmpeg"));
        var plan = new RenderPlan { HasVideo = false, HasAudio = true, AudioClipCount = 1 };
        var settings = new ExportSettings { OutputPath = "o.wav", ExportAudioOnly = true };

        var args = engine.BuildArguments(settings, plan);

        Assert.Multiple(() =>
        {
            Assert.That(args, Does.Not.Contain("-map [outv]"));
            Assert.That(args, Does.Contain("-vn "));
        });
    }
}

[TestFixture]
public class ExportProgressParserTests
{
    [TestCase("frame=  100 fps=25 q=28.0 size=    1024kB time=00:00:04.00 bitrate=   2.1kbits/s speed=1x", 4.0)]
    [TestCase("time=00:01:03.50", 63.5)]
    [TestCase("Press [q] to stop", -1)]
    public void TryParseTime_ExtractsSeconds(string line, double expected)
    {
        var ok = ExportProgressParser.TryParseTime(line, out var time);

        if (expected < 0)
        {
            Assert.That(ok, Is.False);
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(time.TotalSeconds, Is.EqualTo(expected));
            });
        }
    }

    [Test]
    public void ToProgress_ComputesPercentageAndRemaining()
    {
        var elapsed = Stopwatch.StartNew();
        Thread.Sleep(450); // parser needs >250 ms of history before it estimates remaining time

        var p = ExportProgressParser.ToProgress(
            "time=00:00:50.00 bitrate=1234kbits/s", totalSeconds: 100, elapsed)!;

        Assert.Multiple(() =>
        {
            Assert.That(p.Percentage, Is.EqualTo(50).Within(0.1));
            Assert.That(p.RenderedDuration.TotalSeconds, Is.EqualTo(50));
            Assert.That(p.EstimatedRemaining.TotalSeconds, Is.GreaterThan(0));
        });
    }
}
