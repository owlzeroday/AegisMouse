using AegisMouse.Core;
using AegisMouse.Engine;
using Xunit;

namespace AegisMouse.Tests;

public class AimVisionTests
{
    static readonly AimAssistSettings Red = new() { HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 5 };
    static readonly AimAssistSettings Green = new() { HueMin = 60, HueMax = 110, SatMin = 100, ValMin = 100, MinTargetSizePx = 5 };

    /// <summary>Solid color frame, BGRA32.</summary>
    static byte[] Frame(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
        }
        return px;
    }

    static void FillRect(byte[] px, int w, int x0, int y0, int rw, int rh, byte r, byte g, byte b)
    {
        for (int y = y0; y < y0 + rh; y++)
            for (int x = x0; x < x0 + rw; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
            }
    }

    [Fact]
    public void Detect_Finds_Solid_Blob()
    {
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 20, 15, 12, 20, 255, 0, 0);          // red blob
        var targets = AimVision.Detect(px, 64, 64, Red);
        var t = Assert.Single(targets);
        Assert.Equal(20, t.Left);
        Assert.Equal(15, t.Top);
        Assert.Equal(12, t.Width);
        Assert.Equal(20, t.Height);
    }

    [Fact]
    public void Detect_Ignores_Wrong_Color()
    {
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 20, 15, 12, 20, 0, 255, 0);          // green blob
        Assert.Empty(AimVision.Detect(px, 64, 64, Red));
        Assert.Single(AimVision.Detect(px, 64, 64, Green));
    }

    [Fact]
    public void Detect_Filters_Tiny_Noise()
    {
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 20, 20, 2, 2, 255, 0, 0);            // 2x2 noise
        var strict = new AimAssistSettings { HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 5 };
        Assert.Empty(AimVision.Detect(px, 64, 64, strict));
        var loose = new AimAssistSettings { HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 1 };
        Assert.Single(AimVision.Detect(px, 64, 64, loose));
    }

    [Fact]
    public void SelectTarget_Picks_Nearest_In_Fov()
    {
        var t1 = new AimTarget { Left = 10, Top = 10, Width = 8, Height = 8 };
        var t2 = new AimTarget { Left = 40, Top = 40, Width = 8, Height = 8 };
        var sel = AimVision.SelectTarget([t1, t2], 32, 32, 50);
        Assert.Equal(t2, sel);
        Assert.Null(AimVision.SelectTarget([t1, t2], 32, 32, 5));
    }

    [Fact]
    public void HsvOf_Returns_Expected()
    {
        Assert.Equal((0, 255, 255), AimVision.HsvOf(0, 0, 255));    // pure red → H0
        Assert.Equal((60, 255, 255), AimVision.HsvOf(0, 255, 0));    // pure green → H60
        Assert.Equal((120, 255, 255), AimVision.HsvOf(255, 0, 0));   // pure blue → H120
        Assert.Equal((0, 0, 0), AimVision.HsvOf(0, 0, 0));           // black
    }

    [Fact]
    public void Detect_Rejects_Giant_Blob()
    {
        // A whole matching hillside must not become a "target".
        var px = Frame(64, 64, 255, 0, 0);
        var s = new AimAssistSettings { HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 5, MaxTargetSizePx = 24 };
        Assert.Empty(AimVision.Detect(px, 64, 64, s));
    }

    [Fact]
    public void Detect_Rejects_Wide_Patch()
    {
        // Solid 40x10 ground strip: 100% fill, but h/w = 0.25 (not humanoid).
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 10, 40, 40, 10, 255, 0, 0);
        Assert.Empty(AimVision.Detect(px, 64, 64, Red));
    }

    [Fact]
    public void Detect_Rejects_Sparse_Speckle()
    {
        // 20x20 hollow square: humanoid aspect but only 19% fill (grass-like).
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 20, 20, 20, 1, 255, 0, 0);   // top edge
        FillRect(px, 64, 20, 39, 20, 1, 255, 0, 0);   // bottom edge
        FillRect(px, 64, 20, 21, 1, 18, 255, 0, 0);   // left edge
        FillRect(px, 64, 39, 21, 1, 18, 255, 0, 0);   // right edge (76/400 px = 19% fill)
        var targets = AimVision.Detect(px, 64, 64, Red);
        Assert.Empty(targets);
    }

    [Fact]
    public void Detect_Accepts_Person_Shaped()
    {
        // Solid 12x30 figure: humanoid aspect (2.5), full density, sane size.
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 20, 15, 12, 30, 255, 0, 0);
        var t = Assert.Single(AimVision.Detect(px, 64, 64, Red));
        Assert.Equal(20, t.Left);
        Assert.Equal(15, t.Top);
        Assert.Equal(12, t.Width);
        Assert.Equal(30, t.Height);
    }

    [Fact]
    public void Detect_SecondRange_Finds_Alternate_Outfit()
    {
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 20, 15, 12, 30, 0, 255, 0);   // green figure, person-shaped
        var redOnly = new AimAssistSettings { HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 5 };
        Assert.Empty(AimVision.Detect(px, 64, 64, redOnly));
        var dual = new AimAssistSettings
        {
            HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 5,
            UseSecondRange = true, HueMin2 = 60, HueMax2 = 110, SatMin2 = 100, ValMin2 = 100
        };
        var t = Assert.Single(AimVision.Detect(px, 64, 64, dual));
        Assert.Equal(20, t.Left);
        Assert.Equal(15, t.Top);
    }

    [Fact]
    public void Detect_Primary_Still_Works_With_Second_Enabled()
    {
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 20, 15, 12, 30, 255, 0, 0);   // red figure
        var dual = new AimAssistSettings
        {
            HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 5,
            UseSecondRange = true, HueMin2 = 60, HueMax2 = 110, SatMin2 = 100, ValMin2 = 100
        };
        Assert.Single(AimVision.Detect(px, 64, 64, dual));
    }

    [Fact]
    public void Detect_Relaxed_Keeps_Flickering_Midrange_Blob()
    {
        // 7 px solid square vs MinTargetSize 10: strict drops it (mid-range
        // shimmer), tracking mode holds it.
        var px = Frame(64, 64, 10, 10, 10);
        FillRect(px, 64, 20, 20, 7, 7, 255, 0, 0);
        var s = new AimAssistSettings { HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 10 };
        Assert.Empty(AimVision.Detect(px, 64, 64, s, relaxed: false));
        var t = Assert.Single(AimVision.Detect(px, 64, 64, s, relaxed: true));
        Assert.Equal(7, t.Width);
    }

    [Fact]
    public void Detect_Relaxed_Still_Rejects_Scenery()
    {
        var px = Frame(64, 64, 255, 0, 0);   // full-frame hillside
        var s = new AimAssistSettings { HueMin = 0, HueMax = 20, SatMin = 100, ValMin = 100, MinTargetSizePx = 5, MaxTargetSizePx = 24 };
        Assert.Empty(AimVision.Detect(px, 64, 64, s, relaxed: true));
    }

    [Fact]
    public void Head_Offset_Point()
    {
        var t = new AimTarget { Left = 0, Top = 100, Width = 40, Height = 80 };
        Assert.Equal(100 + 80 * 18 / 100, t.AimY(18));   // ~14 px down from top
        Assert.Equal(100, t.AimY(0));
        Assert.Equal(180, t.AimY(100));
    }
}