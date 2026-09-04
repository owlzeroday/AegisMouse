using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AegisMouse.Core;

namespace AegisMouse.App;

/// <summary>
/// Click-through overlay crosshair. Never takes focus, never intercepts input
/// (WS_EX_TRANSPARENT + NOACTIVATE), closes with the main window.
/// NOTE: focus prevention lives in the NOACTIVATE ex-style, NOT in
/// ShowActivated — WPF throws InvalidOperationException when showing a
/// ShowActivated=false window that is Maximized, which used to kill the main
/// window at startup whenever the overlay was enabled.
/// </summary>
public partial class CrosshairWindow : Window
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    CrosshairSettings _settings = new();
    readonly DispatcherTimer _followTimer;

    public CrosshairWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Render();
        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _followTimer.Tick += (_, _) => { if (_settings is { Enabled: true, FollowCursor: true }) Render(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE,
            GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public void Apply(CrosshairSettings s)
    {
        ProfileSanitizer.Sanitize(s);
        _settings = s;
        _followTimer.IsEnabled = s is { Enabled: true, FollowCursor: true };
        Render();
    }

    /// <summary>Re-render once visible (post-Show layout pass).</summary>
    public void Refresh() => Render();

    void Render()
    {
        Pad.Children.Clear();
        if (!_settings.Enabled) return;
        // Before first layout ActualWidth/Height are 0 — fall back to the
        // primary screen so Apply-before-Show still draws; SizeChanged redraws
        // at the real size right after.
        double w = ActualWidth >= 10 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        double h = ActualHeight >= 10 ? ActualHeight : SystemParameters.PrimaryScreenHeight;
        if (w < 10 || h < 10) return;

        var mousePx = System.Windows.Forms.Control.MousePosition;
        System.Windows.Point anchor = _settings.FollowCursor
            ? new System.Windows.Point(mousePx.X, mousePx.Y)
            : new System.Windows.Point(w / 2 + _settings.OffsetX, h / 2 + _settings.OffsetY);
        // MousePosition is screen px; WPF canvas is device-independent — convert.
        if (_settings.FollowCursor)
        {
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget is { } ct) anchor = ct.TransformFromDevice.Transform(anchor);
        }

        byte a = (byte)(_settings.ColorArgb >> 24), r = (byte)(_settings.ColorArgb >> 16),
             g = (byte)(_settings.ColorArgb >> 8), b = (byte)_settings.ColorArgb;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        double t = _settings.ThicknessPx, half = _settings.SizePx / 2.0;

        switch (_settings.Style)
        {
            case CrosshairStyle.Dot:
                var dot = new Ellipse { Width = _settings.SizePx, Height = _settings.SizePx, Fill = brush };
                Canvas.SetLeft(dot, anchor.X - half);
                Canvas.SetTop(dot, anchor.Y - half);
                Pad.Children.Add(dot);
                break;
            case CrosshairStyle.Circle:
                var ring = new Ellipse
                {
                    Width = _settings.SizePx, Height = _settings.SizePx,
                    Stroke = brush, StrokeThickness = t
                };
                Canvas.SetLeft(ring, anchor.X - half);
                Canvas.SetTop(ring, anchor.Y - half);
                Pad.Children.Add(ring);
                var pip = new Ellipse { Width = t + 1, Height = t + 1, Fill = brush };
                Canvas.SetLeft(pip, anchor.X - (t + 1) / 2);
                Canvas.SetTop(pip, anchor.Y - (t + 1) / 2);
                Pad.Children.Add(pip);
                break;
            default: // Cross
                var hLine = new Line
                {
                    X1 = anchor.X - half, Y1 = anchor.Y, X2 = anchor.X + half, Y2 = anchor.Y,
                    Stroke = brush, StrokeThickness = t
                };
                var vLine = new Line
                {
                    X1 = anchor.X, Y1 = anchor.Y - half, X2 = anchor.X, Y2 = anchor.Y + half,
                    Stroke = brush, StrokeThickness = t
                };
                Pad.Children.Add(hLine);
                Pad.Children.Add(vLine);
                break;
        }
    }
}
