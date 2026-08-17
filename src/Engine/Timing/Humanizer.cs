using AegisMouse.Core;

namespace AegisMouse.Engine.Timing;

/// <summary>Adds human-like jitter to synthetic input timings. Seeded for deterministic tests.</summary>
public sealed class Humanizer
{
    readonly Random _rng;

    public Humanizer(int? seed = null) => _rng = seed is { } s ? new Random(s) : new Random();

    /// <summary>Delay with +/- jitter applied (clamped to >= 0).</summary>
    public int Delay(int ms, HumanizeSettings h) =>
        h.Enabled && h.DelayJitterMs > 0 ? Math.Max(0, ms + _rng.Next(-h.DelayJitterMs, h.DelayJitterMs + 1)) : ms;

    /// <summary>Hold time with +/- jitter applied (clamped to >= 1).</summary>
    public int Hold(int ms, HumanizeSettings h) =>
        h.Enabled && h.HoldJitterMs > 0 ? Math.Max(1, ms + _rng.Next(-h.HoldJitterMs, h.HoldJitterMs + 1)) : Math.Max(1, ms);

    /// <summary>Movement with +/- jitter applied per axis.</summary>
    public (int dx, int dy) Move(int dx, int dy, HumanizeSettings h)
    {
        if (!h.Enabled || h.MoveJitterCounts <= 0) return (dx, dy);
        int j = h.MoveJitterCounts;
        return (dx + _rng.Next(-j, j + 1), dy + _rng.Next(-j, j + 1));
    }

    /// <summary>Should this compensation step be skipped (percent 0..100)?</summary>
    public bool SkipStep(HumanizeSettings h) => h.Enabled && h.StepSkipPercent > 0 && _rng.Next(100) < h.StepSkipPercent;
}