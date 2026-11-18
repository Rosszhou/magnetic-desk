using Windows.Graphics;

namespace MagneticDesk.Services;

internal static class WidgetMagneticResizeLayout
{
    internal static IReadOnlyDictionary<string, RectInt32> Arrange(
        IReadOnlyDictionary<string, RectInt32> original, string sourceId, RectInt32 proposed,
        IReadOnlyDictionary<string, RectInt32> attachment, string? anchor, int spacing, int tolerance)
    {
        var targets = original.ToDictionary();
        targets[sourceId] = proposed;
        AlignStackedWidths(original, targets, sourceId, attachment, anchor, spacing, tolerance);
        RectInt32 source = original[sourceId];
        ReflowAxis(original, targets, sourceId, spacing, tolerance, horizontal: true,
            forward: true, changed: proposed.X + proposed.Width != source.X + source.Width);
        ReflowAxis(original, targets, sourceId, spacing, tolerance, horizontal: true,
            forward: false, changed: proposed.X != source.X);
        ReflowAxis(original, targets, sourceId, spacing, tolerance, horizontal: false,
            forward: true, changed: proposed.Y + proposed.Height != source.Y + source.Height);
        ReflowAxis(original, targets, sourceId, spacing, tolerance, horizontal: false,
            forward: false, changed: proposed.Y != source.Y);
        return targets;
    }

    private static void AlignStackedWidths(IReadOnlyDictionary<string, RectInt32> original,
        Dictionary<string, RectInt32> targets, string sourceId,
        IReadOnlyDictionary<string, RectInt32> attachment, string? anchor, int spacing, int tolerance)
    {
        bool trailing = anchor?.Contains("Right", StringComparison.Ordinal) == true;
        var visited = new HashSet<string>(StringComparer.Ordinal) { sourceId };
        var pending = new Queue<string>();
        pending.Enqueue(sourceId);
        while (pending.TryDequeue(out string? parentId))
        {
            RectInt32 parent = original[parentId];
            RectInt32 moved = targets[parentId];
            foreach (var pair in original.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (visited.Contains(pair.Key) || !Touches(parent, pair.Value, horizontal: false, spacing, tolerance)) continue;
                double? alignment = Alignment(attachment[parentId], attachment[pair.Key], trailing, tolerance);
                if (alignment is not { } ratio) continue;
                int delta = moved.X - parent.X + (int)Math.Round((moved.Width - parent.Width) * ratio);
                RectInt32 child = targets[pair.Key];
                targets[pair.Key] = new RectInt32(pair.Value.X + delta, child.Y, child.Width, child.Height);
                visited.Add(pair.Key);
                pending.Enqueue(pair.Key);
            }
        }
    }

    private static double? Alignment(RectInt32 parent, RectInt32 child, bool preferTrailing, int tolerance)
    {
        int leading = child.X - parent.X;
        int trailing = child.X + child.Width - parent.X - parent.Width;
        if (Math.Abs(trailing) <= tolerance && (Math.Abs(leading) > tolerance || preferTrailing)) return 1;
        if (Math.Abs(leading) <= tolerance) return 0;
        int space = parent.Width - child.Width;
        if (space == 0) return null;
        double ratio = (double)leading / space;
        return ratio is >= 0 and <= 1 ? ratio : null;
    }

    private static void ReflowAxis(IReadOnlyDictionary<string, RectInt32> original,
        Dictionary<string, RectInt32> targets, string sourceId, int spacing, int tolerance,
        bool horizontal, bool forward, bool changed)
    {
        if (!changed) return;
        var ordered = original.OrderBy(p => forward ? Start(p.Value, horizontal) : -End(p.Value, horizontal))
            .ThenBy(p => p.Key == sourceId ? 0 : 1)
            .ThenBy(p => p.Key, StringComparer.Ordinal).ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            var pair = ordered[index];
            if (pair.Key == sourceId) continue;
            RectInt32 target = targets[pair.Key];
            var predecessors = ordered.Take(index)
                .Where(p => Overlaps(targets[p.Key], target, !horizontal)).ToArray();
            var supports = predecessors.Where(p => Touches(p.Value, pair.Value, horizontal, spacing, tolerance)).ToArray();
            int Position(RectInt32 bounds) => forward ? Start(bounds, horizontal) : End(bounds, horizontal);
            int Boundary(RectInt32 bounds) => forward ? End(bounds, horizontal) : Start(bounds, horizontal);
            int Gap(RectInt32 bounds) => forward
                ? Start(pair.Value, horizontal) - End(bounds, horizontal)
                : Start(bounds, horizontal) - End(pair.Value, horizontal);
            int position = Position(target);
            // A shared support can keep a member in place when another support
            // shrinks. Side-by-side members have independent vertical supports.
            if (supports.Any(p => Boundary(targets[p.Key]) != Boundary(p.Value)))
            {
                var positions = supports.Select(p => Boundary(targets[p.Key]) + (forward ? 1 : -1) * Math.Max(0, Gap(p.Value)));
                int supported = forward ? positions.Max() : positions.Min();
                if (Position(target) == Position(pair.Value)) position = supported;
                else position = forward ? Math.Max(position, supported) : Math.Min(position, supported);
            }
            foreach (var predecessor in predecessors)
            {
                int clearance = Math.Clamp(Gap(predecessor.Value), 0, spacing);
                int limit = Boundary(targets[predecessor.Key]) + (forward ? clearance : -clearance);
                position = forward ? Math.Max(position, limit) : Math.Min(position, limit);
            }
            targets[pair.Key] = WithStart(target, forward ? position : position - Length(target, horizontal), horizontal);
        }
    }

    private static bool Touches(RectInt32 a, RectInt32 b, bool horizontal, int spacing, int tolerance)
    {
        int gap = Math.Max(Start(a, horizontal), Start(b, horizontal)) - Math.Min(End(a, horizontal), End(b, horizontal));
        return gap >= -tolerance && gap <= spacing + tolerance && Overlaps(a, b, !horizontal);
    }

    private static bool Overlaps(RectInt32 a, RectInt32 b, bool horizontal) =>
        Start(a, horizontal) < End(b, horizontal) && Start(b, horizontal) < End(a, horizontal);
    private static int Start(RectInt32 bounds, bool horizontal) => horizontal ? bounds.X : bounds.Y;
    private static int Length(RectInt32 bounds, bool horizontal) => horizontal ? bounds.Width : bounds.Height;
    private static int End(RectInt32 bounds, bool horizontal) => Start(bounds, horizontal) + Length(bounds, horizontal);
    private static RectInt32 WithStart(RectInt32 bounds, int position, bool horizontal) => horizontal
        ? new(position, bounds.Y, bounds.Width, bounds.Height)
        : new(bounds.X, position, bounds.Width, bounds.Height);
}
