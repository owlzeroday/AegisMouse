using AegisMouse.Core;
using AegisMouse.Engine;
using Xunit;

namespace AegisMouse.Tests;

public class TriggerbotTests
{
    [Fact]
    public void Fires_After_Holding_Tolerance_For_Delay()
    {
        var tb = new TriggerbotState();
        for (int i = 0; i < 9; i++)   // 144 ms inside tolerance — not yet
            Assert.False(tb.Update(true, 3, 6, 150, 16));
        Assert.True(tb.Update(true, 3, 6, 150, 16));   // 160 ms — fire
        Assert.False(tb.Update(true, 3, 6, 150, 16));  // one shot only, no spray
    }

    [Fact]
    public void Resets_Timer_When_Leaving_Tolerance()
    {
        var tb = new TriggerbotState();
        Assert.False(tb.Update(true, 3, 6, 150, 100));
        Assert.False(tb.Update(true, 20, 6, 150, 100));  // left — timer wiped
        Assert.False(tb.Update(true, 3, 6, 150, 100));   // needs the full delay again
        Assert.True(tb.Update(true, 3, 6, 150, 100));
    }

    [Fact]
    public void Rearms_Only_After_Leaving_2x_Band()
    {
        var tb = new TriggerbotState();
        Assert.True(tb.Update(true, 3, 6, 0, 16));      // delay 0 — instant fire
        Assert.False(tb.Update(true, 8, 6, 0, 16));     // edge jitter — no refire
        Assert.False(tb.Update(true, 3, 6, 0, 16));     // back on — still quiet
        Assert.False(tb.Update(true, 15, 6, 0, 16));    // truly left — re-arms
        Assert.True(tb.Update(true, 3, 6, 0, 16));      // fires again
    }

    [Fact]
    public void No_Fire_Without_Lock()
    {
        var tb = new TriggerbotState();
        for (int i = 0; i < 20; i++)
            Assert.False(tb.Update(false, 0, 6, 0, 16));
    }
}

public class CrosshairSettingsTests
{
    [Fact]
    public void Sanitize_Clamps_Overlay_Values()
    {
        var c = new CrosshairSettings
        {
            Style = (CrosshairStyle)99, SizePx = 500, ThicknessPx = 0,
            ColorArgb = 0x00001122, OffsetX = -9999, OffsetY = 9999
        };
        ProfileSanitizer.Sanitize(c);
        Assert.Equal(CrosshairStyle.Cross, c.Style);
        Assert.Equal(128, c.SizePx);
        Assert.Equal(1, c.ThicknessPx);
        Assert.Equal(0xFF001122u, c.ColorArgb);   // forced opaque
        Assert.Equal(-600, c.OffsetX);
        Assert.Equal(600, c.OffsetY);
    }

    [Fact]
    public void Settings_RoundTrip_ThroughJson()
    {
        var c = new CrosshairSettings { Enabled = true, Style = CrosshairStyle.Circle, SizePx = 40 };
        var back = System.Text.Json.JsonSerializer.Deserialize<CrosshairSettings>(
            System.Text.Json.JsonSerializer.Serialize(c))!;
        Assert.True(back.Enabled);
        Assert.Equal(CrosshairStyle.Circle, back.Style);
        Assert.Equal(40, back.SizePx);
    }
}
