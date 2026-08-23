using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using MirrorStudio.Core.Commands;
using MirrorStudio.Timeline.Commands;
using MirrorStudio.Timeline.Models;

namespace MirrorStudio.Tests;

[TestFixture]
public class EditCommandTests
{
    private static TimelineClip MakeClip(double start = 0, double duration = 5, double srcStart = 0, double? srcEnd = null) =>
        new()
        {
            Name = "Test",
            Start = start,
            Duration = duration,
            SourceStart = srcStart,
            SourceEnd = srcEnd ?? srcStart + duration
        };

    // ---------- CommandManager ----------

    [Test]
    public void CommandManager_UndoRedo_RoundTrip()
    {
        var clip = MakeClip();
        var manager = new CommandManager();

        manager.Execute(new MoveClipCommand(clip, 10));
        Assert.That(clip.Start, Is.EqualTo(10));
        Assert.That(manager.CanUndo, Is.True);
        Assert.That(manager.CanRedo, Is.False);

        manager.Undo();
        Assert.That(clip.Start, Is.EqualTo(0));
        Assert.That(manager.CanUndo, Is.False);
        Assert.That(manager.CanRedo, Is.True);

        manager.Redo();
        Assert.That(clip.Start, Is.EqualTo(10));
    }

    [Test]
    public void CommandManager_Execute_ClearsRedoStack()
    {
        var clip = MakeClip();
        var manager = new CommandManager();

        manager.Execute(new MoveClipCommand(clip, 5));
        manager.Undo();
        manager.Execute(new MoveClipCommand(clip, 8));

        Assert.That(manager.CanRedo, Is.False);
        Assert.That(clip.Start, Is.EqualTo(8));
    }

    [Test]
    public void CommandManager_Push_RecordsWithoutExecuting()
    {
        var clip = MakeClip(start: 3);   // already mutated by a drag preview
        var manager = new CommandManager();

        manager.Push(new MoveClipCommand(clip, 3));

        Assert.That(clip.Start, Is.EqualTo(3), "Push must not re-apply the command");
        Assert.That(manager.CanUndo, Is.True);

        manager.Undo();
        Assert.That(clip.Start, Is.EqualTo(0));
    }

    [Test]
    public void CommandManager_HistoryChanged_Fires()
    {
        var fired = 0;
        var manager = new CommandManager();
        manager.HistoryChanged += () => fired++;

        manager.Execute(new MoveClipCommand(MakeClip(), 1));
        manager.Undo();
        manager.Clear();

        Assert.That(fired, Is.EqualTo(3));
    }

    // ---------- MoveClipCommand ----------

    [Test]
    public void MoveClipCommand_ClampsNegativeStart()
    {
        var clip = MakeClip(start: 2);
        new MoveClipCommand(clip, -10).Execute();

        Assert.That(clip.Start, Is.EqualTo(0));
    }

    // ---------- DeleteClipCommand ----------

    [Test]
    public void DeleteClipCommand_RemovesAndRestoresAtIndex()
    {
        var track = new TimelineTrack { Name = "V1" };
        var a = MakeClip(); var b = MakeClip(5); var c = MakeClip(9);
        track.Clips.AddRange([a, b, c]);

        var cmd = new DeleteClipCommand(track, b);
        cmd.Execute();
        Assert.That(track.Clips, Is.EqualTo(new TimelineClip[] { a, c }));

        cmd.Undo();
        Assert.That(track.Clips, Is.EqualTo(new TimelineClip[] { a, b, c }));
        Assert.That(track.Clips[1], Is.SameAs(b));
    }

    // ---------- TrimClipCommand ----------

    [Test]
    public void TrimClipCommand_ClampsAndRestores()
    {
        var clip = MakeClip(start: 4, duration: 5);
        var cmd = new TrimClipCommand(clip, -3, 0.001);
        cmd.Execute();

        Assert.That(clip.Start, Is.EqualTo(0));
        Assert.That(clip.Duration, Is.GreaterThanOrEqualTo(0.01));

        cmd.Undo();
        Assert.That(clip.Start, Is.EqualTo(4));
        Assert.That(clip.Duration, Is.EqualTo(5));
    }

    // ---------- SplitClipCommand ----------

    [Test]
    public void SplitClipCommand_CreatesTwoHalvesWithSourceMapping()
    {
        var track = new TimelineTrack { Name = "V1" };
        var clip = MakeClip(start: 2, duration: 10, srcStart: 30, srcEnd: 40);
        track.Clips.Add(clip);

        var cmd = new SplitClipCommand(track, clip, 7);
        cmd.Execute();

        Assert.That(cmd.Right, Is.Not.Null);
        Assert.That(track.Clips.Count, Is.EqualTo(2));

        var left = track.Clips[0];
        var right = track.Clips[1];

        Assert.Multiple(() =>
        {
            Assert.That(left.Start, Is.EqualTo(2));
            Assert.That(left.Duration, Is.EqualTo(5));
            Assert.That(left.SourceStart, Is.EqualTo(30));
            Assert.That(left.SourceEnd, Is.EqualTo(35));

            Assert.That(right.Start, Is.EqualTo(7));
            Assert.That(right.Duration, Is.EqualTo(5));
            Assert.That(right.SourceStart, Is.EqualTo(35));
            Assert.That(right.SourceEnd, Is.EqualTo(40));
        });

        cmd.Undo();
        Assert.That(track.Clips.Count, Is.EqualTo(1));
        Assert.That(track.Clips[0], Is.SameAs(clip));
        Assert.That(clip.Duration, Is.EqualTo(10));
    }

    [Test]
    public void SplitClipCommand_OutsideRange_IsNoOp()
    {
        var track = new TimelineTrack { Name = "V1" };
        var clip = MakeClip(start: 2, duration: 5);
        track.Clips.Add(clip);

        var cmd = new SplitClipCommand(track, clip, 99);
        cmd.Execute();

        Assert.That(cmd.Right, Is.Null);
        Assert.That(track.Clips.Count, Is.EqualTo(1));
        Assert.That(track.Clips[0].Duration, Is.EqualTo(5));
    }

    // ---------- RippleDeleteCommand ----------

    [Test]
    public void RippleDeleteCommand_ClosesGapAndRestores()
    {
        var track = new TimelineTrack { Name = "V1" };
        var a = MakeClip(0, 4);     // A
        var b = MakeClip(4, 3);     // B (deleted)
        var c = MakeClip(7, 5);     // C
        track.Clips.AddRange([a, b, c]);

        var cmd = new RippleDeleteCommand(track, b);
        cmd.Execute();

        // A │ C   — C moved left by B's duration (3s)
        Assert.Multiple(() =>
        {
            Assert.That(track.Clips.Count, Is.EqualTo(2));
            Assert.That(c.Start, Is.EqualTo(4));
        });

        cmd.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(track.Clips.Count, Is.EqualTo(3));
            Assert.That(b.Start, Is.EqualTo(4));
            Assert.That(c.Start, Is.EqualTo(7));
        });
    }
}
