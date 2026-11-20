using MagneticDesk.Models;
using Windows.Graphics;

namespace MagneticDesk.Services;

internal static class WidgetMagneticResizeLayout
{
    internal static Dictionary<string, RectInt32> Arrange(
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

    internal static IReadOnlyDictionary<string, RectInt32> ArrangeInteractive(
        IReadOnlyDictionary<string, RectInt32> original, string sourceId, RectInt32 proposed,
        IReadOnlyDictionary<string, RectInt32> attachment, string? anchor, int spacing, int tolerance,
        out bool reattached)
    {
        var targets = Arrange(original, sourceId, proposed, attachment, anchor, spacing, tolerance);
        reattached = !proposed.Equals(original[sourceId]) &&
            ReconnectDetachedMembers(original, targets, sourceId, attachment, spacing, tolerance);
        return targets;
    }

    private static bool ReconnectDetachedMembers(IReadOnlyDictionary<string, RectInt32> original,
        Dictionary<string, RectInt32> targets, string sourceId,
        IReadOnlyDictionary<string, RectInt32> attachment, int spacing, int tolerance)
    {
        bool reattached = false;
        var ids = targets.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        while (true)
        {
            var connected = FindConnectedMembers(sourceId);
            if (connected.Count == targets.Count) return reattached;

            HashSet<string>? closestBranch = null;
            PointInt32 closestDelta = default;
            (int AlignmentShift, double Distance) best = (int.MaxValue, double.MaxValue);
            var examined = new HashSet<string>(connected, StringComparer.Ordinal);
            foreach (string seed in ids)
            {
                if (examined.Contains(seed)) continue;
                // A detached column can still be joined internally. Move those members as a unit.
                var branch = FindConnectedMembers(seed);
                examined.UnionWith(branch);
                var moving = ids.Where(branch.Contains).Select(id => targets[id]).ToArray();
                var obstacles = ids.Where(id => !branch.Contains(id)).Select(id => targets[id]).ToArray();
                foreach (string nearId in ids.Where(connected.Contains))
                foreach (string farId in ids.Where(branch.Contains))
                foreach (bool horizontal in new[] { true, false })
                {
                    var near = targets[nearId];
                    var far = targets[farId];
                    var reference = Touches(original[nearId], original[farId], horizontal, spacing, tolerance)
                        ? original : attachment;
                    if (!Touches(reference[nearId], reference[farId], horizontal, spacing, tolerance)) continue;
                    bool after = Start(reference[nearId], horizontal) < Start(reference[farId], horizontal);
                    MagneticDockEdge edge = horizontal
                        ? (after ? MagneticDockEdge.Left : MagneticDockEdge.Right)
                        : (after ? MagneticDockEdge.Top : MagneticDockEdge.Bottom);
                    MagneticDockEdge opposite = horizontal
                        ? (after ? MagneticDockEdge.Right : MagneticDockEdge.Left)
                        : (after ? MagneticDockEdge.Bottom : MagneticDockEdge.Top);
                    var contact = new MagneticDockContact(edge, opposite, 0, 0, far);
                    int minimumOverlap = Math.Min(tolerance, Math.Min(Length(near, !horizontal), Length(far, !horizontal)));
                    var docked = WidgetMagneticDockPlacement.Resolve(contact, far, near, obstacles, moving,
                        spacing, minimumOverlap);
                    if (docked is not { } dock) continue;

                    var delta = new PointInt32(dock.DockedBounds.X - far.X, dock.DockedBounds.Y - far.Y);
                    int requiresAlignment = Overlaps(near, far, !horizontal) ? 0 : 1;
                    var score = (requiresAlignment, (double)delta.X * delta.X + (double)delta.Y * delta.Y);
                    if (score.CompareTo(best) >= 0 || !CanSlide(moving, obstacles, delta)) continue;
                    best = score;
                    closestBranch = branch;
                    closestDelta = delta;
                }
            }
            if (closestBranch is null) return reattached;
            foreach (string id in closestBranch)
                targets[id] = Offset(targets[id], closestDelta.X, closestDelta.Y);
            reattached = true;
        }

        bool HasRetainedContact(string first, string second, bool horizontal) =>
            Touches(targets[first], targets[second], horizontal, spacing, tolerance) &&
            (Touches(original[first], original[second], horizontal, spacing, tolerance) ||
             Touches(attachment[first], attachment[second], horizontal, spacing, tolerance));

        HashSet<string> FindConnectedMembers(string seed)
        {
            var connected = new HashSet<string>(StringComparer.Ordinal) { seed };
            var pending = new Queue<string>();
            pending.Enqueue(seed);
            while (pending.TryDequeue(out string? id))
            {
                foreach (string other in ids)
                {
                    if (connected.Contains(other)) continue;
                    if (!HasRetainedContact(id, other, true) && !HasRetainedContact(id, other, false)) continue;
                    connected.Add(other);
                    pending.Enqueue(other);
                }
            }
            return connected;
        }
    }

    private static bool CanSlide(RectInt32[] moving, RectInt32[] obstacles, PointInt32 delta)
    {
        bool HasClearPath(bool horizontalFirst)
        {
            foreach (var start in moving)
            {
                RectInt32 turn = horizontalFirst ? Offset(start, delta.X, 0) : Offset(start, 0, delta.Y);
                RectInt32 end = Offset(start, delta.X, delta.Y);
                if (!Clear(start, turn) || !Clear(turn, end)) return false;
            }
            return true;
        }

        bool Clear(RectInt32 start, RectInt32 end)
        {
            RectInt32 swept = new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                start.Width + Math.Abs(end.X - start.X), start.Height + Math.Abs(end.Y - start.Y));
            return !obstacles.Any(other => Overlaps(swept, other, true) && Overlaps(swept, other, false));
        }

        return HasClearPath(horizontalFirst: true) || HasClearPath(horizontalFirst: false);
    }

    private static RectInt32 Offset(RectInt32 bounds, int dx, int dy) =>
        new(bounds.X + dx, bounds.Y + dy, bounds.Width, bounds.Height);

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
