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
    public void Head_Offset_Point()
    {
        var t = new AimTarget { Left = 0, Top = 100, Width = 40, Height = 80 };
        Assert.Equal(100 + 80 * 18 / 100, t.AimY(18));   // ~14 px down from top
        Assert.Equal(100, t.AimY(0));
        Assert.Equal(180, t.AimY(100));
    }
}