using AegisMouse.Core;
using AegisMouse.Engine;
using Xunit;

namespace AegisMouse.Tests;

public class TargetLockTests
{
    static AimAssistSettings S => new() { LockFrames = 3 };
    static AimTarget Box(int l, int t, int w, int h) => new() { Left = l, Top = t, Width = w, Height = h };

    [Fact]
    public void Acquires_After_Persisting_LockFrames()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);   // center (110,115)
        Assert.Null(lk.Update([a], 110, 115, S));   // frame 1: tracking, not pullable
        Assert.Null(lk.Update([a], 110, 115, S));   // frame 2
        Assert.Equal(a, lk.Update([a], 110, 115, S)); // frame 3: locked
    }

    [Fact]
    public void Single_Frame_Blip_Never_Engages()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);
        Assert.Null(lk.Update([a], 110, 115, S));
        Assert.Null(lk.Update([], 110, 115, S));    // gone next frame
        Assert.Null(lk.Current);                     // lock fully dropped
    }

    [Fact]
    public void Sticks_To_Lock_When_Challenger_Only_Slightly_Nearer()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);   // center (110,115) — the real enemy
        var b = Box(140, 100, 20, 30);   // center (150,115) — grass blob
        for (int i = 0; i < 3; i++) lk.Update([a, b], 110, 115, S);
        Assert.Equal(a, lk.Update([a, b], 110, 115, S));

        // Crosshair drifts so B is nearer (25 vs 40 px) but inside the 30 px
        // hysteresis margin — the lock must NOT flicker to B (the old wild spin).
        var held = lk.Update([a, b], 125, 115, S);
        Assert.Equal(a, held);
    }

    [Fact]
    public void Switches_When_Challenger_Clearly_Closer()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);   // center (110,115)
        var b = Box(200, 100, 20, 30);   // center (210,115)
        for (int i = 0; i < 3; i++) lk.Update([a, b], 110, 115, S);

        // User clearly retargeted: B is 10 px away, A is 100 px away.
        Assert.Null(lk.Update([a, b], 200, 115, S));  // steal rearms debounce
        Assert.Null(lk.Update([a, b], 200, 115, S));
        Assert.Equal(b, lk.Update([a, b], 200, 115, S));
    }

    [Fact]
    public void Predict_Leads_Strafing_Target()
    {
        var lk = new TargetLock();
        for (int i = 0; i < 4; i++)
            lk.Update([Box(100 + 5 * i, 100, 20, 30)], 110, 115, S);
        // 5 px/frame over 3 intervals → 2-frame lead = +10 px from (125,115).
        var (x, y) = lk.Predict(2, 48);
        Assert.Equal(135, x, precision: 6);
        Assert.Equal(115, y, precision: 6);
    }

    [Fact]
    public void Predict_Stationary_Target_Holds_Center()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);
        for (int i = 0; i < 3; i++) lk.Update([a], 110, 115, S);
        var (x, y) = lk.Predict(2, 48);
        Assert.Equal(110, x, precision: 6);
        Assert.Equal(115, y, precision: 6);
    }

    [Fact]
    public void Predict_Caps_Teleport_Lead()
    {
        var lk = new TargetLock();
        lk.Update([Box(100, 100, 20, 30)], 110, 115, S);
        lk.Update([Box(200, 100, 20, 30)], 210, 115, S);
        // 100 px/frame × 2 frames = 200, capped to 48 from last center (210).
        var (x, _) = lk.Predict(2, 48);
        Assert.Equal(258, x, precision: 6);
    }

    [Fact]
    public void IoU_Matches_Geometry()
    {
        Assert.Equal(1, TargetLock.IoU(Box(0, 0, 10, 10), Box(0, 0, 10, 10)), precision: 6);
        Assert.Equal(0, TargetLock.IoU(Box(0, 0, 10, 10), Box(20, 20, 10, 10)), precision: 6);
        Assert.Equal(25.0 / 175.0, TargetLock.IoU(Box(0, 0, 10, 10), Box(5, 5, 10, 10)), precision: 6);
    }

    [Fact]
    public void Lock_Survives_Box_Jump_With_Overlap()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 40, 30);   // center (120,115)
        for (int i = 0; i < 3; i++) lk.Update([a], 120, 115, S);
        // Enemy closes distance: box grows, center jumps 50 px (past the 48 px
        // center gate) but overlaps (IoU ~0.167) — lock must ride through.
        var grown = Box(120, 100, 100, 30);
        Assert.True(TargetLock.IoU(a, grown) >= 0.15);
        var held = lk.Update([grown], 120, 115, S);
        Assert.NotNull(held);
        Assert.Equal(grown.Left, held!.Left);
    }

    [Fact]
    public void Follows_Slowly_Moving_Target()
    {
        var lk = new TargetLock();
        // Enemy strafes ~5 px/frame; the lock must ride along, not drop.
        AimTarget? got = null;
        for (int i = 0; i < 6; i++)
            got = lk.Update([Box(100 + 5 * i, 100, 20, 30)], 110, 115, S);
        Assert.NotNull(got);
        Assert.Equal(125, got!.Left);   // latest position, not the stale one
    }
}
