using Windows.Graphics;

namespace MagneticDesk.Services;

internal static class WidgetMagneticPlacement
{
    public static bool ShouldMoveTogether(bool controlPressed, string? groupId)
    {
        return !controlPressed && !string.IsNullOrWhiteSpace(groupId);
    }

    public static bool SharesMoveScope(string? sourceGroupId, string? candidateGroupId)
    {
        return !string.IsNullOrWhiteSpace(sourceGroupId) &&
            string.Equals(sourceGroupId, candidateGroupId, StringComparison.Ordinal);
    }

    public static RectInt32 SnapBeside(RectInt32 source, RectInt32 target, int spacing)
    {
        spacing = Math.Max(0, spacing);
        PointInt32[] origins =
        [
            new(target.X + target.Width + spacing, target.Y),
            new(target.X - source.Width - spacing, target.Y),
            new(target.X, target.Y + target.Height + spacing),
            new(target.X, target.Y - source.Height - spacing)
        ];
        PointInt32 closest = origins.MinBy(point =>
            Math.Pow((double)point.X - source.X, 2) + Math.Pow((double)point.Y - source.Y, 2));
        return new RectInt32(closest.X, closest.Y, source.Width, source.Height);
    }
}
