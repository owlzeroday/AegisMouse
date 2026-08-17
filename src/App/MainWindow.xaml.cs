using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AegisMouse.App.ViewModels;
using AegisMouse.Database;
using Serilog;

namespace AegisMouse.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    System.Windows.Forms.NotifyIcon? _tray;
    bool _trayMinimized;
    bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(App.Db!, App.Engine!);
        DataContext = _vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Calibrating)) UpdateCalibBadge();
        };
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        UpdateCalibBadge();
    }

    void UpdateCalibBadge()
    {
        CalibBadge.Visibility = _vm.Calibrating ? Visibility.Visible : Visibility.Collapsed;
        CalibrateBtn.Content = _vm.Calibrating ? "Stop calibration" : "Calibrate";
        CalibrateBtn.Background = _vm.Calibrating ? FindResource("Danger") as System.Windows.Media.Brush : FindResource("Surface2") as System.Windows.Media.Brush;
    }

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
        if (!ModernChrome.Apply(hwnd))
            Background = FindResource("BgSolid") as System.Windows.Media.Brush;
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _tray = null;
        base.OnClosed(e);
    }
}