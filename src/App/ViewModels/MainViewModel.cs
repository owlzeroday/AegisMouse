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

public sealed class ArmOption : IEquatable<ArmOption>
{
    public MouseButton? Button { get; init; }
    public required string Label { get; init; }
    public override string ToString() => Label;
    // Value equality so ComboBox SelectedItem matching never shows a blank box.
    public bool Equals(ArmOption? other) => other is not null && Button == other.Button;
    public override bool Equals(object? obj) => obj is ArmOption o && Equals(o);
    public override int GetHashCode() => Button.GetHashCode();
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
        ClickHoldMs = b.ClickHoldMs <= 0 ? 8 : b.ClickHoldMs;
        BurstCount = b.BurstCount; ArmButton = b.ArmButton; RemapVk = b.RemapVk;
        Actions.Clear();
        foreach (var a in b.Actions) Actions.Add(new MacroActionItem(a));
    }

    public ButtonBinding Build() => new()
    {
        Trigger = Trigger,
        Mode = Mode,
        FireRateRpm = Math.Clamp(FireRateRpm, 60, 60000),
        ClickHoldMs = Math.Clamp(ClickHoldMs, 1, 100),
        BurstCount = Math.Clamp(BurstCount, 1, 100),
        ArmButton = ArmButton,
        RemapVk = RemapVk is > 0 and <= 255 ? RemapVk : 0,
        Actions = Actions.Select(a => a.Build()).ToList()
    };

    [ObservableProperty] MouseButton trigger;
    [ObservableProperty] BindingMode mode = BindingMode.Default;
    [ObservableProperty] int fireRateRpm = 600;
    [ObservableProperty] int clickHoldMs = 8;
    [ObservableProperty] int burstCount = 3;
    [ObservableProperty] MouseButton? armButton;
    partial void OnArmButtonChanged(MouseButton? value) => OnPropertyChanged(nameof(ArmButtonOption));
    /// <summary>
    /// Wrapper for the "Armed while" ComboBox (same pattern as the 1.3.4 aim-trigger
    /// fix: SelectedItem over a shared static list — never a null-key SelectedValue).
    /// </summary>
    public ArmOption? ArmButtonOption
    {
        get => ArmButton is null
            ? MainViewModel.ArmOptions[0]
            : MainViewModel.ArmOptions.FirstOrDefault(o => o.Button == ArmButton) ?? MainViewModel.ArmOptions[0];
        set
        {
            if (ArmButton != value?.Button) ArmButton = value?.Button;
            OnPropertyChanged();
        }
    }
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
        Kind = Kind, Button = Button, Vk = Math.Clamp(Vk, 0, 255),
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
        new() { Mode = BindingMode.RapidFireToggle, Label = "Rapid fire (toggle)" },
        new() { Mode = BindingMode.BurstFire, Label = "Burst fire" },
        new() { Mode = BindingMode.HoldMacro, Label = "Hold macro" },
        new() { Mode = BindingMode.RecoilComp, Label = "Recoil compensation" },
        new() { Mode = BindingMode.RapidFireRecoil, Label = "Rapid fire + Recoil" },
        new() { Mode = BindingMode.KeyRemap, Label = "Key remap" },
    ];
    /// <summary>
    /// Shared static arm-trigger list: the Aim tab AND every per-binding "Armed while"
    /// ComboBox bind SelectedItem against these same instances (single source of truth).
    /// </summary>
    public static IReadOnlyList<ArmOption> ArmOptions { get; } =
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

        // Grid-side delete of the rapid binding must uncheck the Rapid tab.
        Bindings.CollectionChanged += (_, _) =>
        {
            if (!_syncingRapid && !_suppressSave) RefreshRapidTab();
        };

        _engine.InputEvent += s => StatusText = s;

        // Global F9 toggles recoil calibration (the header badge advertises it,
        // but nothing was wired to it). Hook events arrive on the hook thread,
        // so marshal to the UI thread — and ignore the key while typing.
        _engine.KeyEvent += (vk, down) =>
        {
            if (!down || vk != AegisMouse.Engine.Interop.Vk.F9) return;
            if (System.Windows.Application.Current?.Dispatcher is not { } dispatcher) return;
            void Toggle()
            {
                if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
                ToggleCalibrateCommand.Execute(null);
            }
            if (dispatcher.CheckAccess()) Toggle(); else dispatcher.BeginInvoke(Toggle);
        };

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
                case nameof(AimFireGate):
                case nameof(AimBone):
                case nameof(AimBoneIndex):
                case nameof(AimTorsoPct):
                case nameof(AimSnapMargin):
                case nameof(AimCoast):
                case nameof(AimFollow):
                case nameof(AimFovRadius):
                case nameof(AimPull):
                case nameof(AimMaxTurn):
                case nameof(AimDeadzone):
                case nameof(AimOffsetPct):
                case nameof(AimMinSize):
                case nameof(AimHueMin):
                case nameof(AimHueMax):
                case nameof(AimSatMin):
                case nameof(AimValMin):
                case nameof(AimCaptureRadius):
                case nameof(AimUseSecondRange):
                case nameof(AimHueMin2):
                case nameof(AimHueMax2):
                case nameof(AimSatMin2):
                case nameof(AimValMin2):
                case nameof(AimTriggerbot):
                case nameof(AimTriggerbotDelay):
                case nameof(AimTriggerbotTol):
                    _saveTimer.Stop(); _saveTimer.Start();
                    break;
                case nameof(CrosshairEnabled):
                case nameof(CrosshairStyleIndex):
                case nameof(CrosshairSize):
                case nameof(CrosshairThick):
                case nameof(CrosshairColorIndex):
                case nameof(CrosshairOffsetX):
                case nameof(CrosshairOffsetY):
                case nameof(CrosshairFollow):
                    SaveCrosshair();
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
            LoadCrosshair();
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
            AimFireGate = a.FireGateEnabled;
            AimBone = Enum.IsDefined(typeof(AimBone), a.Bone) ? a.Bone : AimBone.Auto;
            AimTorsoPct = a.TorsoOffsetPct;
            AimSnapMargin = a.SnapMarginPx;
            AimCoast = a.CoastFrames;
            AimFollow = a.FollowLock;
            AimFovRadius = a.FovRadiusPx;
            AimPull = a.PullStrength;
            AimMaxTurn = a.MaxTurnPxPerFrame;
            AimDeadzone = a.DeadzonePx;
            AimOffsetPct = a.AimOffsetPct;
            AimMinSize = a.MinTargetSizePx;
            AimHueMin = a.HueMin;
            AimHueMax = a.HueMax;
            AimSatMin = a.SatMin;
            AimValMin = a.ValMin;
            AimCaptureRadius = a.CaptureRadiusPx;
            AimUseSecondRange = a.UseSecondRange;
            AimHueMin2 = a.HueMin2;
            AimHueMax2 = a.HueMax2;
            AimSatMin2 = a.SatMin2;
            AimValMin2 = a.ValMin2;
            AimTriggerbot = a.TriggerbotEnabled;
            AimTriggerbotDelay = a.TriggerbotDelayMs;
            AimTriggerbotTol = a.TriggerbotTolerancePx;

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
            RefreshRapidTab();
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
    [ObservableProperty] bool aimFireGate = true;
    [ObservableProperty] AimBone aimBone = AimBone.Auto;
    [ObservableProperty] int aimTorsoPct = 50;
    [ObservableProperty] int aimSnapMargin;
    [ObservableProperty] int aimCoast = 3;
    [ObservableProperty] bool aimFollow = true;
    public static IReadOnlyList<string> AimBoneNames { get; } = ["Head", "Torso", "Auto (nearest)"];
    public int AimBoneIndex
    {
        get => (int)AimBone;
        set => AimBone = Enum.IsDefined(typeof(AimBone), value) ? (AimBone)value : AimBone.Auto;
    }
    [ObservableProperty] double aimFovRadius = 150;
    [ObservableProperty] double aimPull = 0.75;
    [ObservableProperty] double aimMaxTurn = 10;
    [ObservableProperty] int aimDeadzone = 4;
    [ObservableProperty] int aimOffsetPct = 18;
    [ObservableProperty] int aimMinSize = 6;
    [ObservableProperty] int aimHueMin;
    [ObservableProperty] int aimHueMax = 179;
    [ObservableProperty] int aimSatMin = 80;
    [ObservableProperty] int aimValMin = 60;
    [ObservableProperty] int aimCaptureRadius = 240;
    [ObservableProperty] bool aimUseSecondRange;
    [ObservableProperty] int aimHueMin2;
    [ObservableProperty] int aimHueMax2 = 179;
    [ObservableProperty] int aimSatMin2 = 80;
    [ObservableProperty] int aimValMin2 = 60;
    [ObservableProperty] bool aimTriggerbot;
    [ObservableProperty] int aimTriggerbotDelay = 150;
    [ObservableProperty] int aimTriggerbotTol = 6;

    // ── Rapid Fire tab: friendly editor over the first rapid binding ──
    public static IReadOnlyList<string> RapidModes { get; } =
        ["Hold (fire while held)", "Toggle (press on/off)"];

    [ObservableProperty] bool rapidEnabled;
    [ObservableProperty] MouseButton rapidTrigger = MouseButton.Side1;
    [ObservableProperty] int rapidRpm = 800;
    [ObservableProperty] int rapidHoldMs = 8;
    [ObservableProperty] int rapidModeIndex;
    bool _syncingRapid;

    static bool IsRapidMode(BindingMode m) => m is BindingMode.RapidFire or BindingMode.RapidFireToggle;
    BindingItem? FindRapidBinding() => Bindings.FirstOrDefault(b => IsRapidMode(b.Mode));

    /// <summary>Reflect the grid's first rapid binding in the tab (or empty state).</summary>
    void RefreshRapidTab()
    {
        _syncingRapid = true;
        try
        {
            var rb = FindRapidBinding();
            RapidEnabled = rb is not null;
            if (rb is not null)
            {
                RapidTrigger = rb.Trigger;
                RapidRpm = rb.FireRateRpm;
                RapidHoldMs = rb.ClickHoldMs;
                RapidModeIndex = rb.Mode == BindingMode.RapidFireToggle ? 1 : 0;
            }
        }
        finally { _syncingRapid = false; }
    }

    void PushRapidToBinding(BindingItem rb)
    {
        rb.Trigger = RapidTrigger;
        rb.Mode = RapidModeIndex == 1 ? BindingMode.RapidFireToggle : BindingMode.RapidFire;
        rb.FireRateRpm = Math.Clamp(RapidRpm, 60, 60000);
        rb.ClickHoldMs = Math.Clamp(RapidHoldMs, 1, 100);
    }

    void EnsureRapidBinding()
    {
        var rb = FindRapidBinding();
        if (rb is null)
        {
            var used = Bindings.Select(b => b.Trigger).ToHashSet();
            var trigger = used.Contains(RapidTrigger)
                ? BindingItem.AllButtons.FirstOrDefault(b => !used.Contains(b), MouseButton.Side1)
                : RapidTrigger;
            rb = new BindingItem { Trigger = trigger };
            rb.PropertyChanged += (_, _) => ScheduleSave();
            rb.Actions.CollectionChanged += (_, _) => ScheduleSave();
            Bindings.Add(rb);
            SelectedBinding = rb;
        }
        PushRapidToBinding(rb);
        ScheduleSave();
    }

    void RemoveRapidBinding()
    {
        var rb = FindRapidBinding();
        if (rb is null) return;
        Bindings.Remove(rb);
        if (SelectedBinding == rb) SelectedBinding = Bindings.FirstOrDefault();
        ScheduleSave();
    }

    partial void OnRapidEnabledChanged(bool value)
    {
        if (_syncingRapid || _suppressSave) return;
        if (value) EnsureRapidBinding(); else RemoveRapidBinding();
    }

    partial void OnRapidModeIndexChanged(int value) => PushRapidFromTab();
    partial void OnRapidRpmChanged(int value) => PushRapidFromTab();
    partial void OnRapidHoldMsChanged(int value) => PushRapidFromTab();
    partial void OnRapidTriggerChanged(MouseButton value) => PushRapidFromTab();

    void PushRapidFromTab()
    {
        if (_syncingRapid || _suppressSave) return;
        var rb = FindRapidBinding();
        if (rb is null) { if (RapidEnabled) EnsureRapidBinding(); return; }
        PushRapidToBinding(rb);
        ScheduleSave();
    }

    // ── Crosshair overlay (global, not per-profile) ──────────────────
    public static IReadOnlyList<string> CrosshairStyles { get; } = ["Cross", "Dot", "Circle"];
    public static IReadOnlyList<string> CrosshairColorNames { get; } =
        ["Green", "Red", "Cyan", "White", "Yellow", "Magenta"];
    static readonly uint[] CrosshairColors =
        [0xFF00E676, 0xFFFF5252, 0xFF00E5FF, 0xFFFFFFFF, 0xFFFFE135, 0xFFFF40F0];

    [ObservableProperty] bool crosshairEnabled;
    [ObservableProperty] int crosshairStyleIndex;
    [ObservableProperty] int crosshairSize = 24;
    [ObservableProperty] int crosshairThick = 2;
    [ObservableProperty] int crosshairColorIndex;
    [ObservableProperty] int crosshairOffsetX;
    [ObservableProperty] int crosshairOffsetY;
    [ObservableProperty] bool crosshairFollow;

    public CrosshairSettings BuildCrosshairSettings() => new()
    {
        Enabled = CrosshairEnabled,
        Style = Enum.IsDefined(typeof(CrosshairStyle), CrosshairStyleIndex) ? (CrosshairStyle)CrosshairStyleIndex : CrosshairStyle.Cross,
        SizePx = Math.Clamp(CrosshairSize, 4, 128),
        ThicknessPx = Math.Clamp(CrosshairThick, 1, 8),
        ColorArgb = CrosshairColors[Math.Clamp(CrosshairColorIndex, 0, CrosshairColors.Length - 1)],
        OffsetX = Math.Clamp(CrosshairOffsetX, -600, 600),
        OffsetY = Math.Clamp(CrosshairOffsetY, -600, 600),
        FollowCursor = CrosshairFollow
    };

    void SaveCrosshair()
    {
        if (_suppressSave) return;
        try { _db.SetSetting("crosshair", System.Text.Json.JsonSerializer.Serialize(BuildCrosshairSettings())); }
        catch (Exception ex) { Log.Warning(ex, "Failed to save crosshair settings"); }
    }

    void LoadCrosshair()
    {
        try
        {
            var json = _db.GetSetting("crosshair");
            if (json is null) return;
            var c = System.Text.Json.JsonSerializer.Deserialize<CrosshairSettings>(json);
            if (c is null) return;
            ProfileSanitizer.Sanitize(c);
            CrosshairEnabled = c.Enabled;
            CrosshairStyleIndex = (int)c.Style;
            CrosshairSize = c.SizePx;
            CrosshairThick = c.ThicknessPx;
            int idx = Array.IndexOf(CrosshairColors, c.ColorArgb);
            CrosshairColorIndex = idx >= 0 ? idx : 0;
            CrosshairOffsetX = c.OffsetX;
            CrosshairOffsetY = c.OffsetY;
            CrosshairFollow = c.FollowCursor;
        }
        catch (Exception ex) { Log.Warning(ex, "Bad crosshair settings, using defaults"); }
    }

    /// <summary>Selected arm-trigger option (works with the ComboBox; "None" is a real item, not a null selection).</summary>
    public ArmOption? AimTriggerOption
    {
        get => AimTrigger is null
            ? ArmOptions[0]
            : ArmOptions.FirstOrDefault(o => o.Button == AimTrigger) ?? ArmOptions[0];
        set => AimTrigger = value?.Button;
    }

    partial void OnAimTriggerChanged(MouseButton? value) => OnPropertyChanged(nameof(AimTriggerOption));
    partial void OnAimBoneChanged(AimBone value) => OnPropertyChanged(nameof(AimBoneIndex));

    /// <summary>Builds the current aim-assist settings (clamped).</summary>
    public AimAssistSettings BuildAimSettings() => new()
    {
        Enabled = AimEnabled,
        Trigger = AimTrigger,
        TriggerVk = Math.Clamp(AimTriggerVk, 0, 255),
        FireGateEnabled = AimFireGate,
        Bone = Enum.IsDefined(typeof(AimBone), AimBone) ? AimBone : AimBone.Auto,
        TorsoOffsetPct = Math.Clamp(AimTorsoPct, 0, 100),
        SnapMarginPx = Math.Clamp(AimSnapMargin, 0, 200),
        CoastFrames = Math.Clamp(AimCoast, 0, 10),
        FollowLock = AimFollow,
        FovRadiusPx = (int)Math.Clamp(AimFovRadius, 8, 900),
        PullStrength = Math.Clamp(AimPull, 0.05, 0.99),
        MaxTurnPxPerFrame = (int)Math.Clamp(AimMaxTurn, 1, 60),
        DeadzonePx = Math.Clamp(AimDeadzone, 0, 200),
        AimOffsetPct = Math.Clamp(AimOffsetPct, 0, 100),
        MinTargetSizePx = Math.Clamp(AimMinSize, 3, 400),
        HueMin = Math.Clamp(AimHueMin, 0, 179),
        HueMax = Math.Clamp(AimHueMax, 0, 179),
        SatMin = Math.Clamp(AimSatMin, 0, 255),
        ValMin = Math.Clamp(AimValMin, 0, 255),
        CaptureRadiusPx = Math.Clamp(AimCaptureRadius, 48, 900),
        UseSecondRange = AimUseSecondRange,
        HueMin2 = Math.Clamp(AimHueMin2, 0, 179),
        HueMax2 = Math.Clamp(AimHueMax2, 0, 179),
        SatMin2 = Math.Clamp(AimSatMin2, 0, 255),
        ValMin2 = Math.Clamp(AimValMin2, 0, 255),
        TriggerbotEnabled = AimTriggerbot,
        TriggerbotDelayMs = Math.Clamp(AimTriggerbotDelay, 0, 1000),
        TriggerbotTolerancePx = Math.Clamp(AimTriggerbotTol, 2, 50),
        // Tracking guards (tuned constants; UI sliders are a follow-up).
        // They reject scenery/false targets and stabilize the lock.
        MaxTargetSizePx = 320,
        MinFillPct = 25,
        LockFrames = 3,
        SmoothFactor = 0.5
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
    void ApplyAimPreset(string? presetName)
    {
        // Weapon-tuned assist presets: SMGs need pull authority up close (fast TTK,
        // wide strafe), rifles need reach (bigger FOV/capture, smaller min-size so
        // mid-range blobs acquire). Each set schedules a save via the property setters.
        switch (presetName)
        {
            case "SMG":
                AimFovRadius = 170; AimPull = 0.80; AimMaxTurn = 16; AimDeadzone = 3;
                AimOffsetPct = 18; AimTorsoPct = 50; AimBone = AimBone.Auto;
                AimMinSize = 8; AimCaptureRadius = 240; AimSnapMargin = 0;
                AimCoast = 3; AimFollow = true;
                StatusText = "Applied 'SMG / CQB' aim preset — strong pull, close frame.";
                break;
            case "Rifle":
                AimFovRadius = 230; AimPull = 0.70; AimMaxTurn = 12; AimDeadzone = 4;
                AimOffsetPct = 15; AimTorsoPct = 50; AimBone = AimBone.Auto;
                AimMinSize = 5; AimCaptureRadius = 320; AimSnapMargin = 0;
                AimCoast = 4; AimFollow = true;
                StatusText = "Applied 'Rifle / Mid-range' aim preset — wide frame, small-target acquisition.";
                break;
            default: return;
        }
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