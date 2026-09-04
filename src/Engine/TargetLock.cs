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
    /// <summary>Overlapping this much (IoU) also counts as "same blob" even if the center jumped.</summary>
    const double ContinueIou = 0.15;
    /// <summary>Challenger must beat the lock by this margin (px) to steal it.</summary>
    const double StealMarginPx = 30;
    /// <summary>Frames of center history kept for velocity prediction.</summary>
    const int HistoryLen = 4;

    AimTarget? _tracked;
    int _seen;
    readonly Queue<(double X, double Y)> _history = new();

    /// <summary>Currently tracked blob (pre-debounce — useful for the debug overlay).</summary>
    public AimTarget? Current => _tracked;

    public void Reset()
    {
        _tracked = null;
        _seen = 0;
        _history.Clear();
    }

    /// <summary>
    /// Feed one frame of detections. Returns the locked target once it has
    /// persisted long enough to pull on, else null.
    /// </summary>
    public AimTarget? Update(IReadOnlyList<AimTarget> targets, int cx, int cy, AimAssistSettings s)
    {
        if (targets.Count == 0)
        {
            Reset();
            return null;
        }

        if (_tracked is not null)
        {
            var cont = Nearest(targets, _tracked.CenterX, _tracked.CenterY);
            if (cont is not null && (CenterDist(cont, _tracked) <= ContinueDistPx || IoU(cont, _tracked) >= ContinueIou))
            {
                Track(cont);
                if (ChallengerSteals(targets, cont, cx, cy))
                {
                    Track(Nearest(targets, cx, cy)!);
                    _seen = 1;
                    return _seen >= s.LockFrames ? _tracked : null;
                }
                return _seen >= s.LockFrames ? _tracked : null;
            }
            // Tracked blob vanished — fall through to fresh acquisition.
        }

        Track(Nearest(targets, cx, cy)!);
        _seen = 1;
        return _seen >= s.LockFrames ? _tracked : null;
    }

    void Track(AimTarget t)
    {
        _tracked = t;
        _seen++;
        _history.Enqueue((t.CenterX, t.CenterY));
        while (_history.Count > HistoryLen) _history.Dequeue();
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

    static double CenterDist(AimTarget a, AimTarget b)
        => Math.Sqrt(Math.Pow(a.CenterX - b.CenterX, 2) + Math.Pow(a.CenterY - b.CenterY, 2));
}
