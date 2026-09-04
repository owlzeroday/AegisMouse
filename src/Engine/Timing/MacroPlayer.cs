using System.Diagnostics;
using AegisMouse.Core;
using AegisMouse.Engine.Interop;
using Serilog;

namespace AegisMouse.Engine.Timing;

/// <summary>
/// Precise macro action scheduler. Runs actions against an absolute millisecond timeline
/// measured with a Stopwatch; coarse waits use Sleep (1 ms timer resolution), the final
/// sub-ms remainder uses a spin so shots land within +/-1-2 ms of target.
/// </summary>
public sealed class MacroPlayer
{
    readonly Humanizer _humanizer;

    public MacroPlayer(int? seed = null) => _humanizer = new Humanizer(seed);

    /// <summary>Play a macro action list once. Blocking; call from a worker thread.</summary>
    public void Play(IReadOnlyList<MacroAction> actions, HumanizeSettings humanize, CancellationToken ct)
    {
        if (actions is null || actions.Count == 0) return;
        NativeInput.BeginHighResTimer();
        // Everything pressed but not released (HoldDown/KeyDown, or a cancel
        // landing between down and up) is released here — stuck input is worse
        // than an extra clean-up release (a redundant Up is a harmless no-op).
        var heldButtons = new HashSet<MouseButton>();
        var heldKeys = new HashSet<int>();
        try
        {
            var sw = Stopwatch.StartNew();
            double timeline = 0;
            foreach (var action in actions)
            {
                ct.ThrowIfCancellationRequested();
                timeline += _humanizer.Delay(Math.Max(0, action.DelayMs), humanize);
                WaitUntil(sw, timeline, ct);
                int reps = Math.Max(1, action.Repeat);
                for (int i = 0; i < reps; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    ExecuteBody(action, humanize, ct, heldButtons, heldKeys);
                    if (i < reps - 1)
                    {
                        timeline += _humanizer.Delay(Math.Max(0, action.RepeatIntervalMs), humanize);
                        WaitUntil(sw, timeline, ct);
                    }
                }
            }
        }
        finally
        {
            foreach (var b in heldButtons)
                try { NativeInput.ButtonUp(b); } catch { }
            foreach (var vk in heldKeys)
                try { NativeInput.KeyUp(vk); } catch { }
            NativeInput.EndHighResTimer();
        }
    }

    void ExecuteBody(MacroAction a, HumanizeSettings h, CancellationToken ct,
        HashSet<MouseButton> heldButtons, HashSet<int> heldKeys)
    {
        switch (a.Kind)
        {
            case ActionKind.Click:
                NativeInput.ButtonDown(a.Button);
                try { WaitMs(_humanizer.Hold(a.HoldMs, h), ct); }
                finally { NativeInput.ButtonUp(a.Button); }
                break;
            case ActionKind.HoldDown:
                NativeInput.ButtonDown(a.Button);
                heldButtons.Add(a.Button);
                break;
            case ActionKind.ReleaseUp:
                NativeInput.ButtonUp(a.Button);
                heldButtons.Remove(a.Button);
                break;
            case ActionKind.MoveRelative:
                var (dx, dy) = _humanizer.Move(a.Dx, a.Dy, h);
                NativeInput.MoveRelative(dx, dy);
                break;
            // ActionKind.Delay is handled purely by the timeline advance (no extra wait)
            case ActionKind.KeyTap:
                NativeInput.KeyDown(a.Vk);
                try { WaitMs(Math.Max(1, a.HoldMs), ct); }
                finally { NativeInput.KeyUp(a.Vk); }
                break;
            case ActionKind.KeyDown:
                NativeInput.KeyDown(a.Vk);
                heldKeys.Add(a.Vk);
                break;
            case ActionKind.KeyUp:
                NativeInput.KeyUp(a.Vk);
                heldKeys.Remove(a.Vk);
                break;
        }
    }

    /// <summary>Wait until the stopwatch reaches <paramref name="targetMs"/> (absolute).</summary>
    static void WaitUntil(Stopwatch sw, double targetMs, CancellationToken ct)
    {
        double remaining;
        while ((remaining = targetMs - sw.Elapsed.TotalMilliseconds) > 0.25)
        {
            ct.ThrowIfCancellationRequested();
            if (remaining > 4) Thread.Sleep(1);
            else Thread.SpinWait(80);
        }
    }

    /// <summary>Blocking sleep honoring cancellation (used for click holds).</summary>
    static void WaitMs(int ms, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var rem = deadline - Environment.TickCount64;
            if (rem > 4) Thread.Sleep(1);
            else Thread.SpinWait(40);
        }
    }

    public static void LogPlay(IReadOnlyList<MacroAction> actions, string profile)
    {
        double total = actions.Sum(a => (double)Math.Max(0, a.DelayMs));
        Log.Debug("Macro start ({Profile}): {Count} actions, ~{Total} ms", profile, actions.Count, total);
    }
}