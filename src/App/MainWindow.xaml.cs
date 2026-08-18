using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AegisMouse.App.ViewModels;
using AegisMouse.Core;
using AegisMouse.Database;
using AegisMouse.Engine;
using Serilog;

namespace AegisMouse.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    readonly AimAssistService _aim = new();
    AimDebugWindow? _debugWin;
    bool _aimArmedMouse, _aimArmedKey;
    System.Windows.Forms.NotifyIcon? _tray;
    bool _trayMinimized;
    bool _exiting;

    static readonly string[] AimProps =
    [
        nameof(MainViewModel.AimEnabled), nameof(MainViewModel.AimTrigger), nameof(MainViewModel.AimTriggerVk),
        nameof(MainViewModel.AimFovRadius), nameof(MainViewModel.AimPull), nameof(MainViewModel.AimMaxTurn),
        nameof(MainViewModel.AimOffsetPct), nameof(MainViewModel.AimMinSize), nameof(MainViewModel.AimHueMin),
        nameof(MainViewModel.AimHueMax), nameof(MainViewModel.AimSatMin), nameof(MainViewModel.AimValMin),
        nameof(MainViewModel.AimCaptureRadius),
        nameof(MainViewModel.HumanizeEnabled), nameof(MainViewModel.MoveJitterCounts)
    ];

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(App.Db!, App.Engine!);
        DataContext = _vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Calibrating)) UpdateCalibBadge();
            if (AimProps.Contains(e.PropertyName)) PushAimSettings();
        };
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        UpdateCalibBadge();

        // Recoil visualizer: subscribe to collection + property changes
        _vm.RecoilRows.CollectionChanged += OnRecoilRowsChanged;
        Loaded += (_, _) => DrawRecoilPattern();
        RecoilCanvas.SizeChanged += (_, _) => DrawRecoilPattern();

        // Aim assist: service + trigger arming (hook events arrive on the hook thread — volatile flags are safe)
        _aim.Start(_vm.BuildAimSettings(), _vm.BuildHumanizeSettings());
        if (App.Engine is { } engine)
        {
            engine.KeyEvent += OnAimKeyEvent;
            engine.ButtonEvent += OnAimButtonEvent;
        }
    }

    void PushAimSettings()
    {
        _aim.SetSettings(_vm.BuildAimSettings());
        _aim.SetHumanize(_vm.BuildHumanizeSettings());
    }

    void OnAimKeyEvent(int vk, bool down)
    {
        if (_vm.AimTriggerVk != 0 && vk == _vm.AimTriggerVk)
        {
            _aimArmedKey = down;
            _aim.SetArmed(_aimArmedKey || _aimArmedMouse);
        }
    }

    void OnAimButtonEvent(MouseButton button, bool down)
    {
        if (_vm.AimTrigger is { } t && button == t)
        {
            _aimArmedMouse = down;
            _aim.SetArmed(_aimArmedKey || _aimArmedMouse);
        }
    }

    void OpenAimDebug_Click(object sender, RoutedEventArgs e)
    {
        if (_debugWin is { } w && w.IsVisible) { w.Activate(); return; }
        _debugWin = new AimDebugWindow(_vm, _aim);
        _debugWin.Closed += (_, _) =>
        {
            _debugWin = null;
            RestoreFromTray();
        };
        _debugWin.Show();

        // Hide AegisMouse so the game screen (and the bots) are fully visible
        WindowState = WindowState.Minimized;   // OnStateChanged hides it to the tray when MinimizeToTray is on
    }

    void UpdateCalibBadge()
    {
        CalibBadge.Visibility = _vm.Calibrating ? Visibility.Visible : Visibility.Collapsed;
        CalibrateBtn.Content = _vm.Calibrating ? "Stop calibration" : "Calibrate";
        CalibrateBtn.Background = _vm.Calibrating ? FindResource("Danger") as System.Windows.Media.Brush : FindResource("Surface2") as System.Windows.Media.Brush;
    }

    // ═══════════════════════════════════════════════════════════════
    // DELETE CONFIRM POPOVER HANDLERS
    // ═══════════════════════════════════════════════════════════════

    void ConfirmDelete_Click(object sender, RoutedEventArgs e) => CloseAllDeletePopups();
    void CancelDelete_Click(object sender, RoutedEventArgs e) => CloseAllDeletePopups();

    void CloseAllDeletePopups()
    {
        DeleteProfileToggle.IsChecked = false;
        DeleteBindingToggle.IsChecked = false;
        DeleteMacroToggle.IsChecked = false;
        DeleteRecoilToggle.IsChecked = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // RECOIL PATTERN VISUALIZER
    // ═══════════════════════════════════════════════════════════════

    void OnRecoilRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (RecoilRow r in e.NewItems)
                r.PropertyChanged += (_, _) => DrawRecoilPattern();
        DrawRecoilPattern();
    }

    void DrawRecoilPattern()
    {
        RecoilCanvas.Children.Clear();
        var rows = _vm.RecoilRows;
        RecoilVizEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Count == 0) return;

        double w = RecoilCanvas.ActualWidth;
        double h = RecoilCanvas.ActualHeight;
        if (w < 10 || h < 10) return;

        // Cumulative Dx/Dy positions
        double cumX = 0, cumY = 0;
        var points = new List<(double x, double y)> { (0, 0) };
        foreach (var row in rows)
        {
            cumX += row.Dx;
            cumY += row.Dy;
            points.Add((cumX, cumY));
        }

        // Normalize to canvas with padding
        double maxAbsX = Math.Max(1, points.Max(p => Math.Abs(p.x)));
        double maxAbsY = Math.Max(1, points.Max(p => Math.Abs(p.y)));
        double pad = 18;
        double scale = Math.Min((w - 2 * pad) / (2 * maxAbsX), (h - 2 * pad) / maxAbsY) * 0.85;
        double cx = w / 2;
        double cy = h - pad;

        // Axis lines (subtle)
        var axisBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x26, 0x30));
        RecoilCanvas.Children.Add(new Line { X1 = pad, Y1 = cy, X2 = w - pad, Y2 = cy, Stroke = axisBrush, StrokeThickness = 1 });
        RecoilCanvas.Children.Add(new Line { X1 = cx, Y1 = pad, X2 = cx, Y2 = h - pad, Stroke = axisBrush, StrokeThickness = 1 });

        // Recoil trajectory path
        var accent = FindResource("Accent") as System.Windows.Media.Brush;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new System.Windows.Point(cx + points[0].x * scale, cy - points[0].y * scale), false, false);
            foreach (var p in points.Skip(1))
                ctx.LineTo(new System.Windows.Point(cx + p.x * scale, cy - p.y * scale), true, false);
        }
        RecoilCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geo,
            Stroke = accent,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 3, 2 },
        });

        // Shot points
        for (int i = 0; i < points.Count; i++)
        {
            var dot = new Ellipse { Width = 5, Height = 5, Fill = accent };
            Canvas.SetLeft(dot, cx + points[i].x * scale - 2.5);
            Canvas.SetTop(dot, cy - points[i].y * scale - 2.5);
            RecoilCanvas.Children.Add(dot);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // WINDOW / TRAY
    // ═══════════════════════════════════════════════════════════════

    void OnStateChanged(object? sender, EventArgs e)
    {
        if (!_vm.MinimizeToTray) return;
        if (WindowState != WindowState.Minimized) return;
        if (_tray is null) EnsureTray();
        _trayMinimized = true;
        Hide();
    }

    void EnsureTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "AegisMouse",
            Visible = true
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        var menu = new System.Windows.Forms.ContextMenuStrip();
menu.Items.Add("Open AegisMouse", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _exiting = true;
            _tray.Visible = false;
            System.Windows.Application.Current.Shutdown();
        });
        _tray.ContextMenuStrip = menu;
    }

    void RestoreFromTray()
    {
        _trayMinimized = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && _vm.MinimizeToTray && !_trayMinimized)
        {
            OnStateChanged(sender, EventArgs.Empty);
            e.Cancel = true;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        ModernChrome.Apply(hwnd);
        // Always force solid black — no translucent OS backdrop.
        Background = FindResource("BgSolid") as System.Windows.Media.Brush;
    }

    protected override void OnClosed(EventArgs e)
    {
        _aim.Dispose();
        _debugWin?.Close();
        _tray?.Dispose();
        _tray = null;
        base.OnClosed(e);
    }
}
