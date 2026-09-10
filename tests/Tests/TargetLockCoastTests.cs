using AegisMouse.Core;
using AegisMouse.Engine;
using Xunit;

namespace AegisMouse.Tests;

/// <summary>Mid-range + mover fixes: coast grace, prediction matching, reseed.</summary>
public class TargetLockCoastTests
{
    static AimAssistSettings S => new() { LockFrames = 3, CoastFrames = 3 };
    static AimTarget Box(int l, int t, int w, int h) => new() { Left = l, Top = t, Width = w, Height = h };

    [Fact]
    public void Coast_Survives_Brief_Detection_Gaps()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);
        for (int i = 0; i < 3; i++) lk.Update([a], 110, 115, S);
        // Two empty frames inside the coast window — lock holds.
        Assert.Equal(a, lk.Update([], 110, 115, S));
        Assert.Equal(a, lk.Update([], 110, 115, S));
    }

    [Fact]
    public void Coast_Exhausted_Drops_Lock()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);
        for (int i = 0; i < 3; i++) lk.Update([a], 110, 115, S);
        lk.Update([], 110, 115, S);
        lk.Update([], 110, 115, S);
        lk.Update([], 110, 115, S);   // 3rd miss = coast spent
        Assert.Null(lk.Update([], 110, 115, S));
        Assert.Null(lk.Current);
    }

    [Fact]
    public void Coast_Disabled_Behaves_Like_Before()
    {
        var s = new AimAssistSettings { LockFrames = 3, CoastFrames = 0 };
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);
        for (int i = 0; i < 3; i++) lk.Update([a], 110, 115, s);
        Assert.Null(lk.Update([], 110, 115, s));
    }

    [Fact]
    public void Predict_Matches_Fast_Strafing_Target()
    {
        var lk = new TargetLock();
        // 40 px/frame — past the raw 48 px gate within two frames of drift,
        // but prediction tracks it.
        Assert.Null(lk.Update([Box(100, 100, 20, 30)], 110, 115, S));
        Assert.Null(lk.Update([Box(140, 100, 20, 30)], 150, 115, S));
        var held = lk.Update([Box(180, 100, 20, 30)], 190, 115, S);
        Assert.NotNull(held);
        Assert.Equal(180, held!.Left);   // latest position, lock rode along
    }

    [Fact]
    public void Reseed_When_New_Blob_Sits_On_Crosshair()
    {
        var lk = new TargetLock();
        var a = Box(100, 100, 20, 30);
        for (int i = 0; i < 3; i++) lk.Update([a], 110, 115, S);
        // A vanished, B sits exactly on the crosshair — reseed at once.
        var b = Box(200, 100, 20, 30);   // center (210,115)
        Assert.Null(lk.Update([b], 210, 115, S));   // debounce restarts
        Assert.Null(lk.Update([b], 210, 115, S));
        Assert.Equal(b, lk.Update([b], 210, 115, S));
    }

    [Fact]
    public void Sanitizer_Clamps_CoastFrames()
    {
        var s = new AimAssistSettings { CoastFrames = 99 };
        ProfileSanitizer.Sanitize(s);
        Assert.Equal(10, s.CoastFrames);
        s = new AimAssistSettings { CoastFrames = -4 };
        ProfileSanitizer.Sanitize(s);
        Assert.Equal(0, s.CoastFrames);
    }
}
