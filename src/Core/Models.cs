using System.Text.Json.Serialization;

namespace AegisMouse.Core;

/// <summary>Physical mouse buttons. Names follow gaming convention (Side1 = back, Side2 = forward).</summary>
public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    Side1 = 3,   // XBUTTON1 (back)
    Side2 = 4,   // XBUTTON2 (forward)
    WheelUp = 5,
    WheelDown = 6,
    WheelTiltLeft = 7,
    WheelTiltRight = 8
}

/// <summary>What a binding does when its trigger button is pressed.</summary>
public enum BindingMode
{
    /// <summary>Native behavior; the engine does not interfere.</summary>
    Default = 0,
    /// <summary>While held, replay the macro action list.</summary>
    HoldMacro = 1,
    /// <summary>While held, fire clicks at the configured rate (RPM). Semi-auto weapons become auto.</summary>
    RapidFire = 2,
    /// <summary>On press, fire exactly N clicks at the configured interval.</summary>
    BurstFire = 3,
    /// <summary>While held (and armed), apply per-shot recoil compensation to real left clicks.</summary>
    RecoilComp = 4,
    /// <summary>Forward a keyboard key instead of the mouse button.</summary>
    KeyRemap = 5,
    /// <summary>While held, auto-fire at the configured rate and apply one recoil-table step per shot.</summary>
    RapidFireRecoil = 6
}

/// <summary>Single macro action in a sequence. Timing is in milliseconds.</summary>
public sealed class MacroAction
{
    public ActionKind Kind { get; set; } = ActionKind.Click;
    public MouseButton Button { get; set; } = MouseButton.Left;
    /// <summary>Windows virtual-key code for Key press actions.</summary>
    public int Vk { get; set; }
    /// <summary>Relative mouse movement in physical counts (MoveRelative).</summary>
    public int Dx { get; set; }
    public int Dy { get; set; }
    /// <summary>Delay before this action runs (ms).</summary>
    public int DelayMs { get; set; }
    /// <summary>For Click: time between down and up (ms).</summary>
    public int HoldMs { get; set; } = 10;
    /// <summary>Repeat this action N times (1 = once).</summary>
    public int Repeat { get; set; } = 1;
    /// <summary>For repeated actions: pause between repetitions (ms).</summary>
    public int RepeatIntervalMs { get; set; }
}

public enum ActionKind
{
    Click = 0,
    HoldDown = 1,
    ReleaseUp = 2,
    MoveRelative = 3,
    Delay = 4,
    KeyTap = 5,
    KeyDown = 6,
    KeyUp = 7
}

/// <summary>One compensation step fired per shot, in physical mouse counts.</summary>
public sealed class RecoilStep
{
    public int Shot { get; set; }          // 1-based shot index
    public int Dx { get; set; }            // negative = left
    public int Dy { get; set; }            // positive = pull down
    public int DelayMs { get; set; } = 8;  // delay after the shot is detected
}

/// <summary>Jitter settings that make synthetic input look organic.</summary>
public sealed class HumanizeSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Random +/- ms added to every delay.</summary>
    public int DelayJitterMs { get; set; } = 2;
    /// <summary>Random +/- ms added to click hold time.</summary>
    public int HoldJitterMs { get; set; } = 1;
    /// <summary>Random +/- counts added to each recoil move.</summary>
    public int MoveJitterCounts { get; set; } = 1;
    /// <summary>Occasional missed compensation step (0..100 %).</summary>
    public int StepSkipPercent { get; set; } = 2;
}

/// <summary>One binding: trigger button -> behavior.</summary>
public sealed class ButtonBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public MouseButton Trigger { get; set; } = MouseButton.Side1;
    public BindingMode Mode { get; set; } = BindingMode.Default;
    /// <summary>Fire rate for RapidFire/BurstFire (rounds per minute).</summary>
    public int FireRateRpm { get; set; } = 600;
    /// <summary>Number of shots for BurstFire.</summary>
    public int BurstCount { get; set; } = 3;
    /// <summary>Hold-to-arm button for RecoilComp (null = always armed).</summary>
    public MouseButton? ArmButton { get; set; }
    /// <summary>Virtual key forwarded by KeyRemap.</summary>
    public int RemapVk { get; set; }
    /// <summary>Macro action list for HoldMacro / key sequences.</summary>
    public List<MacroAction> Actions { get; set; } = [];
}

/// <summary>A named, per-game profile. Mirrors a Logitech G HUB profile.</summary>
public sealed class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Profile";
    /// <summary>When true, this profile's bindings are live.</summary>
    public bool Enabled { get; set; } = true;
    public List<ButtonBinding> Bindings { get; set; } = [];
    public List<RecoilStep> RecoilTable { get; set; } = [];
    /// <summary>Physical mouse DPI (used by the calibration math only).</summary>
    public int Dpi { get; set; } = 800;
    /// <summary>Global intensity multiplier for recoil compensation (0.0 .. 2.0).</summary>
    public double RecoilStrength { get; set; } = 1.0;
    public HumanizeSettings Humanize { get; set; } = new();
    /// <summary>Soft aim assist (magnetic pull) for this profile. Null until first saved.</summary>
    public AimAssistSettings? AimAssist { get; set; }
}

/// <summary>Application-wide settings.</summary>
public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>Request 1 ms timer resolution while macros run.</summary>
    public bool UseHighResTimer { get; set; } = true;
    public string LastProfileId { get; set; } = "";
}

/// <summary>
/// Soft aim assist: magnetic pull toward color-detected targets while the trigger is held.
/// Pulls only a fraction of the remaining distance per frame (never a hard lock), capped
/// by a max turn speed, so the player stays in the loop.
/// </summary>
public sealed class AimAssistSettings
{
    public bool Enabled { get; set; }
    /// <summary>Hold this mouse button to arm the assist (e.g. Right = ADS). Null = any.</summary>
    public MouseButton? Trigger { get; set; }
    /// <summary>Alternative keyboard trigger (virtual-key code). 0 = none.</summary>
    public int TriggerVk { get; set; }
    /// <summary>Targets further from the crosshair than this (px) are ignored.</summary>
    public int FovRadiusPx { get; set; } = 150;
    /// <summary>Fraction of the remaining distance moved per frame (0.05 .. 0.99).</summary>
    public double PullStrength { get; set; } = 0.75;
    /// <summary>Hard cap on mouse movement per frame (px) — limits snap speed.</summary>
    public int MaxTurnPxPerFrame { get; set; } = 10;
    /// <summary>Stop assisting once the target is within this many px of the crosshair.</summary>
    public int DeadzonePx { get; set; } = 4;
    /// <summary>Aim point: % down from the top of the detected box (18 ≈ head).</summary>
    public int AimOffsetPct { get; set; } = 18;
    /// <summary>Detected blobs smaller than this (px) are noise and ignored.</summary>
    public int MinTargetSizePx { get; set; } = 10;
    /// <summary>HSV target color. Hue 0..179, S/V 0..255 (OpenCV convention).</summary>
    public int HueMin { get; set; }
    public int HueMax { get; set; } = 179;
    public int SatMin { get; set; } = 80;
    public int ValMin { get; set; } = 60;
    /// <summary>Half-side of the square capture region around the crosshair (px).</summary>
    public int CaptureRadiusPx { get; set; } = 240;
}

public static class Presets
{
    /// <summary>Common FPS recoil archetypes (per-shot pull-down in counts at 800 DPI mid sens).</summary>
    public static IReadOnlyDictionary<string, List<RecoilStep>> Weapons { get; } = new Dictionary<string, List<RecoilStep>>
    {
        ["SMG / MP"] = Table(new[] { (3, 0), (3, 0), (3, 0), (4, 0), (4, 0), (4, 0), (4, 1), (5, 1), (5, 1), (5, 0), (5, -1), (5, -1) }),
        ["AR (M4-style)"] = Table(new[] { (4, 0), (5, 0), (5, 1), (6, 1), (6, 2), (6, 2), (7, 2), (7, 1), (7, 0), (7, -1), (8, -1), (8, -1), (8, 0), (8, 1), (8, 1) }),
        ["AK-style"] = Table(new[] { (6, 0), (7, 0), (8, 1), (8, 2), (9, 2), (9, 3), (9, 3), (10, 3), (10, 2), (10, 1), (10, 0), (11, 0), (11, -1), (11, -1) }),
        ["LMG"] = Table(new[] { (4, 0), (5, 0), (5, 0), (6, 0), (6, 0), (7, 0), (7, 0), (8, 0), (8, 0), (8, 0), (9, 0), (9, 0) }),
        ["Pistol (spam)"] = Table(new[] { (5, 0), (5, 1), (5, -1), (5, 1), (5, -1) })
    };

    static List<RecoilStep> Table((int dy, int dx)[] rows)
    {
        var list = new List<RecoilStep>(rows.Length);
        for (int i = 0; i < rows.Length; i++)
            list.Add(new RecoilStep { Shot = i + 1, Dy = rows[i].dy, Dx = rows[i].dx });
        return list;
    }
}