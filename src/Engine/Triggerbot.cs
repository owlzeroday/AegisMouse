namespace AegisMouse.Engine;

/// <summary>
/// One-shot-per-acquisition triggerbot: fires when a locked target holds inside
/// tolerance for the configured delay, then stays quiet until the aim leaves
/// (re-arm) — no machine-gun spraying on a lingering lock.
/// Pure logic, no I/O — fully unit-testable. The service owns the actual click.
/// </summary>
public sealed class TriggerbotState
{
    double _heldMs;
    bool _fired;

    public void Reset()
    {
        _heldMs = 0;
        _fired = false;
    }

    /// <param name="locked">A debounced lock exists this frame.</param>
    /// <param name="distPx">Aim-point distance to crosshair.</param>
    /// <param name="tolerancePx">Fire only within this distance.</param>
    /// <param name="delayMs">Must hold inside tolerance this long before firing.</param>
    /// <param name="dtMs">Frame time.</param>
    /// <returns>True exactly once per acquisition when it is time to fire.</returns>
    public bool Update(bool locked, double distPx, int tolerancePx, int delayMs, double dtMs)
    {
        if (!locked || distPx > tolerancePx)
        {
            _heldMs = 0;
            // Re-arm only once the aim truly leaves (2x band), so borderline
            // jitter at the edge can't machine-gun repeated shots.
            if (!locked || distPx > tolerancePx * 2) _fired = false;
            return false;
        }
        if (_fired) return false;
        _heldMs += Math.Max(0, dtMs);
        if (_heldMs >= Math.Max(0, delayMs))
        {
            _fired = true;
            _heldMs = 0;
            return true;
        }
        return false;
    }
}
