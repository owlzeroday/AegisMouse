using AegisMouse.Core;
using AegisMouse.Engine;
using Xunit;

namespace AegisMouse.Tests;

public class AimBoneTests
{
    static AimTarget Box(int l, int t, int w, int h) => new() { Left = l, Top = t, Width = w, Height = h };

    [Fact]
    public void BoneAimPoint_Head_Uses_Head_Pct()
    {
        var b = Box(100, 100, 20, 40);   // Top=100 H=40
        var (x, y) = b.BoneAimPoint(AimBone.Head, 18, 50, 0, 0);
        Assert.Equal(110, x);
        Assert.Equal(100 + 40 * 18 / 100, y);
    }

    [Fact]
    public void BoneAimPoint_Torso_Uses_Torso_Pct()
    {
        var b = Box(100, 100, 20, 40);
        var (x, y) = b.BoneAimPoint(AimBone.Torso, 18, 50, 0, 0);
        Assert.Equal(110, x);
        Assert.Equal(100 + 40 * 50 / 100, y);
    }

    [Fact]
    public void BoneAimPoint_Auto_Picks_Nearest_To_Crosshair()
    {
        var b = Box(100, 100, 20, 100);  // head y=118, torso y=150
        var (_, yNearHead) = b.BoneAimPoint(AimBone.Auto, 18, 50, 110, 120);
        Assert.Equal(118, yNearHead);
        var (_, yNearTorso) = b.BoneAimPoint(AimBone.Auto, 18, 50, 110, 160);
        Assert.Equal(150, yNearTorso);
    }

    [Fact]
    public void ContainsPoint_Respects_Margin()
    {
        var b = Box(100, 100, 20, 30);
        Assert.True(b.ContainsPoint(110, 115, 0));
        Assert.False(b.ContainsPoint(50, 50, 0));
        Assert.True(b.ContainsPoint(50, 50, 70));
    }

    [Fact]
    public void Sanitizer_Clamps_New_Fields_And_Fixes_Bone()
    {
        var s = new AimAssistSettings
        {
            Bone = (AimBone)99, TorsoOffsetPct = 500, SnapMarginPx = -5,
            AimOffsetPct = 18, MinTargetSizePx = 10, MaxTargetSizePx = 320
        };
        ProfileSanitizer.Sanitize(s);
        Assert.Equal(AimBone.Auto, s.Bone);
        Assert.Equal(100, s.TorsoOffsetPct);
        Assert.Equal(0, s.SnapMarginPx);
    }

    [Fact]
    public void New_Settings_Default_To_FireGate_Auto()
    {
        var s = new AimAssistSettings();
        Assert.True(s.FireGateEnabled);
        Assert.Equal(AimBone.Auto, s.Bone);
        Assert.Equal(50, s.TorsoOffsetPct);
        Assert.Equal(0, s.SnapMarginPx);
    }
}
