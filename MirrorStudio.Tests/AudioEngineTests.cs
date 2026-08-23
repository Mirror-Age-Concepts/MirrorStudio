using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MirrorStudio.Audio.Effects;
using MirrorStudio.Audio.Interfaces;
using MirrorStudio.Audio.Models;
using MirrorStudio.Audio.Services;

namespace MirrorStudio.Tests;

[TestFixture]
public class AudioMathTests
{
    [TestCase(0.0, 1.0)]
    [TestCase(-6.0, 0.5012)]
    [TestCase(-12.0, 0.2512)]
    [TestCase(-20.0, 0.1)]
    public void DbToLinear_MatchesSpecTable(double db, double expectedLinear)
    {
        Assert.That(AudioMath.DbToLinear(db), Is.EqualTo(expectedLinear).Within(0.0005));
    }

    [Test]
    public void LinearToDb_RoundTrips()
    {
        Assert.That(AudioMath.LinearToDb(AudioMath.DbToLinear(-8.5)), Is.EqualTo(-8.5).Within(0.001));
        Assert.That(AudioMath.LinearToDb(0), Is.EqualTo(double.NegativeInfinity));
    }

    [Test]
    public void PanGains_FollowsEqualPowerLaw()
    {
        var (lHard, rHard) = AudioMath.PanGains(-1);
        var (lCenter, rCenter) = AudioMath.PanGains(0);
        var (lRight, rRight) = AudioMath.PanGains(+1);

        Assert.Multiple(() =>
        {
            Assert.That(lHard, Is.EqualTo(1.0).Within(0.001));
            Assert.That(rHard, Is.EqualTo(0).Within(0.001));
            // center splits √½ per channel so perceived loudness stays constant
            double halfRoot = Math.Sqrt(0.5);
            Assert.That(lCenter, Is.EqualTo(halfRoot).Within(0.001));
            Assert.That(rCenter, Is.EqualTo(halfRoot).Within(0.001));
            Assert.That(lRight, Is.EqualTo(0).Within(0.001));
            Assert.That(rRight, Is.EqualTo(1.0).Within(0.001));
        });
    }
}

[TestFixture]
public class AudioMixerTests
{
    private static (AudioTrack a1, AudioTrack a2) TwoTracks() =>
        (
            new AudioTrack { Name = "Dialogue", Index = 0 },
            new AudioTrack { Name = "Music", Index = 1 }
        );

    private readonly AudioMixer _mixer = new();

    // ---------- §7 mute / solo ----------

    [Test]
    public void SoloTrack_SilencesOtherTracks()
    {
        var (a1, a2) = TwoTracks();
        a2.Solo = true;

        Assert.Multiple(() =>
        {
            Assert.That(_mixer.IsAudible(a1, [a1, a2]), Is.False);
            Assert.That(_mixer.IsAudible(a2, [a1, a2]), Is.True);
        });
    }

    [Test]
    public void NoSolo_MuteWins()
    {
        var (a1, a2) = TwoTracks();
        a1.Muted = true;

        Assert.Multiple(() =>
        {
            Assert.That(_mixer.IsAudible(a1, [a1, a2]), Is.False);
            Assert.That(_mixer.IsAudible(a2, [a1, a2]), Is.True);
        });
    }

    [Test]
    public void ClipSolo_OverridesMutedTrackOnlyForSoloedClips()
    {
        var (a1, a2) = TwoTracks();
        a2.Muted = true;
        var soloClip = new AudioClip { Solo = true };
        var plain = new AudioClip();
        a2.Clips.Add(soloClip);

        Assert.Multiple(() =>
        {
            // clip solo can't resurrect a muted track
            Assert.That(_mixer.IsClipAudible(soloClip, a2, [a1, a2]), Is.False);

            a2.Muted = false;
            a2.Clips.Add(plain);
            // once any clip is soloed, only soloed clips play
            Assert.That(_mixer.IsClipAudible(soloClip, a2, [a1, a2]), Is.True);
            Assert.That(_mixer.IsClipAudible(plain, a2, [a1, a2]), Is.False);
        });
    }

    // ---------- §5 gain staging ----------

    [Test]
    public void ClipGainLinear_CombinesVolumeDbAndTrack()
    {
        var clip = new AudioClip { Volume = 0.8, GainDb = -6 };
        var track = new AudioTrack { Volume = 0.5 };

        double gain = _mixer.ClipGainLinear(clip, track);

        Assert.That(gain, Is.EqualTo(0.8 * 0.5012 * 0.5).Within(0.002));
    }

    // ---------- §8 fades ----------

    [Test]
    public void FadeGainAt_FormsTrapezoid()
    {
        var clip = new AudioClip { Duration = 10, FadeIn = 2, FadeOut = 3 };

        Assert.Multiple(() =>
        {
            Assert.That(_mixer.FadeGainAt(clip, 0), Is.EqualTo(0));
            Assert.That(_mixer.FadeGainAt(clip, 1), Is.EqualTo(0.5).Within(0.001));       // halfway into fade-in
            Assert.That(_mixer.FadeGainAt(clip, 5), Is.EqualTo(1.0));                     // steady state
            Assert.That(_mixer.FadeGainAt(clip, 8), Is.EqualTo(2.0 / 3.0).Within(0.01));  // early fade-out
            Assert.That(_mixer.FadeGainAt(clip, 9), Is.EqualTo(1.0 / 3.0).Within(0.01));  // late fade-out
            Assert.That(_mixer.FadeGainAt(clip, 10), Is.EqualTo(0));
        });
    }

    // ---------- §22 ducking ----------

    [Test]
    public void Ducking_Disabled_ReturnsUnity()
    {
        var ducking = new DuckingSettings { Enabled = false, AmountDb = -12 };

        Assert.That(_mixer.DuckGainAt([(10, 20)], 15, ducking), Is.EqualTo(1.0));
    }

    [Test]
    public void Ducking_ShapeRampsHoldAndRelease()
    {
        var ducking = new DuckingSettings
        {
            Enabled = true,
            AmountDb = -12,
            AttackSec = 0.5,
            ReleaseSec = 1.0,
        };
        double amount = AudioMath.DbToLinear(-12);

        Assert.Multiple(() =>
        {
            Assert.That(_mixer.DuckGainAt([(10, 20)], 5, ducking), Is.EqualTo(1.0));       // before
            Assert.That(_mixer.DuckGainAt([(10, 20)], 10.25, ducking),
                Is.EqualTo((1 + amount) / 2).Within(0.01));                                 // mid attack ramp
            Assert.That(_mixer.DuckGainAt([(10, 20)], 15, ducking), Is.EqualTo(amount));    // full duck
            Assert.That(_mixer.DuckGainAt([(10, 20)], 20.5, ducking),
                Is.EqualTo((1 + amount) / 2).Within(0.01));                                  // mid release ramp
            Assert.That(_mixer.DuckGainAt([(10, 20)], 25, ducking), Is.EqualTo(1.0));        // recovered
        });
    }

    // ---------- §21 filter generation ----------

    [Test]
    public void BuildClipFilters_EmitsVolumeAfadeAndPan()
    {
        var clip = new AudioClip
        {
            Volume = 0.75,
            Duration = 60,
            FadeIn = 2,
            FadeOut = 2,
            Pan = -0.5,
        };

        string filters = _mixer.BuildClipFilters(clip, new AudioTrack());

        Assert.Multiple(() =>
        {
            Assert.That(filters, Does.Contain("volume=0.75"));
            Assert.That(filters, Does.Contain("afade=t=in:st=0:d=2"));
            Assert.That(filters, Does.Contain("afade=t=out:st=58:d=2"));
            // equal-power pan to the left: cos(π/8) on c0, sin(π/8) on c1
            Assert.That(filters, Does.Contain("pan=stereo|c0=0.924*c0|c1=0.383*c1"));
        });
    }

    [Test]
    public void BuildClipFilters_NeutralClip_IsEmpty()
    {
        var clip = new AudioClip { Duration = 5 };

        Assert.That(_mixer.BuildClipFilters(clip, new AudioTrack()), Is.Empty);
    }

    [Test]
    public void BuildClipFilters_Ducking_AddsFrameEvaluatedVolume()
    {
        var clip = new AudioClip { Start = 0, Duration = 30 };
        var ducking = new DuckingSettings { Enabled = true, AmountDb = -18, KeyTrackIndexes = [0] };

        string filters = _mixer.BuildClipFilters(
            clip, new AudioTrack(), [(5, 15)], ducking);

        Assert.Multiple(() =>
        {
            Assert.That(filters, Does.Contain("eval=frame"));
            Assert.That(filters, Does.Contain("between(t"));
        });
    }

    [Test]
    public void BuildKeyframeExpression_InterpolatesBetweenPoints()
    {
        string expr = AudioMixer.BuildKeyframeExpression(
        [
            new VolumeKeyframe { Time = 0, Value = 1.0 },
            new VolumeKeyframe { Time = 5, Value = 0.4 },
            new VolumeKeyframe { Time = 10, Value = 1.0 },
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(expr, Does.StartWith("if(lt(t\\,5)"));
            Assert.That(expr, Does.Contain("(t-0)/5"));
        });
    }
}

[TestFixture]
public class WaveformTests
{
    private class FakeDecoder : IAudioDecoder
    {
        public int Calls;
        public short[]? Pcm { get; set; } = [];

        public Task<short[]?> DecodeMono16Async(string filePath, int sampleRateHz,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<short[]?>(Pcm);
        }
    }

    [Test]
    public void Compute_PeaksMatchKnownSamples()
    {
        // group of 4: quiet, loud, negative-loud, zero → peak is 16384 (half scale)
        short[] pcm = [4096, 16384, -16384, 0];

        var (peaks, rms) = PeakAnalyzer.Compute(pcm, groupSize: 4);

        Assert.Multiple(() =>
        {
            Assert.That(peaks, Has.Length.EqualTo(1));
            Assert.That(peaks[0], Is.EqualTo(0.5).Within(0.001));
            Assert.That(rms[0], Is.GreaterThan(0.3).And.LessThan(0.5));
        });
    }

    [Test]
    public void Compute_EmptyPcm_ReturnsEmpty()
    {
        var (peaks, rms) = PeakAnalyzer.Compute([], 100);

        Assert.Multiple(() =>
        {
            Assert.That(peaks, Is.Empty);
            Assert.That(rms, Is.Empty);
        });
    }

    [Test]
    public async Task Generator_SecondCall_HitsCacheWithoutDecoding()
    {
        var decoder = new FakeDecoder
        {
            // 8000 Hz × 2 s of silence-with-a-blip
            Pcm = Enumerable.Repeat((short)0, 16000).Select((s, i) => i == 100 ? (short)32000 : s).ToArray(),
        };
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"ms_wf_{Guid.NewGuid():N}");
        var generator = new WaveformGenerator(decoder, cacheRoot);
        var mediaPath = Path.Combine(cacheRoot, "song.mp3");
        Directory.CreateDirectory(cacheRoot);
        File.WriteAllBytes(mediaPath, [1, 2, 3]);

        try
        {
            var first = await generator.GetWaveformAsync(mediaPath, groupSize: 400);
            var second = await generator.GenerateAsync(mediaPath, samplesPerPixel: 400);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(first!.Peaks.Max(), Is.EqualTo(32000f / 32768f).Within(0.001));
                Assert.That(second.Length, Is.EqualTo(first.Peaks.Length));
                Assert.That(decoder.Calls, Is.EqualTo(1)); // second call served from cache
                var wfDir = Path.Combine(cacheRoot, "waveforms");
                Assert.That(Directory.GetFiles(wfDir, "*.waveform"), Has.Length.EqualTo(1));
            });

            generator.Invalidate(mediaPath, 400);
            await generator.GetWaveformAsync(mediaPath, 400);
            Assert.That(decoder.Calls, Is.EqualTo(2)); // invalidated → decoded again
        }
        finally
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Test]
    public async Task WaveformFile_RoundTripsThroughBytes()
    {
        var wf = new AudioWaveform
        {
            Peaks = [0.1f, 0.5f, 1.0f],
            Rms = [0.05f, 0.25f, 0.4f],
            GroupSize = 400,
            SampleRate = 8000,
        };

        var loaded = await Task.Run(() => AudioWaveform.FromBytes(wf.ToBytes()));

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Peaks, Is.EqualTo(wf.Peaks));
            Assert.That(loaded.Rms, Is.EqualTo(wf.Rms));
            Assert.That(loaded.GroupSize, Is.EqualTo(400));
        });
    }

    [Test]
    public void WaveformFile_RejectsGarbage()
    {
        Assert.Throws<InvalidDataException>(() => AudioWaveform.FromBytes([1, 2, 3]));
    }
}

[TestFixture]
public class ClockAndSyncTests
{
    [Test]
    public void MasterClock_SeekThenPause_HoldsPosition()
    {
        var clock = new MasterClock();

        clock.Seek(12.5);
        clock.Play();
        clock.Pause();

        Assert.Multiple(() =>
        {
            Assert.That(clock.CurrentTime, Is.EqualTo(12.5).Within(0.2));
            Assert.That(clock.IsPlaying, Is.False);
        });

        clock.Stop();
        Assert.That(clock.CurrentTime, Is.EqualTo(0).Within(0.01));
    }

    [Test]
    public async Task MasterClock_AdvancesWhilePlaying()
    {
        var clock = new MasterClock();
        clock.Seek(1);
        clock.Play();
        await Task.Delay(120);
        double t = clock.CurrentTime;
        clock.Pause();

        Assert.That(t, Is.GreaterThan(1.0));
        Assert.That(clock.CurrentTime, Is.LessThanOrEqualTo(t + 0.01));
    }

    // ---------- §17 A/V sync policy ----------

    [TestCase(10.0, 10.0, VideoSyncAction.Normal)]     // in sync
    [TestCase(10.02, 10.0, VideoSyncAction.Normal)]     // inside tolerance
    [TestCase(10.5, 10.0, VideoSyncAction.HoldFrame)]   // video ahead → hold last frame
    [TestCase(9.5, 10.0, VideoSyncAction.SkipAhead)]    // video behind → jump
    public void SyncCorrector_DecidesFrameAction(double videoTs, double clockTime, VideoSyncAction expected)
    {
        Assert.That(AvSyncCorrector.Decide(videoTs, clockTime), Is.EqualTo(expected));
    }

    [Test]
    public async Task AudioEngine_StopsAtDurationAndFiresEnded()
    {
        var engine = new AudioEngine(new MasterClock()) { Duration = 0.2 };
        bool ended = false;
        engine.Ended += (_, _) => ended = true;

        await engine.PlayAsync();
        await Task.Delay(500);

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.True);
            Assert.That(engine.IsPlaying, Is.False);
        });
    }
}

[TestFixture]
public class EffectChainTests
{
    [Test]
    public void Chain_JoinsEnabledEffectsInOrder()
    {
        string? chain = AudioEffect.BuildChain(
        [
            new EqualizerEffect { BandGainsDb = [8, 6, 0, 0, 0, 0] },   // bass boost preset
            new CompressorEffect(),
            new NoiseReductionEffect { Enabled = false },               // skipped
            new LimiterEffect(),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(chain, Does.Contain("equalizer=f=60"));
            Assert.That(chain, Does.Contain("acompressor"));
            Assert.That(chain, Does.Contain("alimiter"));
            Assert.That(chain, Does.Not.Contain("afftdn"));
            int eq = chain!.IndexOf("equalizer", StringComparison.Ordinal);
            int comp = chain.IndexOf("acompressor", StringComparison.Ordinal);
            int lim = chain.IndexOf("alimiter", StringComparison.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(eq, Is.LessThan(comp));
                Assert.That(comp, Is.LessThan(lim));
            });
        });
    }

    [Test]
    public void Chain_AllDisabled_ReturnsNull()
    {
        Assert.That(AudioEffect.BuildChain([new CompressorEffect { Enabled = false }]), Is.Null);
    }

    [Test]
    public void ReverbScalesDelayWithRoomSize()
    {
        var small = new ReverbEffect { RoomSize = 0 }.ToFilterArg();
        var hall = new ReverbEffect { RoomSize = 1 }.ToFilterArg();

        Assert.Multiple(() =>
        {
            Assert.That(small, Does.Contain("aecho=0.8:0.85:40|70|110"));
            Assert.That(hall, Does.Contain("aecho=0.8:0.85:120|270|610"));
        });
    }
}
