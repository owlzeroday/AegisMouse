using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using AegisMouse.Core;
using AegisMouse.Database;
using AegisMouse.Engine;
using AegisMouse.Engine.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog;

namespace AegisMouse.App.ViewModels;

public sealed class ModeOption
{
    public required BindingMode Mode { get; init; }
    public required string Label { get; init; }
    public override string ToString() => Label;
}

public sealed class ArmOption
{
    public MouseButton? Button { get; init; }
    public required string Label { get; init; }
    public override string ToString() => Label;
}

public sealed partial class BindingItem : ObservableObject
{
    public static IReadOnlyList<MouseButton> AllButtons { get; } = new[]
    {
        MouseButton.Left, MouseButton.Right, MouseButton.Middle,
        MouseButton.Side1, MouseButton.Side2, MouseButton.WheelUp, MouseButton.WheelDown
    };

    public ObservableCollection<MacroActionItem> Actions { get; } = [];

    public void Load(ButtonBinding b)
    {
        Trigger = b.Trigger; Mode = b.Mode; FireRateRpm = b.FireRateRpm;
        BurstCount = b.BurstCount; ArmButton = b.ArmButton; RemapVk = b.RemapVk;
        Actions.Clear();
        foreach (var a in b.Actions) Actions.Add(new MacroActionItem(a));
    }

    public ButtonBinding Build() => new()
    {
        Trigger = Trigger,
        Mode = Mode,
        FireRateRpm = Math.Clamp(FireRateRpm, 60, 60000),
        BurstCount = Math.Clamp(BurstCount, 1, 100),
        ArmButton = ArmButton,
        RemapVk = RemapVk is > 0 and <= 255 ? RemapVk : 0,
        Actions = Actions.Select(a => a.Build()).ToList()
    };

    [ObservableProperty] MouseButton trigger;
    [ObservableProperty] BindingMode mode = BindingMode.Default;
    [ObservableProperty] int fireRateRpm = 600;
    [ObservableProperty] int burstCount = 3;
    [ObservableProperty] MouseButton? armButton;
    [ObservableProperty] int remapVk = AegisMouse.Engine.Interop.Vk.F;
    [ObservableProperty] bool isMacroMode;
    partial void OnModeChanged(BindingMode value) => IsMacroMode = value == BindingMode.HoldMacro;
}

public sealed partial class MacroActionItem : ObservableObject
{
    public static IReadOnlyList<ActionKind> AllKinds { get; } = (ActionKind[])Enum.GetValues(typeof(ActionKind));

    public MacroActionItem() { }
    public MacroActionItem(MacroAction a) { Kind = a.Kind; Button = a.Button; Vk = a.Vk; Dx = a.Dx; Dy = a.Dy; DelayMs = a.DelayMs; HoldMs = a.HoldMs; Repeat = a.Repeat; RepeatIntervalMs = a.RepeatIntervalMs; }

    public MacroAction Build() => new()
    {
        Kind = Kind, Button = Button, Vk = Vk,
        Dx = Math.Clamp(Dx, -2000, 2000), Dy = Math.Clamp(Dy, -2000, 2000),
        DelayMs = Math.Clamp(DelayMs, 0, 60000), HoldMs = Math.Clamp(HoldMs, 0, 60000),
        Repeat = Math.Clamp(Repeat, 1, 1000), RepeatIntervalMs = Math.Clamp(RepeatIntervalMs, 0, 60000)
    };

    [ObservableProperty] ActionKind kind = ActionKind.Click;
    [ObservableProperty] MouseButton button = MouseButton.Left;
    [ObservableProperty] int vk = AegisMouse.Engine.Interop.Vk.Space;
    [ObservableProperty] int dx;
    [ObservableProperty] int dy = 10;
    [ObservableProperty] int delayMs = 30;
    [ObservableProperty] int holdMs = 20;
    [ObservableProperty] int repeat = 1;
    [ObservableProperty] int repeatIntervalMs = 50;
    [ObservableProperty] string vkName = "Space";
    partial void OnVkChanged(int value) => VkName = AegisMouse.Engine.Interop.Vk.Name(value);
}

public sealed partial class RecoilRow : ObservableObject
{
    public RecoilRow() { }
    public RecoilRow(RecoilStep s) { Shot = s.Shot; Dx = s.Dx; Dy = s.Dy; DelayMs = s.DelayMs; }

    public RecoilStep Build() => new()
    {
        Shot = Shot,
        Dx = Math.Clamp(Dx, -2000, 2000),
        Dy = Math.Clamp(Dy, -2000, 2000),
        DelayMs = Math.Clamp(DelayMs, 1, 60000)
    };

    [ObservableProperty] int shot;
    [ObservableProperty] int dx;
    [ObservableProperty] int dy = 4;
    [ObservableProperty] int delayMs = 200;
}

public sealed class KeyOption
{
    public required int Vk { get; init; }
    public required string Name { get; init; }
    public override string ToString() => Name;
}

public sealed partial class ProfileItem : ObservableObject
{
    public GameProfile Model { get; }

    public ProfileItem(GameProfile model) => Model = model;

    [ObservableProperty] string name = "";
    [ObservableProperty] string bindingSummary = "";
}

public sealed partial class MainViewModel : ObservableObject
{
    readonly MacroDatabase _db;
    readonly MacroEngine _engine;

    public ObservableCollection<ProfileItem> Profiles { get; } = [];
    public ObservableCollection<BindingItem> Bindings { get; } = [];
    public ObservableCollection<MacroActionItem> MacroActions { get; } = [];
    public ObservableCollection<RecoilRow> RecoilRows { get; } = [];
    public ObservableCollection<string> RecoilPresets { get; } = [];
    public IReadOnlyList<ModeOption> ModeOptions { get; } =
    [
        new() { Mode = BindingMode.Default, Label = "Default (native)" },
        new() { Mode = BindingMode.RapidFire, Label = "Rapid fire" },
        new() { Mode = BindingMode.BurstFire, Label = "Burst fire" },
        new() { Mode = BindingMode.HoldMacro, Label = "Hold macro" },
        new() { Mode = BindingMode.RecoilComp, Label = "Recoil compensation" },
        new() { Mode = BindingMode.RapidFireRecoil, Label = "Rapid fire + Recoil" },
        new() { Mode = BindingMode.KeyRemap, Label = "Key remap" },
    ];
    public IReadOnlyList<ArmOption> ArmOptions { get; } =
    [
        new() { Button = null, Label = "None (always)" },
        new() { Button = MouseButton.Left, Label = "Left" },
        new() { Button = MouseButton.Right, Label = "Right" },
        new() { Button = MouseButton.Middle, Label = "Middle" },
        new() { Button = MouseButton.Side1, Label = "Side 1" },
        new() { Button = MouseButton.Side2, Label = "Side 2" },
    ];
    public IReadOnlyList<KeyOption> Keys { get; } =
    [
        new() { Vk = Vk.F, Name = "F" }, new() { Vk = Vk.G, Name = "G" }, new() { Vk = Vk.V, Name = "V" },
        new() { Vk = Vk.C, Name = "C" }, new() { Vk = Vk.X, Name = "X" }, new() { Vk = Vk.Q, Name = "Q" },
        new() { Vk = Vk.E, Name = "E" }, new() { Vk = Vk.R, Name = "R" }, new() { Vk = Vk.T, Name = "T" },
        new() { Vk = Vk.Digit1, Name = "1" }, new() { Vk = Vk.Digit2, Name = "2" }, new() { Vk = Vk.Digit3, Name = "3" },
        new() { Vk = Vk.Digit4, Name = "4" }, new() { Vk = Vk.Digit5, Name = "5" },
        new() { Vk = Vk.Space, Name = "Space" }, new() { Vk = Vk.Enter, Name = "Enter" },
        new() { Vk = Vk.Tab, Name = "Tab" }, new() { Vk = Vk.Escape, Name = "Esc" },
        new() { Vk = Vk.Shift, Name = "Shift" }, new() { Vk = Vk.Control, Name = "Ctrl" }, new() { Vk = Vk.Alt, Name = "Alt" },
        new() { Vk = Vk.F1, Name = "F1" }, new() { Vk = Vk.F2, Name = "F2" }, new() { Vk = Vk.F3, Name = "F3" },
        new() { Vk = Vk.F4, Name = "F4" }, new() { Vk = Vk.F5, Name = "F5" }, new() { Vk = Vk.F6, Name = "F6" },
        new() { Vk = Vk.F7, Name = "F7" }, new() { Vk = Vk.F8, Name = "F8" }, new() { Vk = Vk.F9, Name = "F9" },
        new() { Vk = Vk.F10, Name = "F10" }, new() { Vk = Vk.F11, Name = "F11" }, new() { Vk = Vk.F12, Name = "F12" },
    ];

    readonly System.Timers.Timer _saveTimer;
    readonly object _saveLock = new();
    bool _suppressSave;
    bool _syncingActions;
    ProfileItem? _prevProfile;

    public MainViewModel(MacroDatabase db, MacroEngine engine)
    {
        _db = db;
        _engine = engine;
        _saveTimer = new System.Timers.Timer(700) { AutoReset = false };
        _saveTimer.Elapsed += (_, _) => SaveNow();

        // Rows added/removed in the Macros grid must flow into the binding's action
        // list (and new rows must subscribe to auto-save).
        MacroActions.CollectionChanged += (_, e) =>
        {
            if (_syncingActions || SelectedBinding is null) return;
            if (e.NewItems is not null)
                foreach (MacroActionItem item in e.NewItems)
                {
                    item.PropertyChanged += (_, _) => ScheduleSave();
                    SelectedBinding.Actions.Add(item);
                }
            if (e.OldItems is not null)
                foreach (MacroActionItem item in e.OldItems)
                    SelectedBinding.Actions.Remove(item);
            ScheduleSave();
        };

        _engine.InputEvent += s => StatusText = s;

        PropertyChanged += (_, e) =>
        {
            if (_suppressSave) return;
            switch (e.PropertyName)
            {
                case nameof(SelectedProfile):
                case nameof(MasterEnabled):
                case nameof(Dpi):
                case nameof(RecoilStrength):
                case nameof(HumanizeEnabled):
                case nameof(DelayJitterMs):
                case nameof(HoldJitterMs):
                case nameof(MoveJitterCounts):
                case nameof(StepSkipPercent):
                case nameof(StartWithWindows):
                case nameof(MinimizeToTray):
                case nameof(AimEnabled):
                case nameof(AimTrigger):
                case nameof(AimTriggerVk):
                case nameof(AimFovRadius):
                case nameof(AimPull):
                case nameof(AimMaxTurn):
                case nameof(AimOffsetPct):
                case nameof(AimMinSize):
                case nameof(AimHueMin):
                case nameof(AimHueMax):
                case nameof(AimSatMin):
                case nameof(AimValMin):
                case nameof(AimCaptureRadius):
                    _saveTimer.Stop(); _saveTimer.Start();
                    break;
            }
        };

        Load();
    }

    void Load()
    {
        _suppressSave = true;
        try
        {
            var profiles = _db.GetProfiles();
            if (profiles.Count == 0)
            {
                var seed = new GameProfile { Name = "Default" };
                seed.Bindings.Add(new ButtonBinding { Trigger = MouseButton.Side1, Mode = BindingMode.RapidFire, FireRateRpm = 750 });
                seed.Bindings.Add(new ButtonBinding { Trigger = MouseButton.Side2, Mode = BindingMode.RecoilComp, ArmButton = MouseButton.Side1, FireRateRpm = 600 });
                seed.RecoilTable = Presets.Weapons["AR (M4-style)"];
                _db.SaveProfile(seed);
                profiles = _db.GetProfiles();
            }

            foreach (var p in profiles) Profiles.Add(new ProfileItem(p) { Name = p.Name });
            foreach (var key in Presets.Weapons.Keys) RecoilPresets.Add(key);

            var lastId = _db.GetSetting("last_profile");
            SelectedProfile = Profiles.FirstOrDefault(p => p.Model.Id.ToString() == lastId) ?? Profiles[0];
            MasterEnabled = _db.GetSetting("master_enabled") != "0";
            MinimizeToTray = _db.GetSetting("minimize_tray") == "1";
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                StartWithWindows = key?.GetValue("AegisMouse") != null;
        }
        finally { _suppressSave = false; }

        SelectProfile(SelectedProfile);
        _engine.Start();
        _engine.SetProfile(SelectedProfile?.Model);
        _engine.SetEnabled(MasterEnabled);
    }

    [ObservableProperty] ProfileItem? selectedProfile;
    partial void OnSelectedProfileChanged(ProfileItem? value)
    {
        if (_prevProfile is not null) _prevProfile.PropertyChanged -= OnProfileItemChanged;
        _prevProfile = value;
        if (value is null) return;
        value.PropertyChanged += OnProfileItemChanged;
        _saveTimer.Stop();
        SelectProfile(value);
        if (!_suppressSave) { SaveNow(); SaveSettings(); }
    }

    void OnProfileItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileItem.Name)) ScheduleSave();
    }

    void SelectProfile(ProfileItem? item)
    {
        if (item is null) return;
        _suppressSave = true;
        try
        {
            var m = item.Model;
            Dpi = m.Dpi;
            RecoilStrength = m.RecoilStrength;
            var h = m.Humanize;
            HumanizeEnabled = h.Enabled;
            DelayJitterMs = h.DelayJitterMs;
            HoldJitterMs = h.HoldJitterMs;
            MoveJitterCounts = h.MoveJitterCounts;
            StepSkipPercent = h.StepSkipPercent;

            var a = m.AimAssist ?? new AimAssistSettings();
            AimEnabled = a.Enabled;
            AimTrigger = a.Trigger;
            AimTriggerVk = a.TriggerVk;
            AimFovRadius = a.FovRadiusPx;
            AimPull = a.PullStrength;
            AimMaxTurn = a.MaxTurnPxPerFrame;
            AimOffsetPct = a.AimOffsetPct;
            AimMinSize = a.MinTargetSizePx;
            AimHueMin = a.HueMin;
            AimHueMax = a.HueMax;
            AimSatMin = a.SatMin;
            AimValMin = a.ValMin;
            AimCaptureRadius = a.CaptureRadiusPx;

            Bindings.Clear();
            foreach (var b in m.Bindings)
            {
                var bi = new BindingItem();
                bi.Load(b);
                bi.PropertyChanged += (_, _) => ScheduleSave();
                bi.Actions.CollectionChanged += (_, _) => ScheduleSave();
                Bindings.Add(bi);
            }

            RecoilRows.Clear();
            foreach (var s in m.RecoilTable)
            {
                var r = new RecoilRow(s);
                r.PropertyChanged += (_, _) => ScheduleSave();
                RecoilRows.Add(r);
            }

            SelectedBinding = Bindings.FirstOrDefault();
            UpdateSummary(item);
        }
        finally { _suppressSave = false; }
    }

    [ObservableProperty] BindingItem? selectedBinding;
    partial void OnSelectedBindingChanged(BindingItem? value)
    {
        _syncingActions = true;
        try
        {
            MacroActions.Clear();
            if (value is null) return;
            foreach (var a in value.Actions)
            {
                a.PropertyChanged += (_, _) => ScheduleSave();
                MacroActions.Add(a);
            }
        }
        finally { _syncingActions = false; }
    }

    void UpdateSummary(ProfileItem item)
    {
        int assigned = Bindings.Count(b => b.Mode != BindingMode.Default);
        item.BindingSummary = $"{assigned} assigned · {RecoilRows.Count} recoil steps";
    }

    void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }

    void SaveNow()
    {
        if (SelectedProfile is null || _suppressSave) return;
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            try { dispatcher.Invoke(SaveNow); } catch (TaskCanceledException) { }
            return;
        }
        lock (_saveLock)
        {
            try
            {
                var m = SelectedProfile.Model;
                m.Name = SelectedProfile.Name;
                m.Dpi = (int)Math.Clamp(Dpi, 100, 16000);
                m.RecoilStrength = Math.Clamp(RecoilStrength, 0.05, 5.0);
                m.Humanize = new HumanizeSettings
                {
                    Enabled = HumanizeEnabled,
                    DelayJitterMs = Math.Clamp(DelayJitterMs, 0, 1000),
                    HoldJitterMs = Math.Clamp(HoldJitterMs, 0, 1000),
                    MoveJitterCounts = Math.Clamp(MoveJitterCounts, 0, 100),
                    StepSkipPercent = Math.Clamp(StepSkipPercent, 0, 100)
                };
                m.Bindings = Bindings.Select(b => b.Build()).ToList();
                m.RecoilTable = RecoilRows.Select(r => r.Build()).ToList();
                for (int i = 0; i < m.RecoilTable.Count; i++) m.RecoilTable[i].Shot = i + 1;
                m.AimAssist = BuildAimSettings();

                _db.SaveProfile(m);
                _engine.SetProfile(m);
                UpdateSummary(SelectedProfile);
                Log.Debug("Profile '{Name}' saved ({Bindings} bindings, {Steps} recoil steps)",
                    m.Name, m.Bindings.Count, m.RecoilTable.Count);
            }
            catch (Exception ex) { Log.Error(ex, "Failed to save profile"); }
        }
    }

    void SaveSettings()
    {
        if (SelectedProfile is null) return;
        _db.SetSetting("last_profile", SelectedProfile.Model.Id.ToString());
        _db.SetSetting("master_enabled", MasterEnabled ? "1" : "0");
        _db.SetSetting("minimize_tray", MinimizeToTray ? "1" : "0");
        ApplyStartWithWindows();
    }

    [ObservableProperty] double dpi = 800;
    [ObservableProperty] double recoilStrength = 1.0;
    [ObservableProperty] bool humanizeEnabled = true;
    [ObservableProperty] int delayJitterMs = 3;
    [ObservableProperty] int holdJitterMs = 3;
    [ObservableProperty] int moveJitterCounts = 1;
    [ObservableProperty] int stepSkipPercent = 10;

    [ObservableProperty] bool masterEnabled = true;
    partial void OnMasterEnabledChanged(bool value)
    {
        _engine.SetEnabled(value);
        _saveTimer.Stop(); _saveTimer.Start();
    }

    [ObservableProperty] bool startWithWindows;
    partial void OnStartWithWindowsChanged(bool value) => ApplyStartWithWindows();

    [ObservableProperty] bool minimizeToTray;

    [ObservableProperty] bool calibrating;
    [ObservableProperty] string statusText = "Ready â€” bind a side button to rapid fire, then hold it in-game.";

    // ── Aim assist ─────────────────────────────────────────────────
    [ObservableProperty] bool aimEnabled;
    [ObservableProperty] MouseButton? aimTrigger;
    [ObservableProperty] int aimTriggerVk;
    [ObservableProperty] double aimFovRadius = 150;
    [ObservableProperty] double aimPull = 0.75;
    [ObservableProperty] double aimMaxTurn = 10;
    [ObservableProperty] int aimOffsetPct = 18;
    [ObservableProperty] int aimMinSize = 10;
    [ObservableProperty] int aimHueMin;
    [ObservableProperty] int aimHueMax = 179;
    [ObservableProperty] int aimSatMin = 80;
    [ObservableProperty] int aimValMin = 60;
    [ObservableProperty] int aimCaptureRadius = 240;

    /// <summary>Selected arm-trigger option (works with the ComboBox; "None" is a real item, not a null selection).</summary>
    public ArmOption? AimTriggerOption
    {
        get => AimTrigger is null ? ArmOptions[0] : ArmOptions.FirstOrDefault(o => o.Button == AimTrigger);
        set => AimTrigger = value?.Button;
    }

    partial void OnAimTriggerChanged(MouseButton? value) => OnPropertyChanged(nameof(AimTriggerOption));

    /// <summary>Builds the current aim-assist settings (clamped).</summary>
    public AimAssistSettings BuildAimSettings() => new()
    {
        Enabled = AimEnabled,
        Trigger = AimTrigger,
        TriggerVk = Math.Clamp(AimTriggerVk, 0, 255),
        FovRadiusPx = (int)Math.Clamp(AimFovRadius, 8, 900),
        PullStrength = Math.Clamp(AimPull, 0.05, 0.99),
        MaxTurnPxPerFrame = (int)Math.Clamp(AimMaxTurn, 1, 60),
        AimOffsetPct = Math.Clamp(AimOffsetPct, 0, 100),
        MinTargetSizePx = Math.Clamp(AimMinSize, 3, 400),
        HueMin = Math.Clamp(AimHueMin, 0, 179),
        HueMax = Math.Clamp(AimHueMax, 0, 179),
        SatMin = Math.Clamp(AimSatMin, 0, 255),
        ValMin = Math.Clamp(AimValMin, 0, 255),
        CaptureRadiusPx = Math.Clamp(AimCaptureRadius, 48, 900)
    };

    /// <summary>Current humanization settings (for the aim-assist jitter).</summary>
    public HumanizeSettings BuildHumanizeSettings() => new()
    {
        Enabled = HumanizeEnabled,
        DelayJitterMs = DelayJitterMs,
        HoldJitterMs = HoldJitterMs,
        MoveJitterCounts = MoveJitterCounts,
        StepSkipPercent = StepSkipPercent
    };

    /// <summary>Keyboard triggers for aim assist (Vk 0 = mouse trigger only).</summary>
    public IReadOnlyList<KeyOption> AimKeys { get; } =
        new KeyOption[] { new() { Vk = 0, Name = "None (mouse button)" } }
            .Concat(new[]
            {
                new KeyOption { Vk = 0xA0, Name = "Left Shift" }, new KeyOption { Vk = 0xA2, Name = "Left Ctrl" },
                new KeyOption { Vk = 0xA4, Name = "Left Alt" }, new KeyOption { Vk = 0xA1, Name = "Right Shift" },
                new KeyOption { Vk = 0xA3, Name = "Right Ctrl" }, new KeyOption { Vk = 0xA5, Name = "Right Alt" },
                new KeyOption { Vk = Vk.X, Name = "X" }, new KeyOption { Vk = Vk.C, Name = "C" }, new KeyOption { Vk = Vk.V, Name = "V" },
                new KeyOption { Vk = Vk.Q, Name = "Q" }, new KeyOption { Vk = Vk.E, Name = "E" }, new KeyOption { Vk = Vk.F, Name = "F" },
                new KeyOption { Vk = Vk.G, Name = "G" }, new KeyOption { Vk = Vk.F1, Name = "F1" }, new KeyOption { Vk = Vk.F2, Name = "F2" },
                new KeyOption { Vk = Vk.F3, Name = "F3" }, new KeyOption { Vk = Vk.F4, Name = "F4" }, new KeyOption { Vk = Vk.F5, Name = "F5" },
                new KeyOption { Vk = Vk.F6, Name = "F6" }, new KeyOption { Vk = Vk.F7, Name = "F7" }, new KeyOption { Vk = Vk.F8, Name = "F8" },
            })
            .ToArray();

    /// <summary>Click-to-pick color on the debug frame: sets the HSV range around the sampled pixel.</summary>
    public void PickAimColor(byte b, byte g, byte r, int tolerance)
    {
        (int hue, int sat, int val) = AegisMouse.Engine.AimVision.HsvOf(b, g, r);
        AimHueMin = Math.Max(0, hue - tolerance);
        AimHueMax = Math.Min(179, hue + tolerance);
        AimSatMin = Math.Max(0, sat - 40);
        AimValMin = Math.Max(0, val - 40);
        StatusText = $"Picked H {hue} · S {sat} · V {val} (0..179/0..255) — box enemies, tune sliders if needed.";
    }

    void ApplyStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return;
            if (StartWithWindows) key.SetValue("AegisMouse", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("AegisMouse", false);
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to update run key"); }
    }

    [RelayCommand]
    void AddProfile()
    {
        var p = new GameProfile { Name = $"Profile {Profiles.Count + 1}" };
        _db.SaveProfile(p);
        var item = new ProfileItem(p) { Name = p.Name };
        Profiles.Add(item);
        SelectedProfile = item;
        StatusText = "New profile created.";
    }

    [RelayCommand]
    void DeleteProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1) { StatusText = "Keep at least one profile."; return; }
        _db.DeleteProfile(SelectedProfile.Model.Id);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[0];
        StatusText = "Profile deleted.";
    }

    [RelayCommand]
    void AddBinding()
    {
        var used = Bindings.Select(b => b.Trigger).ToHashSet();
        MouseButton? free = null;
        foreach (var b in BindingItem.AllButtons)
            if (!used.Contains(b)) { free = b; break; }
        if (free is null) { StatusText = "All buttons already assigned."; return; }
        var bi = new BindingItem { Trigger = free.Value };
        bi.PropertyChanged += (_, _) => ScheduleSave();
        bi.Actions.CollectionChanged += (_, _) => ScheduleSave();
        Bindings.Add(bi);
        SelectedBinding = bi;
        StatusText = $"Assigned {free.Value} — choose its function on the right.";
        ScheduleSave();
    }

    [RelayCommand]
    void RemoveBinding(BindingItem? item)
    {
        if (item is null) return;
        Bindings.Remove(item);
        if (SelectedBinding == item) SelectedBinding = Bindings.FirstOrDefault();
        ScheduleSave();
    }

    [RelayCommand]
    void AddAction()
    {
        if (SelectedBinding is null) return;
        var ai = new MacroActionItem();
        ai.PropertyChanged += (_, _) => ScheduleSave();
        SelectedBinding.Actions.Add(ai);
        MacroActions.Add(ai);
        ScheduleSave();
    }

    [RelayCommand]
    void RemoveAction(object? item)
    {
        if (item is not MacroActionItem ai || SelectedBinding is null) return;
        SelectedBinding.Actions.Remove(ai);
        MacroActions.Remove(ai);
        ScheduleSave();
    }

    [RelayCommand]
    void AddRow()
    {
        var r = new RecoilRow { Shot = RecoilRows.Count + 1, Dy = 4, DelayMs = 200 };
        r.PropertyChanged += (_, _) => ScheduleSave();
        RecoilRows.Add(r);
        ScheduleSave();
    }

    [RelayCommand]
    void RemoveRow(object? row)
    {
        if (row is not RecoilRow r) return;
        RecoilRows.Remove(r);
        Renumber();
        ScheduleSave();
    }

    void Renumber()
    {
        for (int i = 0; i < RecoilRows.Count; i++) RecoilRows[i].Shot = i + 1;
    }

    [RelayCommand]
    void ApplyPreset(string? presetName)
    {
        if (presetName is null || !Presets.Weapons.TryGetValue(presetName, out var table)) return;
        RecoilRows.Clear();
        foreach (var s in table)
        {
            var r = new RecoilRow(s);
            r.PropertyChanged += (_, _) => ScheduleSave();
            RecoilRows.Add(r);
        }
        StatusText = $"Applied '{presetName}' recoil table ({table.Count} steps).";
        ScheduleSave();
    }

    [RelayCommand]
    void ToggleCalibrate()
    {
        if (Calibrating)
        {
            _engine.StopCalibration();
            Calibrating = false;
            StatusText = "Calibration stopped. Table untouched — edit rows manually.";
        }
        else
        {
            _engine.StartCalibration();
            Calibrating = true;
            StatusText = "Calibration active: hold your fire button in-game — the engine pushes the crosshair down ~10px per 50ms. Stop when the feel is right, then tune the table.";
        }
    }

    [RelayCommand]
    void Shutdown() => System.Windows.Application.Current.Shutdown();
}