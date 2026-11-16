using MagneticDesk.Models;
using Windows.Graphics;

namespace MagneticDesk.Services;

internal readonly record struct MagneticDockContact(
    MagneticDockEdge SourceEdge,
    MagneticDockEdge TargetEdge,
    int ContactCoordinate,
    int Distance,
    RectInt32 DockedBounds);

internal static class WidgetMagneticDockPolicy
{
    public static MagneticDockContact? FindContact(
        RectInt32 source, RectInt32 target, int spacing, int threshold, int minimumOverlap)
    {
        if (source.Width <= 0 || source.Height <= 0 || target.Width <= 0 || target.Height <= 0)
        {
            return null;
        }
        spacing = Math.Max(0, spacing);
        threshold = Math.Max(0, threshold);
        int verticalStart = Math.Max(source.Y, target.Y);
        int verticalEnd = Math.Min(source.Y + source.Height, target.Y + target.Height);
        int horizontalStart = Math.Max(source.X, target.X);
        int horizontalEnd = Math.Min(source.X + source.Width, target.X + target.Width);
        var contacts = new List<MagneticDockContact>(4);
        if (verticalEnd - verticalStart >= Math.Max(1, minimumOverlap))
        {
            int coordinate = verticalStart + (verticalEnd - verticalStart) / 2;
            int rightX = target.X - spacing - source.Width;
            int leftX = target.X + target.Width + spacing;
            contacts.Add(new(MagneticDockEdge.Right, MagneticDockEdge.Left, coordinate,
                Math.Abs(source.X - rightX), new(rightX, source.Y, source.Width, source.Height)));
            contacts.Add(new(MagneticDockEdge.Left, MagneticDockEdge.Right, coordinate,
                Math.Abs(source.X - leftX), new(leftX, source.Y, source.Width, source.Height)));
        }
        if (horizontalEnd - horizontalStart >= Math.Max(1, minimumOverlap))
        {
            int coordinate = horizontalStart + (horizontalEnd - horizontalStart) / 2;
            int bottomY = target.Y - spacing - source.Height;
            int topY = target.Y + target.Height + spacing;
            contacts.Add(new(MagneticDockEdge.Bottom, MagneticDockEdge.Top, coordinate,
                Math.Abs(source.Y - bottomY), new(source.X, bottomY, source.Width, source.Height)));
            contacts.Add(new(MagneticDockEdge.Top, MagneticDockEdge.Bottom, coordinate,
                Math.Abs(source.Y - topY), new(source.X, topY, source.Width, source.Height)));
        }
        return contacts.Where(contact => contact.Distance <= threshold)
            .OrderBy(contact => contact.Distance).Select(contact => (MagneticDockContact?)contact).FirstOrDefault();
    }
}

internal sealed class MagneticDockCountdown
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);
    private string? _candidate;
    private TimeSpan _startedAt;

    public double Progress { get; private set; }
    public int SecondsRemaining => (int)Math.Ceiling((1 - Progress) * Duration.TotalSeconds);
    public bool IsComplete => _candidate is not null && Progress >= 1;

    public void Update(string? candidate, TimeSpan now)
    {
        if (candidate is null || !string.Equals(candidate, _candidate, StringComparison.Ordinal))
        {
            _candidate = candidate;
            _startedAt = now;
            Progress = 0;
            return;
        }
        Progress = Math.Clamp((now - _startedAt).TotalSeconds / Duration.TotalSeconds, 0, 1);
    }
}
