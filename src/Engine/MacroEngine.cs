using System.Collections.Concurrent;
using AegisMouse.Core;
using AegisMouse.Engine.Interop;
using AegisMouse.Engine.Timing;
using Serilog;

namespace AegisMouse.Engine;

/// <summary>
/// Orchestrates bindings: low-level hooks detect real input, and the engine dispatches
/// rapid-fire / burst / hold-macro / recoil-comp / key-remap behaviors with precise ms timing.
/// Works with any mouse because everything is synthesized at the OS level (SendInput).
/// </summary>
public sealed class MacroEngine : IDisposable
{
    readonly GlobalHook _hook = new();
    readonly Humanizer _humanizer = new();
    readonly ConcurrentDictionary<MouseButton, CancellationTokenSource> _active = new();
    readonly HashSet<MouseButton> _held = new();

    GameProfile? _profile;
    volatile bool _enabled = true;
    volatile bool _calibrating;
    int _wheelStepIndex;

    /// <summary>Human-readable dispatch feed for the UI status bar ("Side1 down → RapidFire").</summary>
    public event Action<string>? InputEvent;

    public event Action? StateChanged;

    /// <summary>Raw physical key events (for aim-assist triggers). Raised on the hook thread.</summary>
    public event Action<int, bool>? KeyEvent;

    /// <summary>Raw physical button events (for aim-assist triggers). Raised on the hook thread.</summary>
    public event Action<MouseButton, bool>? ButtonEvent;

    public GameProfile? Profile => _profile;
    public bool Enabled => _enabled;
    public bool IsCalibrating => _calibrating;
    public bool HookActive => _hookActive;

    volatile bool _hookActive;

    public MacroEngine()
    {
        _hook.Button += OnMouseButton;
        _hook.Wheel += OnWheel;
        _hook.Key += (vk, down) => KeyEvent?.Invoke(vk, down);
    }

    public void Start()
    {
        if (_hookActive) return;
        _hook.Start();
        _hookActive = true;
        Log.Information("Macro engine started (universal input hooks live)");
    }

    public void SetProfile(GameProfile? profile)
    {
        CancelAll();
        _profile = profile;
        _wheelStepIndex = 0;
        _hook.ShouldSuppress = profile is null
            ? null
            : b => _enabled && profile.Bindings.Any(x => x.Trigger == b && x.Mode == BindingMode.KeyRemap);
        StateChanged?.Invoke();
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled) CancelAll();
        _enabled = enabled;
        _hook.ShouldSuppress = _enabled
            ? _hook.ShouldSuppress
            : _hook.ShouldSuppress; // kept; SetProfile recomputes
        StateChanged?.Invoke();
    }

    void OnMouseButton(MouseButton button, bool down)
    {
        lock (_held)
        {
            if (down) _held.Add(button);
            else _held.Remove(button);
        }
        if (!down) Cancel(button);
        ButtonEvent?.Invoke(button, down);

        if (!_enabled || _profile is null) return;

        if (_calibrating)
        {
            if (button == MouseButton.Left && down)
            {
                _ = Task.Run(() => CalibrationLoop());
                return;
            }
        }

        var binding = _profile.Bindings.FirstOrDefault(x => x.Trigger == button);
        if (binding is null || binding.Mode == BindingMode.Default) return;

        if (down)
        {
            switch (binding.Mode)
            {
                case BindingMode.RapidFire:
                    StartLoop(button, "RapidFire", () => RapidFireLoop(binding));
                    break;
                case BindingMode.BurstFire:
                    StartLoop(button, "BurstFire", () => BurstFire(binding));
                    break;
                case BindingMode.HoldMacro:
                    StartLoop(button, "HoldMacro", () => HoldMacro(binding));
                    break;
                case BindingMode.RecoilComp:
                    if (Armed(binding)) StartLoop(button, "RecoilComp", () => RecoilLoop(binding));
                    break;
                case BindingMode.RapidFireRecoil:
                    if (Armed(binding)) StartLoop(button, "RapidFireRecoil", () => RapidFireRecoil(binding));
                    break;
                case BindingMode.KeyRemap:
                    if (binding.RemapVk != 0) NativeInput.KeyDown(binding.RemapVk);
                    break;
            }
            InputEvent?.Invoke($"{(button is MouseButton.Left or MouseButton.Right or MouseButton.Middle ? $"{button} down" : $"{button} pressed")} → {binding.Mode}");
        }
        else
        {
            if (binding.Mode == BindingMode.KeyRemap && binding.RemapVk != 0)
                NativeInput.KeyUp(binding.RemapVk);
            Cancel(button);
        }
    }

    /// <summary>
    /// Wheel notches have no "held" state: every tick is a single-shot trigger.
    /// RapidFire = one click, BurstFire = full burst, HoldMacro = one pass,
    /// RecoilComp = one compensation step (cycles the table), KeyRemap = key tap.
    /// </summary>
    void OnWheel(bool down, int ticks)
    {
        if (!_enabled || _profile is null) return;
        var button = down ? MouseButton.WheelDown : MouseButton.WheelUp;
        var binding = _profile.Bindings.FirstOrDefault(x => x.Trigger == button);
        if (binding is null || binding.Mode == BindingMode.Default) return;
        // Cap: real wheels deliver 1-3 ticks/notch; virtual-wheel spam (or a
        // malicious app synthesizing wheel input) must not spawn unbounded threads.
        int n = Math.Clamp(ticks, 1, 8);
        for (int i = 0; i < n; i++)
            StartLoop(button, "WheelSingleShot", () => WheelAction(binding, down));
        InputEvent?.Invoke($"{button} ×{ticks} → {binding.Mode}");
    }

    void WheelAction(ButtonBinding b, bool down)
    {
        var ct = _active.TryGetValue(b.Trigger, out var c) ? c.Token : CancellationToken.None;
        var h = _profile?.Humanize ?? new HumanizeSettings();
        var direction = down ? -1 : 1;
        switch (b.Mode)
        {
            case BindingMode.RapidFire:
                NativeInput.ButtonDown(MouseButton.Left);
                WaitMs(Math.Max(1, _humanizer.Hold(8, h)), ct);
                NativeInput.ButtonUp(MouseButton.Left);
                break;
            case BindingMode.BurstFire:
                int interval = ShotIntervalMs(b.FireRateRpm);
                NativeInput.BeginHighResTimer();
                try
                {
                    for (int i = 0; i < Math.Max(1, b.BurstCount) && !ct.IsCancellationRequested; i++)
                    {
                        int hold = Math.Max(1, _humanizer.Hold(8, h));
                        NativeInput.ButtonDown(MouseButton.Left);
                        WaitMs(hold, ct);
                        NativeInput.ButtonUp(MouseButton.Left);
                        if (i < b.BurstCount - 1) WaitMs(Math.Max(1, _humanizer.Delay(RestAfterHoldMs(interval, hold), h)), ct);
                    }
                }
                finally { NativeInput.EndHighResTimer(); }
                break;
            case BindingMode.HoldMacro:
                new MacroPlayer().Play(b.Actions, h, ct);
                break;
            case BindingMode.RecoilComp:
                if (!Armed(b) || _profile is null || _profile.RecoilTable.Count == 0) break;
                var step = _profile.RecoilTable[_wheelStepIndex % _profile.RecoilTable.Count];
                Interlocked.Increment(ref _wheelStepIndex);
                var (jx, jy) = _humanizer.Move(
                    (int)Math.Round(step.Dx * _profile.RecoilStrength * direction),
                    (int)Math.Round(step.Dy * _profile.RecoilStrength * direction), h);
                NativeInput.MoveRelative(jx, jy);
                break;
            case BindingMode.KeyRemap:
                if (b.RemapVk != 0) NativeInput.KeyTap(b.RemapVk);
                break;
        }
    }

    bool Armed(ButtonBinding b) => b.ArmButton is null || NativeInput.IsDown(b.ArmButton.Value);

    bool Held(MouseButton b)
    {
        lock (_held) return _held.Contains(b);
    }

    /// <summary>Ms between shot starts for a fire rate (clamped to a sane range).</summary>
    public static int ShotIntervalMs(int fireRateRpm) => Math.Clamp(60000 / Math.Max(1, fireRateRpm), 1, 5000);

    /// <summary>
    /// Rest time after ButtonUp: the hold already consumed part of the cycle, so
    /// waiting the full interval on top of it makes effective RPM always lower
    /// than configured (never below 1 ms).
    /// </summary>
    public static int RestAfterHoldMs(int intervalMs, int holdMs) => Math.Max(1, intervalMs - holdMs);

    void StartLoop(MouseButton button, string mode, Action body)
    {
        Cancel(button);
        var cts = new CancellationTokenSource();
        _active[button] = cts;
        var t = new Thread(() =>
        {
            try { Log.Information("Macro dispatch: {Button} → {Mode}", button, mode); body(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "Macro loop failed for {Button}", button); }
            finally
            {
                if (_active.TryGetValue(button, out var current) && ReferenceEquals(current, cts))
                    _active.TryRemove(button, out _);
            }
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = $"Macro-{button}" };
        t.Start();
    }

    void RapidFireLoop(ButtonBinding b)
    {
        int interval = ShotIntervalMs(b.FireRateRpm);
        var ct = _active.TryGetValue(b.Trigger, out var c) ? c.Token : CancellationToken.None;
        var h = _profile?.Humanize ?? new HumanizeSettings();
        // High-res timer: without timeBeginPeriod(1), Sleep/TickCount granularity
        // (~15.6 ms) makes every wait overshoot and rapid fire runs slow — very
        // noticeable on high-RPM SMGs where each millisecond matters.
        NativeInput.BeginHighResTimer();
        try
        {
            while (Held(b.Trigger) && !ct.IsCancellationRequested)
            {
                int hold = Math.Max(1, _humanizer.Hold(8, h));
                NativeInput.ButtonDown(MouseButton.Left);
                WaitMs(hold, ct);
                NativeInput.ButtonUp(MouseButton.Left);
                WaitMs(Math.Max(1, _humanizer.Delay(RestAfterHoldMs(interval, hold), h)), ct);
            }
        }
        finally { NativeInput.EndHighResTimer(); }
    }

    void BurstFire(ButtonBinding b)
    {
        int interval = ShotIntervalMs(b.FireRateRpm);
        var ct = _active.TryGetValue(b.Trigger, out var c) ? c.Token : CancellationToken.None;
        var h = _profile?.Humanize ?? new HumanizeSettings();
        NativeInput.BeginHighResTimer();
        try
        {
            for (int i = 0; i < Math.Max(1, b.BurstCount) && !ct.IsCancellationRequested; i++)
            {
                int hold = Math.Max(1, _humanizer.Hold(8, h));
                NativeInput.ButtonDown(MouseButton.Left);
                WaitMs(hold, ct);
                NativeInput.ButtonUp(MouseButton.Left);
                if (i < b.BurstCount - 1) WaitMs(Math.Max(1, _humanizer.Delay(RestAfterHoldMs(interval, hold), h)), ct);
            }
        }
        finally { NativeInput.EndHighResTimer(); }
    }

    void HoldMacro(ButtonBinding b)
    {
        var ct = _active.TryGetValue(b.Trigger, out var c) ? c.Token : CancellationToken.None;
        var player = new MacroPlayer();
        player.Play(b.Actions, _profile?.Humanize ?? new HumanizeSettings(), ct);
    }

    void RecoilLoop(ButtonBinding b)
    {
        var profile = _profile;
        if (profile is null || profile.RecoilTable.Count == 0) return;
        int interval = ShotIntervalMs(b.FireRateRpm);
        var ct = _active.TryGetValue(b.Trigger, out var c) ? c.Token : CancellationToken.None;
        var h = profile.Humanize;
        double strength = profile.RecoilStrength;

        NativeInput.BeginHighResTimer();
        try
        {
            foreach (var step in profile.RecoilTable)
            {
                WaitMs(_humanizer.Delay(interval, h), ct);
                if (ct.IsCancellationRequested || !Held(b.Trigger)) break;
                if (_humanizer.SkipStep(h)) continue;
                int dx = (int)Math.Round(step.Dx * strength);
                int dy = (int)Math.Round(step.Dy * strength);
                var (jx, jy) = _humanizer.Move(dx, dy, h);
                NativeInput.MoveRelative(jx, jy);
            }
        }
        finally { NativeInput.EndHighResTimer(); }
    }

    void RapidFireRecoil(ButtonBinding b)
    {
        var profile = _profile;
        if (profile is null) return;
        if (profile.RecoilTable.Count == 0) { RapidFireLoop(b); return; }
        int interval = ShotIntervalMs(b.FireRateRpm);
        var ct = _active.TryGetValue(b.Trigger, out var c) ? c.Token : CancellationToken.None;
        var h = profile.Humanize;
        double strength = profile.RecoilStrength;
        int stepIndex = 0;

        NativeInput.BeginHighResTimer();
        try
        {
            while (Held(b.Trigger) && !ct.IsCancellationRequested)
            {
                int hold = Math.Max(1, _humanizer.Hold(8, h));
                NativeInput.ButtonDown(MouseButton.Left);
                WaitMs(hold, ct);
                NativeInput.ButtonUp(MouseButton.Left);
                if (ct.IsCancellationRequested || !Held(b.Trigger)) break;
                if (!_humanizer.SkipStep(h))
                {
                    var step = profile.RecoilTable[stepIndex % profile.RecoilTable.Count];
                    int dx = (int)Math.Round(step.Dx * strength);
                    int dy = (int)Math.Round(step.Dy * strength);
                    var (jx, jy) = _humanizer.Move(dx, dy, h);
                    NativeInput.MoveRelative(jx, jy);
                }
                stepIndex++;
                WaitMs(Math.Max(1, _humanizer.Delay(RestAfterHoldMs(interval, hold), h)), ct);
            }
        }
        finally { NativeInput.EndHighResTimer(); }
    }

    public void StartCalibration() { _calibrating = true; StateChanged?.Invoke(); }

    public void StopCalibration()
    {
        _calibrating = false;
        Cancel(MouseButton.Left);
        StateChanged?.Invoke();
    }

    void CalibrationLoop()
    {
        try
        {
            while (_calibrating && NativeInput.IsDown(MouseButton.Left))
            {
                NativeInput.MoveRelative(0, 10);
                Thread.Sleep(50);
            }
        }
        finally { }
    }

    public void CancelAll()
    {
        foreach (var kv in _active) { try { kv.Value.Cancel(); } catch { } }
        _active.Clear();
    }

    void Cancel(MouseButton b)
    {
        if (_active.TryRemove(b, out var cts))
            try { cts.Cancel(); } catch { }
    }

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

    public void Dispose()
    {
        _hook.Button -= OnMouseButton;
        CancelAll();
        _hook.Dispose();
    }
}