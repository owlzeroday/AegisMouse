using AegisMouse.Core;

namespace AegisMouse.Engine;

/// <summary>
/// Sticky target lock: the thing that stops wild aim rotation.
///
/// The old loop re-picked "nearest blob to crosshair" every single frame with
/// zero memory. With 2+ blobs on screen (grass, props, a real enemy) the pick
/// flickered A,B,A,B at 60 Hz and the pull direction flipped with it — that is
/// the spin the user sees. This tracker instead:
///   1. Debounce — a blob must persist <see cref="AimAssistSettings.LockFrames"/>
///      consecutive frames before it becomes pullable (1-frame speckles never engage).
///   2. Stickiness — once locked, the same blob is followed even if another blob
///      momentarily appears nearer the crosshair (IoU + center hybrid matching
///      rides through split/merge flicker from partial occlusion).
///   3. Hysteresis — a challenger steals the lock only when it is clearly closer
///      (30 px margin), so borderline flicker can't dethrone the lock.
///   4. Prediction — short center history extrapolates where a strafing enemy
///      will be next frame, so the pull leads instead of trailing behind.
/// Pure logic, no I/O — fully unit-testable.
/// </summary>
public sealed class TargetLock
{
    /// <summary>Lock must move less than this (px, capture-space) per frame to count as "same blob".</summary>
    const double ContinueDistPx = 48;
    /// <summary>Continuation gate is measured around the PREDICTED center (not the
    /// last center) and widened 1.5x — fast strafers jump past the raw gate
    /// between frames at low capture rates.</summary>
    const double ContinueDistFactor = 1.5;
    /// <summary>A fresh blob this close to the crosshair means the user retargeted —
    /// reseed immediately instead of coasting the stale lock.</summary>
    const double ReseedRadiusPx = 20;
    /// <summary>Overlapping this much (IoU) also counts as "same blob" even if the center jumped.</summary>
    const double ContinueIou = 0.15;
    /// <summary>Challenger must beat the lock by this margin (px) to steal it.</summary>
    const double StealMarginPx = 30;
    /// <summary>Frames of center history kept for velocity prediction.</summary>
    const int HistoryLen = 4;

    AimTarget? _tracked;
    int _seen;
    int _missed;
    readonly Queue<(double X, double Y)> _history = new();

    /// <summary>Currently tracked blob (pre-debounce — useful for the debug overlay).</summary>
    public AimTarget? Current => _tracked;

    public void Reset()
    {
        _tracked = null;
        _seen = 0;
        _missed = 0;
        _history.Clear();
    }

    /// <summary>
    /// Feed one frame of detections. Returns the locked target once it has
    /// persisted long enough to pull on, else null.
    ///
    /// Two mover fixes vs the old version: (1) continuation is matched around
    /// the PREDICTED center so fast strafers don't outrun the gate; (2) a
    /// pullable lock coasts through up to CoastFrames missed frames (mid-range
    /// shimmer drops detections for a frame or two — the old code reset the
    /// whole debounce to zero every time, so flickering targets never engaged).
    /// </summary>
    public AimTarget? Update(IReadOnlyList<AimTarget> targets, int cx, int cy, AimAssistSettings s)
    {
        int coast = Math.Clamp(s.CoastFrames, 0, 10);

        if (_tracked is not null)
        {
            // Area gate first: a 3 px speckle next to the lock must never win
            // "nearest" over the real (moved) box — mid-range flicker made the
            // lock hop to noise and the view whipped with it.
            var (fx, fy) = Predict(1, ContinueDistPx);
            var cont = NearestPassing(targets, _tracked, (int)Math.Round(fx), (int)Math.Round(fy));
            if (cont is not null && (CenterDist(cont, _tracked) <= ContinueDistPx * ContinueDistFactor || IoU(cont, _tracked) >= ContinueIou))
            {
                Track(cont);
                _missed = 0;
                if (ChallengerSteals(targets, cont, cx, cy))
                {
                    Track(Nearest(targets, cx, cy)!);
                    _seen = 1;
                    _missed = 0;
                    return _seen >= s.LockFrames ? _tracked : null;
                }
                return _seen >= s.LockFrames ? _tracked : null;
            }

            // Continuation lost this frame. Fresh blob right on the crosshair =
            // the user clearly retargeted: reseed at once, don't coast stale.
            var near = Nearest(targets, cx, cy);
            if (near is not null && !ReferenceEquals(near, _tracked)
                && Math.Sqrt(Math.Pow(near.CenterX - cx, 2) + Math.Pow(near.CenterY - cy, 2)) <= ReseedRadiusPx
                && !SameBox(near, _tracked))
            {
                Track(near);
                _seen = 1;
                _missed = 0;
                return _seen >= s.LockFrames ? _tracked : null;
            }

            // Otherwise coast the pullable lock through the gap.
            if (_seen >= s.LockFrames && _missed < coast)
            {
                _missed++;
                PushHistory(_tracked.CenterX, _tracked.CenterY);
                return _tracked;
            }
            // Tracked blob vanished — fall through to fresh acquisition.
        }

        if (targets.Count == 0)
        {
            // Empty frame: same coast deal, else the lock fully drops.
            if (_tracked is not null && _seen >= s.LockFrames && _missed < coast)
            {
                _missed++;
                PushHistory(_tracked.CenterX, _tracked.CenterY);
                return _tracked;
            }
            Reset();
            return null;
        }

        Track(Nearest(targets, cx, cy)!);
        _seen = 1;
        _missed = 0;
        return _seen >= s.LockFrames ? _tracked : null;
    }

    void PushHistory(double x, double y)
    {
        _history.Enqueue((x, y));
        while (_history.Count > HistoryLen) _history.Dequeue();
    }

    void Track(AimTarget t)
    {
        _tracked = t;
        _seen++;
        PushHistory(t.CenterX, t.CenterY);
    }

    /// <summary>
    /// Predicted center leadFrames ahead, from recent center velocity.
    /// Capped to maxLeadPx so a teleporting blob can't fling the view.
    /// Falls back to the current center when there is no history yet.
    /// </summary>
    public (double X, double Y) Predict(int leadFrames, double maxLeadPx)
    {
        if (_tracked is null) return (0, 0);
        if (_history.Count < 2 || leadFrames <= 0 || maxLeadPx <= 0)
            return (_tracked.CenterX, _tracked.CenterY);
        var arr = _history.ToArray();
        var first = arr[0];
        var last = arr[^1];
        // Stationary damping: sub-6 px of total wander is center noise, not
        // motion — extrapolating it would inject ±leadFrames of jitter into
        // small mid-range targets. Hold still instead.
        double wander = 0;
        for (int i = 1; i < arr.Length; i++)
            wander = Math.Max(wander, Math.Sqrt(Math.Pow(arr[i].X - arr[0].X, 2) + Math.Pow(arr[i].Y - arr[0].Y, 2)));
        if (wander < 6) return (last.X, last.Y);
        double n = arr.Length - 1;
        double vx = (last.X - first.X) / n, vy = (last.Y - first.Y) / n;
        double px = last.X + vx * leadFrames, py = last.Y + vy * leadFrames;
        px = last.X + Math.Clamp(px - last.X, -maxLeadPx, maxLeadPx);
        py = last.Y + Math.Clamp(py - last.Y, -maxLeadPx, maxLeadPx);
        return (px, py);
    }

    /// <summary>Intersection-over-union of two boxes (0 = disjoint, 1 = identical).</summary>
    public static double IoU(AimTarget a, AimTarget b)
    {
        int ix0 = Math.Max(a.Left, b.Left), iy0 = Math.Max(a.Top, b.Top);
        int ix1 = Math.Min(a.Left + a.Width, b.Left + b.Width);
        int iy1 = Math.Min(a.Top + a.Height, b.Top + b.Height);
        int iw = ix1 - ix0, ih = iy1 - iy0;
        if (iw <= 0 || ih <= 0) return 0;
        double inter = (double)iw * ih;
        double union = (double)a.Width * a.Height + (double)b.Width * b.Height - inter;
        return union <= 0 ? 0 : inter / union;
    }

    /// <summary>True when another blob is so much closer to the crosshair that the user clearly retargeted.</summary>
    static bool ChallengerSteals(IReadOnlyList<AimTarget> targets, AimTarget locked, int cx, int cy)
    {
        double lockedDist = Math.Sqrt(Math.Pow(locked.CenterX - cx, 2) + Math.Pow(locked.CenterY - cy, 2));
        foreach (var t in targets)
        {
            if (ReferenceEquals(t, locked)) continue;
            double d = Math.Sqrt(Math.Pow(t.CenterX - cx, 2) + Math.Pow(t.CenterY - cy, 2));
            if (d + StealMarginPx < lockedDist) return true;
        }
        return false;
    }

    static AimTarget? Nearest(IReadOnlyList<AimTarget> targets, int cx, int cy)
    {
        AimTarget? best = null;
        double bestDist = double.MaxValue;
        foreach (var t in targets)
        {
            double dx = t.CenterX - cx, dy = t.CenterY - cy;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestDist) { bestDist = d2; best = t; }
        }
        return best;
    }

    /// <summary>
    /// Nearest candidate whose area is within 0.25x–4x of the tracked box.
    /// Speckles can't hijack the lock; genuine growth/shrink (approach, crouch)
    /// still passes.
    /// </summary>
    static AimTarget? NearestPassing(IReadOnlyList<AimTarget> targets, AimTarget tracked, int cx, int cy)
    {
        double baseArea = Math.Max(1, tracked.Area);
        AimTarget? best = null;
        double bestDist = double.MaxValue;
        foreach (var t in targets)
        {
            double ratio = t.Area / baseArea;
            if (ratio < 0.25 || ratio > 4.0) continue;
            double dx = t.CenterX - cx, dy = t.CenterY - cy;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestDist) { bestDist = d2; best = t; }
        }
        return best;
    }

    static double CenterDist(AimTarget a, AimTarget b)
        => Math.Sqrt(Math.Pow(a.CenterX - b.CenterX, 2) + Math.Pow(a.CenterY - b.CenterY, 2));

    static bool SameBox(AimTarget a, AimTarget b)
        => a.Left == b.Left && a.Top == b.Top && a.Width == b.Width && a.Height == b.Height;
}
