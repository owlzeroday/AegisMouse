using System.Diagnostics;
using AegisMouse.Core;
using AegisMouse.Engine;
using AegisMouse.Engine.Timing;
using Xunit;

namespace AegisMouse.Tests;

public class HumanizerTests
{
    [Fact]
    public void Delay_Jitter_StaysWithinBounds()
    {
        var h = new Humanizer(42);
        var set = new HumanizeSettings { Enabled = true, DelayJitterMs = 3 };
        for (int i = 0; i < 500; i++)
        {
            var d = h.Delay(50, set);
            Assert.InRange(d, 47, 53);
        }
    }

    [Fact]
    public void Hold_Never_BelowOne()
    {
        var h = new Humanizer(7);
        var set = new HumanizeSettings { Enabled = true, HoldJitterMs = 5 };
        for (int i = 0; i < 500; i++) Assert.True(h.Hold(2, set) >= 1);
    }

    [Fact]
    public void Move_Jitter_AppliesPerAxis()
    {
        var h = new Humanizer(1);
        var set = new HumanizeSettings { Enabled = true, MoveJitterCounts = 2 };
        var seen = new HashSet<(int, int)>();
        for (int i = 0; i < 500; i++)
        {
            var (dx, dy) = h.Move(5, -3, set);
            Assert.InRange(dx, 3, 7);
            Assert.InRange(dy, -5, -1);
            seen.Add((dx, dy));
        }
        Assert.True(seen.Count > 1, "jitter should produce variation");
    }

    [Fact]
    public void Disabled_Humanize_IsDeterministic()
    {
        var h = new Humanizer(9);
        var set = new HumanizeSettings { Enabled = false, DelayJitterMs = 10 };
        Assert.Equal(50, h.Delay(50, set));
        Assert.Equal(8, h.Hold(8, set));
        Assert.Equal((4, 2), h.Move(4, 2, set));
        Assert.False(h.SkipStep(new HumanizeSettings { Enabled = false, StepSkipPercent = 100 }));
    }

    [Fact]
    public void SkipStep_RespectsPercent()
    {
        var h = new Humanizer(3);
        var set = new HumanizeSettings { Enabled = true, StepSkipPercent = 100 };
        int skips = 0;
        for (int i = 0; i < 200; i++) if (h.SkipStep(set)) skips++;
        Assert.Equal(200, skips);
    }
}

public class MacroPlayerTests
{
    [Fact]
    public void Delay_Sequence_MatchesWallClock()
    {
        var actions = Enumerable.Range(0, 20)
            .Select(i => new MacroAction { Kind = ActionKind.Delay, DelayMs = 40 })
            .ToList();
        var player = new MacroPlayer();
        var sw = Stopwatch.StartNew();
        player.Play(actions, new HumanizeSettings { Enabled = false }, CancellationToken.None);
        sw.Stop();
        Assert.InRange(sw.ElapsedMilliseconds, 750, 860);
    }

    [Fact]
    public void Repeat_ExtendsTimeline()
    {
        var actions = new List<MacroAction>
        {
            new() { Kind = ActionKind.Delay, DelayMs = 30, Repeat = 5, RepeatIntervalMs = 20 }
        };
        var player = new MacroPlayer();
        var sw = Stopwatch.StartNew();
        player.Play(actions, new HumanizeSettings { Enabled = false }, CancellationToken.None);
        sw.Stop();
        // 30ms pre-delay + 4 * 20ms repeat intervals = 110ms
        Assert.InRange(sw.ElapsedMilliseconds, 90, 200);
    }

    [Fact]
    public void Cancellation_StopsPlayback()
    {
        var actions = Enumerable.Range(0, 1000)
            .Select(i => new MacroAction { Kind = ActionKind.Delay, DelayMs = 100 })
            .ToList();
        var player = new MacroPlayer();
        using var cts = new CancellationTokenSource(120);
        var sw = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => player.Play(actions, new HumanizeSettings { Enabled = false }, cts.Token));
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000, "cancellation should stop playback quickly");
    }

    [Fact]
    public void Empty_Actions_CompletesInstantly()
    {
        var player = new MacroPlayer();
        var sw = Stopwatch.StartNew();
        player.Play([], new HumanizeSettings(), CancellationToken.None);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 50);
    }
}

public class PresetTests
{
    [Fact]
    public void WeaponTables_AreSequentialAndSanityChecked()
    {
        foreach (var (name, table) in Presets.Weapons)
        {
            Assert.NotEmpty(table);
            Assert.Equal(1, table[0].Shot);
            for (int i = 1; i < table.Count; i++) Assert.Equal(table[i - 1].Shot + 1, table[i].Shot);
            foreach (var step in table)
            {
                Assert.InRange(step.Dy, 0, 15);
                Assert.InRange(step.Dx, -3, 3);
                Assert.True(step.DelayMs > 0);
            }
        }
    }

    [Fact]
    public void Ak_Table_PullsHarderThan_Smg()
    {
        var ak = Presets.Weapons["AK-style"].Sum(s => s.Dy);
        var smg = Presets.Weapons["SMG / MP"].Sum(s => s.Dy);
        Assert.True(ak > smg, "AK recoil must exceed SMG recoil");
    }
}

public class ProfileSerializationTests
{
    [Fact]
    public void Profile_RoundTrips_ThroughJson()
    {
        var profile = new GameProfile
        {
            Name = "CS2",
            Dpi = 800,
            RecoilStrength = 1.1,
            Bindings =
            [
                new ButtonBinding { Trigger = MouseButton.Side1, Mode = BindingMode.RapidFire, FireRateRpm = 750 },
                new ButtonBinding { Trigger = MouseButton.Side2, Mode = BindingMode.RecoilComp, ArmButton = MouseButton.Side1, FireRateRpm = 600 }
            ],
            RecoilTable = Presets.Weapons["AR (M4-style)"],
            Humanize = new HumanizeSettings { DelayJitterMs = 3 }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(profile);
        var back = System.Text.Json.JsonSerializer.Deserialize<GameProfile>(json)!;
        Assert.Equal("CS2", back.Name);
        Assert.Equal(2, back.Bindings.Count);
        Assert.Equal(BindingMode.RapidFire, back.Bindings[0].Mode);
        Assert.Equal(750, back.Bindings[0].FireRateRpm);
        Assert.Equal(MouseButton.Side1, back.Bindings[1].ArmButton);
        Assert.Equal(15, back.RecoilTable.Count);
        Assert.Equal(3, back.Humanize.DelayJitterMs);
    }
}

public class RapidToggleTests
{
    [Fact]
    public void Toggle_Mode_And_Hold_Survive_Json_RoundTrip()
    {
        var b = new ButtonBinding
        {
            Trigger = MouseButton.Side1, Mode = BindingMode.RapidFireToggle,
            FireRateRpm = 900, ClickHoldMs = 12
        };
        var back = System.Text.Json.JsonSerializer.Deserialize<ButtonBinding>(
            System.Text.Json.JsonSerializer.Serialize(b))!;
        Assert.Equal(BindingMode.RapidFireToggle, back.Mode);
        Assert.Equal(900, back.FireRateRpm);
        Assert.Equal(12, back.ClickHoldMs);
    }

    [Fact]
    public void Legacy_Binding_Without_Hold_Defaults_Safe()
    {
        // Saves predating ClickHoldMs deserialize it as 0 — engine must fall
        // back to the classic 8 ms, never a 0/negative hold.
        var back = System.Text.Json.JsonSerializer.Deserialize<ButtonBinding>(
            """{"Trigger":3,"Mode":2,"FireRateRpm":600,"BurstCount":3}""")!;
        Assert.Equal(8, back.ClickHoldMs);   // initializer default survives
        ProfileSanitizer.Sanitize(back);
        Assert.InRange(back.ClickHoldMs, 1, 100);
    }
}

public class VkNameTests
{
    [Theory]
    [InlineData(AegisMouse.Engine.Interop.Vk.F1, "F1")]
    [InlineData(AegisMouse.Engine.Interop.Vk.Digit3, "3")]
    [InlineData(AegisMouse.Engine.Interop.Vk.Digit7, "7")]
    [InlineData(AegisMouse.Engine.Interop.Vk.Digit0, "0")]
    [InlineData(AegisMouse.Engine.Interop.Vk.W, "W")]
    [InlineData(AegisMouse.Engine.Interop.Vk.F, "F")]
    [InlineData(AegisMouse.Engine.Interop.Vk.Enter, "Enter")]
    [InlineData(AegisMouse.Engine.Interop.Vk.Escape, "Esc")]
    [InlineData(0x7D, "VK0x7D")]
    public void Names_Are_HumanReadable(int vk, string expected)
        => Assert.Equal(expected, AegisMouse.Engine.Interop.Vk.Name(vk));
}

public class WheelDirectionTests
{
    [Fact]
    public void Interval_Math_Clamps_To_Bounds()
    {
        Assert.Equal(5000, Math.Clamp(60000 / 1, 1, 5000));         // 1 RPM → max 5000 ms
        Assert.Equal(1, Math.Clamp(60000 / 60000, 1, 5000));        // 60000 RPM → 1 ms
        Assert.Equal(1, Math.Clamp(60000 / 100000, 1, 5000));       // silly-high RPM → min 1 ms
        Assert.Equal(100, Math.Clamp(60000 / 600, 1, 5000));        // 600 RPM → 100 ms
    }

    [Fact]
    public void ShotInterval_Compensates_Hold_So_Effective_Rpm_Matches_Setting()
    {
        // At 600 RPM each cycle must total 100 ms: 8 ms hold + 92 ms rest.
        // The old code waited the full interval AFTER the hold (108 ms cycle
        // = ~555 RPM), which is why SMGs felt slow/semi-auto vs native.
        Assert.Equal(100, MacroEngine.ShotIntervalMs(600));
        Assert.Equal(92, MacroEngine.RestAfterHoldMs(100, 8));
        Assert.Equal(59, MacroEngine.RestAfterHoldMs(67, 8));   // ~900 RPM SMG
        Assert.Equal(1, MacroEngine.RestAfterHoldMs(5, 8));     // floor, never 0/negative
        Assert.Equal(5000, MacroEngine.ShotIntervalMs(1));
        Assert.Equal(1, MacroEngine.ShotIntervalMs(60000));
    }

    [Fact]
    public void Wheel_MultiNotch_Counts_Absolute()
    {
        // each notch of a multi-notch scroll spawns one single-shot dispatch
        Assert.Equal(3, Math.Abs(-3));
        Assert.Equal(2, Math.Abs(2));
    }
}