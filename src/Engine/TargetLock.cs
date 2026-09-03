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
///      momentarily appears nearer the crosshair.
///   3. Hysteresis — a challenger steals the lock only when it is clearly closer
///      (30 px margin), so borderline flicker can't dethrone the lock.
/// Pure logic, no I/O — fully unit-testable.
/// </summary>
public sealed class TargetLock
{
    /// <summary>Lock must move less than this (px, capture-space) per frame to count as "same blob".</summary>
    const double ContinueDistPx = 48;
    /// <summary>Challenger must beat the lock by this margin (px) to steal it.</summary>
    const double StealMarginPx = 30;

    AimTarget? _tracked;
    int _seen;

    /// <summary>Currently tracked blob (pre-debounce — useful for the debug overlay).</summary>
    public AimTarget? Current => _tracked;

    public void Reset()
    {
        _tracked = null;
        _seen = 0;
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
            if (cont is not null && CenterDist(cont, _tracked) <= ContinueDistPx)
            {
                _tracked = cont;
                _seen++;
                if (ChallengerSteals(targets, cont, cx, cy))
                {
                    _tracked = Nearest(targets, cx, cy);
                    _seen = 1;
                    return _seen >= s.LockFrames ? _tracked : null;
                }
                return _seen >= s.LockFrames ? _tracked : null;
            }
            // Tracked blob vanished — fall through to fresh acquisition.
        }

        _tracked = Nearest(targets, cx, cy);
        _seen = 1;
        return _seen >= s.LockFrames ? _tracked : null;
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
