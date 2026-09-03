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
