using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AegisMouse.App.ViewModels;
using AegisMouse.Database;
using AegisMouse.Engine;
using Serilog;
using Path = System.IO.Path;

namespace AegisMouse.App;

public partial class AimDebugWindow : Window
{
    readonly MainViewModel _vm;
    readonly AimAssistService _aim;
    AimFrame? _last;

    public AimDebugWindow(MainViewModel vm, AimAssistService aim)
    {
        InitializeComponent();
        _vm = vm;
        _aim = aim;
        DataContext = vm;
        aim.DebugActive = true;
        aim.DebugFrame += OnFrame;
        Closed += (_, _) =>
        {
            aim.DebugFrame -= OnFrame;
            aim.DebugActive = false;
        };

        // Dock to the bottom-right corner of the monitor the game is on, away from the crosshair
        var wa = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Left = wa.Right / dpi - Width - 16;
        Top = wa.Bottom / dpi - Height - 16;

        // F9 / Ctrl+S saves the current frame without moving the mouse off the target
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F9 || (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                SaveFrameNow();
                e.Handled = true;
            }
        };
    }

    void OnFrame(AimFrame f)
    {
        Dispatcher.BeginInvoke(() => Render(f));
    }

    void Render(AimFrame f)
    {
        _last = f;
        NoFrame.Visibility = Visibility.Collapsed;

        var bmp = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
        bmp.Freeze();
        Preview.Source = bmp;

        Overlay.Children.Clear();
        double sx = Preview.ActualWidth / f.Width;
        double sy = Preview.ActualHeight / f.Height;

        // FOV circle
        var fov = new Ellipse
        {
            Width = f.FovRadiusPx * 2 * sx,
            Height = f.FovRadiusPx * 2 * sy,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0x00, 0xE6, 0x76)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
        };
        Canvas.SetLeft(fov, (f.CenterX - f.FovRadiusPx) * sx);
        Canvas.SetTop(fov, (f.CenterY - f.FovRadiusPx) * sy);
        Overlay.Children.Add(fov);

        // Crosshair center
        var cx = new Line { X1 = (f.CenterX - 8) * sx, Y1 = f.CenterY * sy, X2 = (f.CenterX + 8) * sx, Y2 = f.CenterY * sy, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1 };
        var cy = new Line { X1 = f.CenterX * sx, Y1 = (f.CenterY - 8) * sy, X2 = f.CenterX * sx, Y2 = (f.CenterY + 8) * sy, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1 };
        Overlay.Children.Add(cx);
        Overlay.Children.Add(cy);

        // Target boxes + aim points
        foreach (var t in f.Targets)
        {
            bool sel = ReferenceEquals(t, f.Selected);
            var brush = sel ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.Lime;
            var box = new System.Windows.Shapes.Rectangle
            {
                Width = t.Width * sx,
                Height = t.Height * sy,
                Stroke = brush,
                StrokeThickness = sel ? 2.5 : 1.2,
            };
            Canvas.SetLeft(box, t.Left * sx);
            Canvas.SetTop(box, t.Top * sy);
            Overlay.Children.Add(box);

            var aim = new Ellipse { Width = 4, Height = 4, Fill = sel ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.Lime };
            Canvas.SetLeft(aim, t.CenterX * sx - 2);
            Canvas.SetTop(aim, t.AimY(_vm.AimOffsetPct) * sy - 2);
            Overlay.Children.Add(aim);
        }

        Stats.Text =
            $"capture: {f.CaptureMs} ms · detect: {f.DetectMs} ms\n" +
            $"targets: {f.Targets.Count} · selected: {(f.Selected is null ? "—" : $"{f.Selected.CenterX},{f.Selected.CenterY}")} · dist: {f.DistancePx:0}\n" +
            $"armed: {(f.Armed ? "YES (pull active)" : "no — hold trigger to pull")} · capture: {f.Width}×{f.Height}";
    }

    void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_last is null || Preview.Source is null) return;
        var pos = e.GetPosition(Preview);
        int x = (int)(pos.X / Preview.ActualWidth * _last.Width);
        int y = (int)(pos.Y / Preview.ActualHeight * _last.Height);
        if (x < 0 || y < 0 || x >= _last.Width || y >= _last.Height) return;
        int i = (y * _last.Width + x) * 4;
        byte b = _last.Bgra[i], g = _last.Bgra[i + 1], r = _last.Bgra[i + 2];
        _vm.PickAimColor(b, g, r, tolerance: 8);
        var (h, s, v) = AimVision.HsvOf(b, g, r);
        PickedSwatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        PickedInfo.Text = $"R{r} G{g} B{b}  →  H{h} S{s} V{v}";
    }

    void SaveFrame_Click(object sender, RoutedEventArgs e) => SaveFrameNow();

    void SaveFrameNow()
    {
        try
        {
            if (_last is null) { SaveNote.Text = "No frame yet — open the game first."; return; }
            var dir = Path.Combine(AppPaths.Root, "aimdebug");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var png = Path.Combine(dir, $"frame-{stamp}.png");
            using (var bmp = new System.Drawing.Bitmap(_last.Width, _last.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(_last.Bgra, 0, data.Scan0, _last.Bgra.Length);
                bmp.UnlockBits(data);
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
            var json = Path.Combine(dir, $"frame-{stamp}.json");
            File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(new
            {
                _last.Width,
                _last.Height,
                ScreenLeft = _last.ScreenLeft,
                ScreenTop = _last.ScreenTop,
                Settings = _vm.BuildAimSettings()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            SaveNote.Text = $"Saved {Path.GetFileName(png)} + settings. Send me the PNG.";
            Log.Information("Aim debug frame saved to {Path}", png);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save aim debug frame");
            SaveNote.Text = "Failed to save: " + ex.Message;
        }
    }
}
