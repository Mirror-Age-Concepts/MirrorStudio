using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using MirrorStudio.Core.Projects;
using MirrorStudio.Export.Models;
using MirrorStudio.Export.Services;
using MirrorStudio.Video.Color;
using MirrorStudio.Video.Effects;
using MirrorStudio.Video.Keyframes;
using MirrorStudio.Video.Transitions;
using Project = MirrorStudio.Core.Projects.Project;

namespace MirrorStudio.Tests;

[TestFixture]
public class VideoEffectFactoryTests
{
    [Test]
    public void Catalog_Types_CreateConcreteEffects()
    {
        foreach (var type in VideoEffectFactory.Catalog)
        {
            var fx = VideoEffectFactory.Create(type, 5);
            Assert.That(fx, Is.Not.Null, $"factory returned null for '{type}'");
            Assert.That(fx!.Name, Is.Not.Empty);
        }
    }

    [Test]
    public void UnknownType_ReturnsNull()
    {
        Assert.That(VideoEffectFactory.Create("DefinitelyNotAnEffect"), Is.Null);
    }

    [Test]
    public void NeutralEffects_BuildNoFilter()
    {
        Assert.That(new TransformEffect().BuildFfmpegFilter(), Is.Null);
        Assert.That(new ColorCorrectionEffect().BuildFfmpegFilter(), Is.Null);
        Assert.That(new BlurEffect { Amount = 0 }.BuildFfmpegFilter(), Is.Null);
        Assert.That(new OpacityEffect { Opacity = 1 }.BuildFfmpegFilter(), Is.Null);
    }

    [Test]
    public void Transform_ScaleAndRotate_AppearInFilter()
    {
        var filter = new TransformEffect { ScaleX = 1.5, ScaleY = 1.5, Rotation = 30 }.BuildFfmpegFilter();

        Assert.Multiple(() =>
        {
            Assert.That(filter, Does.Contain("scale=trunc(iw*1.5/2)*2"));
            Assert.That(filter, Does.Contain("rotate="));
        });
    }

    [Test]
    public void Crop_OnlyWhenRectangleGiven()
    {
        Assert.That(new CropEffect().BuildFfmpegFilter(), Is.Null);
        Assert.That(new CropEffect { X = 10, Y = 20, Width = 640, Height = 360 }.BuildFfmpegFilter(),
            Is.EqualTo("crop=640:360:10:20"));
    }

    [Test]
    public void ColorCorrection_Temperature_MapsToKelvin()
    {
        var filter = new ColorCorrectionEffect { Temperature = 20 }.BuildFfmpegFilter();
        Assert.That(filter, Does.Contain("colortemperature=temperature=6500"));
    }

    [Test]
    public void ChromaKey_IncludesDespill()
    {
        var filter = new ChromaKeyEffect().BuildFfmpegFilter()!;

        Assert.Multiple(() =>
        {
            Assert.That(filter, Does.StartWith("chromakey=0x00FF00:0.25:0.08"));
            Assert.That(filter, Does.Contain("despill=type=green"));
        });
    }

    [Test]
    public void FactorySettings_OverrideDefaults()
    {
        var lut = (LutEffect)VideoEffectFactory.Create("Lut", 50,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["path"] = @"C:\luts\warm.cube",
                ["intensity"] = "0.7",
            })!;

        Assert.Multiple(() =>
        {
            Assert.That(lut.FilePath, Is.EqualTo(@"C:\luts\warm.cube"));
            Assert.That(lut.Intensity, Is.EqualTo(0.7).Within(0.001));
        });

        var key = (ChromaKeyEffect)VideoEffectFactory.Create("ChromaKey", 0,
            new Dictionary<string, string> { ["keyColor"] = "#00B140" })!;
        Assert.That(key.KeyColor, Is.EqualTo("0x00B140"));
    }

    [Test]
    public void Glow_Instances_UseUniqueLabels()
    {
        var a = new GlowEffect { Amount = 0.4 }.BuildFfmpegFilter();
        var b = new GlowEffect { Amount = 0.4 }.BuildFfmpegFilter();

        Assert.Multiple(() =>
        {
            Assert.That(a, Does.Contain("blend=all_mode=screen"));
            // shared labels would short-circuit two splits into one graph
            Assert.That(Labels(a!).Intersect(Labels(b!)), Is.Empty,
                "two glow instances must not share graph labels");
        });
    }

    private static List<string> Labels(string filter) =>
        System.Text.RegularExpressions.Regex.Matches(filter, @"\[([^\]]+)\]")
            .Select(m => m.Groups[1].Value)
            .ToList();
}

[TestFixture]
public class EffectStackTests
{
    private static string TypeName(VideoEffect e) => e.GetType().Name;

    [Test]
    public void Add_SortsIntoCanonicalOrder_Stably()
    {
        var stack = new EffectStack();
        stack.Add(new VignetteEffect());
        stack.Add(new BlurEffect { Amount = 3 });
        stack.Add(new CropEffect { Width = 100, Height = 100 });
        stack.Add(new SharpenEffect());

        Assert.That(stack.Items.Select(TypeName),
            Is.EqualTo(new[] { nameof(CropEffect), nameof(BlurEffect), nameof(SharpenEffect), nameof(VignetteEffect) }));
    }

    [Test]
    public void BuildFilterChain_JoinsEnabledAndSkipsNoOps()
    {
        var stack = new EffectStack();
        stack.Add(new BlurEffect { Amount = 8 });
        stack.Add(new TransformEffect());               // neutral → skipped
        stack.Add(new InvertEffect { Enabled = false }); // disabled → skipped

        Assert.That(stack.BuildFilterChain(), Is.EqualTo("gblur=sigma=8"));
    }

    [Test]
    public void MoveUpMoveDown_ReorderWithinList()
    {
        var stack = new EffectStack();
        stack.Add(new BlurEffect { Amount = 2 });   // sorts first anyway
        var blur = stack.Items[0];
        var invert = new InvertEffect();            // canonical order: after blur
        stack.Insert(stack.Items.Count, invert);

        stack.MoveUp(invert);   // now before blur
        Assert.That(stack.Items[0], Is.SameAs(invert));

        stack.MoveDown(invert);
        Assert.That(stack.Items[0], Is.SameAs(blur));
    }
}

[TestFixture]
public class LutFileTests
{
    private const string CubeText = """
        # small test cube
        TITLE "Test"
        LUT_3D_SIZE 2
        DOMAIN_MIN 0.0 0.0 0.0
        DOMAIN_MAX 1.0 1.0 1.0
        0.0 0.0 0.0
        1.0 0.0 0.0
        0.0 1.0 0.0
        1.0 1.0 0.0
        0.0 0.0 1.0
        1.0 0.0 1.0
        0.0 1.0 1.0
        1.0 1.0 1.0
        """;

    [Test]
    public void Parse_ValidCube_ReadsTitleSizeAndTable()
    {
        var lut = LutFile.Parse(CubeText);

        Assert.Multiple(() =>
        {
            Assert.That(lut.Title, Is.EqualTo("Test"));
            Assert.That(lut.Size, Is.EqualTo(2));
            Assert.That(lut.Table, Has.Count.EqualTo(8));
            Assert.That(lut.IsValid, Is.True);
            Assert.That(lut.Table[^1].R, Is.EqualTo(1f));
        });
    }

    [Test]
    public void ToCubeText_RoundTripsThroughParse()
    {
        var first = LutFile.Parse(CubeText);
        var second = LutFile.Parse(first.ToCubeText());

        Assert.Multiple(() =>
        {
            Assert.That(second.Size, Is.EqualTo(first.Size));
            Assert.That(second.Table.Count, Is.EqualTo(first.Table.Count));
            Assert.That(second.Table[3].G, Is.EqualTo(first.Table[3].G).Within(1e-5));
        });
    }

    [Test]
    public void Parse_ErrorsCarryLineNumber()
    {
        Assert.Throws<FormatException>(() => LutFile.Parse("LUT_3D_SIZE 2\n0.1 0.2 0.3\n"));
        Assert.Throws<FormatException>(() => LutFile.Parse("TITLE \"no size\"\n"));
        Assert.Throws<FormatException>(() => LutFile.Parse("LUT_1D_SIZE 2\n"));
    }

    [Test]
    public void LoaderRejects_BrokenFiles_AndCachesGoodOnes()
    {
        string dir = TestContext.CurrentContext.WorkDirectory;
        string good = Path.Combine(dir, "good.cube");
        string bad = Path.Combine(dir, "bad.cube");
        File.WriteAllText(good, CubeText);
        File.WriteAllText(bad, "not a lut");

        var loader = new LutLoader();

        Assert.Multiple(() =>
        {
            Assert.That(loader.TryLoad(bad, out _), Is.False);
            Assert.That(loader.TryLoad(good, out var lut), Is.True);
            Assert.That(lut!.Table, Has.Count.EqualTo(8));
            Assert.That(loader.TryLoad(@"Z:\missing.cube", out _), Is.False);
        });
    }
}

[TestFixture]
public class KeyframeTrackTests
{
    [Test]
    public void GetValue_ClampsOutsideRange()
    {
        var track = new KeyframeTrack<double>();
        track.Add(0, 10);
        track.Add(10, 20);

        Assert.Multiple(() =>
        {
            Assert.That(track.GetValue(-5), Is.EqualTo(10));
            Assert.That(track.GetValue(99), Is.EqualTo(20));
        });
    }

    [Test]
    public void Linear_InterpolatesMidpoint()
    {
        var track = new KeyframeTrack<double>();
        track.Add(0, 0);
        track.Add(10, 100);

        Assert.Multiple(() =>
        {
            Assert.That(track.GetValue(5), Is.EqualTo(50).Within(1e-9));
            Assert.That(track.GetValue(2.5), Is.EqualTo(25).Within(1e-9));
        });
    }

    [Test]
    public void Smooth_EasingStaysBetweenKeysButDiffersFromLinear()
    {
        var linear = new KeyframeTrack<double>();
        linear.Add(0, 0, "Linear");
        linear.Add(10, 100, "Linear");

        var smooth = new KeyframeTrack<double>();
        smooth.Add(0, 0, "Smooth");
        smooth.Add(10, 100, "Smooth");

        double sMid = smooth.GetValue(5);
        Assert.Multiple(() =>
        {
            Assert.That(sMid, Is.EqualTo(50).Within(1e-9));   // symmetric ease has same midpoint
            Assert.That(smooth.GetValue(2.5), Is.EqualTo(15.625).Within(1e-9)); // t²(3-2t)
            Assert.That(linear.GetValue(2.5), Is.EqualTo(25).Within(1e-9));
        });
    }

    [Test]
    public void EmptyTrack_ReturnsDefault()
    {
        Assert.That(new KeyframeTrack<double>().GetValue(3), Is.EqualTo(0d));
    }
}

[TestFixture]
public class TransitionTests
{
    [Test]
    public void Factory_MapsKinds()
    {
        Assert.That(TransitionFactory.Create("Cut", 0.5), Is.Null);
        Assert.That(TransitionFactory.Create("CrossDissolve", 0.5), Is.InstanceOf<CrossDissolveTransition>());
        Assert.That(TransitionFactory.Create("Wipe", 0.5), Is.InstanceOf<WipeTransition>());
        Assert.That(TransitionFactory.Create("Slide", 0.5), Is.InstanceOf<SlideTransition>());
        Assert.That(TransitionFactory.Create("Zoom", 0.5), Is.InstanceOf<ZoomTransition>());
        Assert.That(TransitionFactory.Create("DipToWhite", 0.5), Is.InstanceOf<FadeThroughColorTransition>());
        Assert.That(((FadeThroughColorTransition)TransitionFactory.Create("DipToWhite", 0.5)!).Color,
            Is.EqualTo("#FFFFFF"));
        Assert.That(TransitionFactory.Create("WhoKnows", 0.5), Is.Null);
    }

    [Test]
    public void XfadeVariants_EmitDuration()
    {
        Assert.That(new CrossDissolveTransition(0.75).BuildXfadeArgs(),
            Is.EqualTo("transition=fade:duration=0.75"));
        Assert.That(new WipeTransition("diagonal", 0.5).BuildXfadeArgs(),
            Does.Contain("transition=diagtl"));
        Assert.That(new SlideTransition("up", 0.5).BuildXfadeArgs(),
            Does.Contain("transition=slideup"));
        Assert.That(new ZoomTransition(0.5).BuildXfadeArgs(),
            Does.Contain("transition=zoomin"));
    }

    [Test]
    public void Dip_Transition_HasNoXfadeButEmitsFades()
    {
        var dip = new FadeThroughColorTransition("#FFFFFF", 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(dip.BuildXfadeArgs(), Is.Null);
            var (fadeOut, fadeIn) = dip.BuildFadeFilters(totalDurationOfA: 5);
            Assert.That(fadeOut, Is.EqualTo("fade=t=out:st=4.5:d=0.5:c=0xFFFFFF"));
            Assert.That(fadeIn, Is.EqualTo("fade=t=in:st=0:d=0.5:c=0xFFFFFF"));
        });
    }
}

[TestFixture]
public class FilterGraphTransitionTests
{
    /// <summary>Two video clips back to back; the second carries the incoming transition.</summary>
    private static (MirrorStudio.Core.Projects.Project project, ExportSettings settings) MakeTwoClips(
        string transitionKind = "Cut", double duration = 0.5)
    {
        var project = new MirrorStudio.Core.Projects.Project();
        var media1 = new ProjectMedia { FileName = "a", FilePath = @"C:\media\a.mp4" };
        var media2 = new ProjectMedia { FileName = "b", FilePath = @"C:\media\b.mp4" };
        project.Media.Add(media1);
        project.Media.Add(media2);

        var track = new TimelineTrackData { Name = "V1", Type = "video", Index = 0 };
        track.Clips.Add(new TimelineClipData
        {
            MediaAssetId = media1.Id, Name = "A", Start = 0, Duration = 5, SourceStart = 0, SourceEnd = 5,
        });
        track.Clips.Add(new TimelineClipData
        {
            MediaAssetId = media2.Id, Name = "B", Start = 5, Duration = 4, SourceStart = 0, SourceEnd = 4,
            TransitionKind = transitionKind, TransitionDurationSec = duration,
        });
        project.Tracks.Add(track);

        return (project, new ExportSettings
        {
            OutputPath = @"C:\out\film.mp4",
            Width = 1920,
            Height = 1080,
            FrameRate = 25,
        });
    }

    [Test]
    public void HardCut_UsesPlainConcat()
    {
        var plan = new FilterGraphBuilder(_ => true).Build(MakeTwoClips().project, MakeTwoClips().settings);

        Assert.Multiple(() =>
        {
            Assert.That(plan.FilterGraph, Does.Contain("concat=n=2:v=1:a=0[vx1]"));
            Assert.That(plan.FilterGraph, Does.Contain("[vx1]null[outv]"));
            Assert.That(plan.FilterGraph, Does.Not.Contain("xfade"));
        });
    }

    [Test]
    public void CrossDissolve_HoldsLastFrame_AndPreservesTotalLength()
    {
        var (project, settings) = MakeTwoClips("CrossDissolve", 0.6);
        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.Multiple(() =>
        {
            // A's tail is cloned so the dissolve eats no timeline length
            Assert.That(plan.FilterGraph, Does.Contain("tpad=stop_mode=clone:stop_duration=0.6[vh1]"));
            Assert.That(plan.FilterGraph, Does.Contain("xfade=transition=fade:duration=0.6:offset=5[vx1]"));
            Assert.That(plan.FilterGraph, Does.Contain("[vx1]null[outv]"));
            Assert.That(plan.TotalDuration, Is.EqualTo(9).Within(1e-9));
        });
    }

    [Test]
    public void Wipe_UsesWipeleftVariant()
    {
        var (project, settings) = MakeTwoClips("Wipe", 0.5);
        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.That(plan.FilterGraph, Does.Contain("xfade=transition=wipeleft:duration=0.5:offset=5"));
    }

    [Test]
    public void DipToWhite_FadesBothSides_KeepsConcat()
    {
        var (project, settings) = MakeTwoClips("DipToWhite", 1.0);
        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.Multiple(() =>
        {
            Assert.That(plan.FilterGraph, Does.Contain("fade=t=out:st=4.5:d=0.5:c=0xFFFFFF[vd1]"));
            Assert.That(plan.FilterGraph, Does.Contain("[v1]fade=t=in:st=0:d=0.5:c=0xFFFFFF[ve1]"));
            Assert.That(plan.FilterGraph, Does.Contain("[vd1][ve1]concat=n=2:v=1:a=0[vx1]"));
        });
    }

    [Test]
    public void ClipEffects_InsertBeforeFitStage_AndRespectOrder()
    {
        var (project, settings) = MakeTwoClips();
        var clip = project.Tracks[0].Clips[0];
        clip.Effects.Add(new ClipEffectData { Type = "Vignette", Amount = 0.5 });
        clip.Effects.Add(new ClipEffectData { Type = "Blur", Amount = 8 });

        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        int blurAt = plan.FilterGraph.IndexOf("gblur=sigma=8", StringComparison.Ordinal);
        int vignetteAt = plan.FilterGraph.IndexOf("vignette=angle=", StringComparison.Ordinal);
        int scaleAt = plan.FilterGraph.IndexOf("scale=1920:1080", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(blurAt, Is.GreaterThan(-1));
            Assert.That(vignetteAt, Is.GreaterThan(-1));
            // canonical order: color work happens before the fit scale/pad
            Assert.That(scaleAt, Is.GreaterThan(blurAt));
            Assert.That(scaleAt, Is.GreaterThan(vignetteAt));
        });
    }

    [Test]
    public void DisabledOrUnknown_Effects_AreSkipped()
    {
        var (project, settings) = MakeTwoClips();
        var clip = project.Tracks[0].Clips[0];
        clip.Effects.Add(new ClipEffectData { Type = "Blur", Amount = 8, Enabled = false });
        clip.Effects.Add(new ClipEffectData { Type = "MysteryBox", Amount = 3 });

        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.That(plan.FilterGraph, Does.Not.Contain("gblur"));
    }

    [Test]
    public void ClipOpacity_BecomesAlphaBlend()
    {
        var (project, settings) = MakeTwoClips();
        project.Tracks[0].Clips[0].Opacity = 0.4;

        var plan = new FilterGraphBuilder(_ => true).Build(project, settings);

        Assert.That(plan.FilterGraph, Does.Contain("format=rgba,colorchannelmixer=aa=0.4"));
    }

    [Test]
    public void Mapper_LegacyBlur_RoundTripsThroughCanonicalUnits()
    {
        var model = new MirrorStudio.Core.Models.ClipModel
        {
            Name = "c",
            SourcePath = @"C:\media\a.mp4",
            TrackKind = MirrorStudio.Core.Models.TrackKind.Video,
        };
        model.Effects.Add(new MirrorStudio.Core.Models.EffectModel { Type = "Blur", Amount = 40 });
        model.TransitionIn = new MirrorStudio.Core.Models.ClipTransition
        {
            Kind = MirrorStudio.Core.Models.TransitionKind.CrossDissolve,
            DurationSeconds = 0.8,
        };

        var session = new MirrorStudio.Core.Models.ProjectModel();
        session.Timeline.Clips.Add(model);

        var project = session.ToProject(rel => rel);
        var clipData = project.Tracks.Single().Clips.Single();

        Assert.Multiple(() =>
        {
            // 40% legacy blur → sigma 6 in effect units
            Assert.That(clipData.Effects.Single().Type, Is.EqualTo("Blur"));
            Assert.That(clipData.Effects.Single().Amount, Is.EqualTo(6).Within(1e-9));
            Assert.That(clipData.TransitionKind, Is.EqualTo("CrossDissolve"));
            Assert.That(clipData.TransitionDurationSec, Is.EqualTo(0.8).Within(1e-9));
        });

        var back = ProjectMapper.ToProjectModel(project);
        var clipBack = back.Timeline.Clips.Single();

        Assert.Multiple(() =>
        {
            Assert.That(clipBack.Effects.Single().Type, Is.EqualTo("Blur"));
            Assert.That(clipBack.Effects.Single().Amount, Is.EqualTo(40).Within(0.01));
            Assert.That(clipBack.TransitionIn!.Kind,
                Is.EqualTo(MirrorStudio.Core.Models.TransitionKind.CrossDissolve));
            Assert.That(clipBack.TransitionIn.DurationSeconds, Is.EqualTo(0.8).Within(1e-9));
        });
    }
}
