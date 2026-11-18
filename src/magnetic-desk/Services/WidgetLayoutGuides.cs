using Windows.Graphics;

namespace MagneticDesk.Services;

internal readonly record struct WidgetLayoutGuide(RectInt32 Bounds);

internal static class WidgetLayoutGuides
{
    internal static IReadOnlyList<WidgetLayoutGuide> Outline(RectInt32 bounds, double scale)
    {
        int thickness = Math.Max(2, (int)Math.Round(2 * scale));
        return
        [
            new(new(bounds.X, bounds.Y, bounds.Width, thickness)),
            new(new(bounds.X, bounds.Y + bounds.Height - thickness, bounds.Width, thickness)),
            new(new(bounds.X, bounds.Y, thickness, bounds.Height)),
            new(new(bounds.X + bounds.Width - thickness, bounds.Y, thickness, bounds.Height))
        ];
    }

    internal static IReadOnlyList<WidgetLayoutGuide> Drag(
        WidgetMoveSnapResult result, IReadOnlyList<WidgetSnapTarget> targets,
        RectInt32? workArea, double scale)
    {
        var guides = new List<WidgetLayoutGuide>();
        int thickness = Math.Max(1, (int)Math.Round(scale));
        AddAlignment(guides, result.Bounds, result.HorizontalMatch, targets, workArea, thickness);
        AddAlignment(guides, result.Bounds, result.VerticalMatch, targets, workArea, thickness);
        return guides;
    }

    private static void AddAlignment(List<WidgetLayoutGuide> guides, RectInt32 source,
        WidgetSnapMatch? match, IReadOnlyList<WidgetSnapTarget> targets, RectInt32? workArea, int thickness)
    {
        if (match is not { UsesSpacing: false } aligned) return;
        RectInt32? target = aligned.TargetWindowHandle == IntPtr.Zero
            ? workArea
            : targets.Where(t => t.WindowHandle == aligned.TargetWindowHandle)
                .Select(t => (RectInt32?)t.Bounds).FirstOrDefault();
        if (target is not { } peer) return;
        bool vertical = aligned.SourceEdge is WidgetSnapEdge.Left or WidgetSnapEdge.Right;
        int start = vertical ? Math.Min(source.Y, peer.Y) : Math.Min(source.X, peer.X);
        int end = vertical ? Math.Max(source.Y + source.Height, peer.Y + peer.Height)
            : Math.Max(source.X + source.Width, peer.X + peer.Width);
        int coordinate = aligned.SourceEdge is WidgetSnapEdge.Right or WidgetSnapEdge.Bottom
            ? aligned.Coordinate - thickness : aligned.Coordinate;
        guides.Add(new(vertical
            ? new(coordinate, start, thickness, end - start)
            : new(start, coordinate, end - start, thickness)));
    }
}
