using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using AegisMouse.Core;
using AegisMouse.Engine.Interop;
using AegisMouse.Engine.Timing;
using Serilog;

namespace AegisMouse.Engine;

/// <summary>One processed frame delivered to the debug vision window.</summary>
public sealed class AimFrame
{
    public required byte[] Bgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public int CaptureMs { get; init; }
    public int DetectMs { get; init; }
    /// <summary>Crosshair center in capture-space (the capture region's center pixel).</summary>
    public int CenterX { get; init; }
    public int CenterY { get; init; }
    /// <summary>Current cursor position in capture-space (the moving marker in the debug preview).</summary>
    public int CursorX { get; init; }
    public int CursorY { get; init; }
    public int ScreenLeft { get; init; }
    public int ScreenTop { get; init; }
    public int FovRadiusPx { get; init; }
    public required List<AimTarget> Targets { get; init; }
    public AimTarget? Selected { get; init; }
    public double DistancePx { get; init; }
    public bool Armed { get; init; }
    public string? Status { get; init; }
}

/// <summary>
/// Soft aim assist loop: captures the region around the cursor, color-detects targets,
/// and pulls the mouse a fraction of the remaining distance toward the nearest in-FOV
/// target while the trigger is held. Frames are also streamed to the debug window so the
/// vision can be tuned live. Idle cost is zero — capture only runs while armed/debug.
/// </summary>
public sealed class AimAssistService : IDisposable
{
    readonly Stopwatch _sw = Stopwatch.StartNew();
    volatile bool _running;
    volatile bool _armed;
    volatile bool _debug;
    volatile bool _fixedCenter;
    volatile int _fixX, _fixY;
    AimAssistSettings? _settings;
    HumanizeSettings? _humanize;
    readonly Humanizer _humanizer = new();
    readonly TargetLock _lock = new();
    double _smX, _smY;   // EMA-smoothed output (kills per-frame rotational jitter)
    Thread? _thread;

    /// <summary>Debug/vision frames (raised on the capture thread; only while the debug window is open).</summary>
    public event Action<AimFrame>? DebugFrame;

    public bool DebugActive { get => _debug; set { _debug = value; if (!value) _fixedCenter = false; } }

    public void Start(AimAssistSettings settings, HumanizeSettings humanize)
    {
        _settings = settings;
        _humanize = humanize;
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "AimAssist" };
        _thread.Start();
        Log.Information("Aim assist service started");
    }

    public void SetSettings(AimAssistSettings s) => _settings = s;
    public void SetHumanize(HumanizeSettings h) => _humanize = h;

    /// <summary>True while the trigger key/button is held (or debug vision is open).</summary>
    public void SetArmed(bool armed)
    {
        _armed = armed;
        if (!armed) { _lock.Reset(); _smX = _smY = 0; }  // never resume a stale lock or fling
    }

    /// <summary>Freeze the capture region at a fixed screen point (used by the debug window).</summary>
    public void SetDebugCenter(int x, int y) { _fixX = x; _fixY = y; _fixedCenter = true; }

    /// <summary>Re-freeze the capture region at the current cursor position.</summary>
    public void RecenterNow() { var c = Cursor.Position; _fixX = c.X; _fixY = c.Y; }

    void Loop()
    {
        while (_running)
        {
            var s = _settings;
            var h = _humanize;
            if (s is null || h is null) { Thread.Sleep(50); continue; }
            if (!_armed && !_debug) { Thread.Sleep(120); continue; }

            int size = Math.Clamp(s.CaptureRadiusPx, 48, 900) * 2;
            var cursor = Cursor.Position;

            // Capture region: fixed while the debug window is open (steady preview), cursor-following otherwise
            int capLeft = _fixedCenter ? _fixX - size / 2 : cursor.X - size / 2;
            int capTop = _fixedCenter ? _fixY - size / 2 : cursor.Y - size / 2;

            var cap = Capture(capLeft, capTop, size, size);
            if (cap is null) { Thread.Sleep(33); continue; }

            var sw = Stopwatch.StartNew();
            var targets = AimVision.Detect(cap.Bgra, cap.Width, cap.Height, s);
            int detectMs = (int)sw.ElapsedMilliseconds;

            int cx = cap.Width / 2, cy = cap.Height / 2;
            int curX = cursor.X - cap.Left, curY = cursor.Y - cap.Top;   // current cursor in capture-space

            // Sticky lock (debounce + hysteresis) instead of re-picking nearest
            // every frame — the old stateless pick is what caused wild rotation
            // when 2+ blobs were visible.
            AimTarget? sel = null;
            if (_armed && s.Enabled)
            {
                double fovSq = (double)s.FovRadiusPx * s.FovRadiusPx;
                var inFov = targets.Where(t =>
                {
                    double ex = t.CenterX - curX, ey = t.CenterY - curY;
                    return ex * ex + ey * ey <= fovSq;
                }).ToList();
                sel = _lock.Update(inFov, curX, curY, s);
            }
            else
            {
                _lock.Reset();
                _smX = _smY = 0;
            }
            // Overlay shows the tracked box even during debounce so tuning stays visible.
            var shown = _armed ? _lock.Current : null;
            double dist = sel is null ? 0 : Math.Sqrt(Math.Pow(sel.CenterX - curX, 2) + Math.Pow(sel.CenterY - curY, 2));

            if (sel is not null)
            {
                // Lead strafing targets: shift the aim point by the lock's
                // predicted motion (2 frames, capped) instead of chasing stale pixels.
                var (predX, predY) = _lock.Predict(2, 48);
                int aimY = sel.AimY(Math.Clamp(s.AimOffsetPct, 0, 100))
                    + (int)Math.Round(predY - sel.CenterY);
                int dx = (int)Math.Round(predX) - curX, dy = aimY - curY;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > s.DeadzonePx)
                {
                    double pull = Math.Min(d * Math.Clamp(s.PullStrength, 0.01, 0.99), s.MaxTurnPxPerFrame);
                    int tx = (int)Math.Round(dx / d * pull);
                    int ty = (int)Math.Round(dy / d * pull);
                    // EMA smoothing: output chases the target instead of jumping to
                    // it — consecutive frames blend instead of whipping the view.
                    double k = 1.0 - Math.Clamp(s.SmoothFactor, 0.05, 0.95);
                    _smX += (tx - _smX) * k;
                    _smY += (ty - _smY) * k;
                    var (jx, jy) = _humanizer.Move((int)Math.Round(_smX), (int)Math.Round(_smY), h);
                    if (jx != 0 || jy != 0) NativeInput.MoveRelative(jx, jy);
                }
                else { _smX = _smY = 0; }
            }
            else { _smX = _smY = 0; }

            // Stream to debug window (throttled by the loop rate; copies only when a sink exists)
            if (_debug)
            {
                var frame = new AimFrame
                {
                    Bgra = cap.Bgra,
                    Width = cap.Width,
                    Height = cap.Height,
                    CaptureMs = cap.CaptureMs,
                    DetectMs = detectMs,
                    CenterX = cx,
                    CenterY = cy,
                    CursorX = curX,
                    CursorY = curY,
                    ScreenLeft = cap.Left,
                    ScreenTop = cap.Top,
                    FovRadiusPx = s.FovRadiusPx,
                    Targets = targets,
                    Selected = shown,
                    DistancePx = dist,
                    Armed = _armed
                };
                DebugFrame?.Invoke(frame);
            }

            Thread.Sleep(_debug ? 33 : 16);
        }
    }

    static CapturedRegion? Capture(int left, int top, int width, int height)
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        int l = Math.Max(left, vs.Left), t = Math.Max(top, vs.Top);
        int r = Math.Min(left + width, vs.Left + vs.Width), b = Math.Min(top + height, vs.Top + vs.Height);
        if (r <= l || b <= t) return null;
        int w = r - l, h = b - t;

        var sw = Stopwatch.StartNew();
        try
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(l, t, 0, 0, new Size(w, h));
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[w * h * 4];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                return new CapturedRegion(bytes, w, h, l, t, (int)sw.ElapsedMilliseconds);
            }
            finally { bmp.UnlockBits(data); }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Screen capture failed");
            return null;
        }
    }

    sealed record CapturedRegion(byte[] Bgra, int Width, int Height, int Left, int Top, int CaptureMs);

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(500); } catch { }
        _thread = null;
    }
}
