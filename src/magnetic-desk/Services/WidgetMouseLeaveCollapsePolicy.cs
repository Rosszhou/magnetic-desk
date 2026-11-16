namespace MagneticDesk.Services;

internal sealed class WidgetMouseLeaveCollapsePolicy
{
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);
    private TimeSpan? _outsideSince;

    internal double RemainingFraction(TimeSpan now) => _outsideSince is { } start
        ? Math.Clamp(1 - (now - start).TotalMilliseconds / Delay.TotalMilliseconds, 0, 1) : 1;

    internal bool IsCounting => _outsideSince.HasValue;

    internal bool Update(TimeSpan now, bool hasActivity)
    {
        if (hasActivity)
        {
            _outsideSince = null;
            return false;
        }
        if (_outsideSince is null || now < _outsideSince.Value) _outsideSince = now;
        return now - _outsideSince.Value >= Delay;
    }
}
