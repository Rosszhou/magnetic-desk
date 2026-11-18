using Windows.Graphics;

namespace MagneticDesk.Services;

internal sealed record MagneticCompactMember(string Id, RectInt32 Expanded, SizeInt32 CompactSize);

internal static class WidgetMagneticCollapseLayout
{
    internal static IReadOnlyDictionary<string, RectInt32> ArrangeTransition(
        IReadOnlyDictionary<string, RectInt32> expanded, IReadOnlyDictionary<string, SizeInt32> sizes,
        string? anchor, int spacing, int tolerance)
    {
        IReadOnlyDictionary<string, RectInt32> targets = expanded.ToDictionary();
        foreach (var pair in expanded.OrderBy(pair => pair.Value.Y).ThenBy(pair => pair.Value.X))
        {
            SizeInt32 size = sizes[pair.Key];
            if (size.Width == pair.Value.Width && size.Height == pair.Value.Height) continue;
            RectInt32 current = targets[pair.Key];
            int x = anchor?.Contains("Right", StringComparison.Ordinal) == true ? current.X + current.Width - size.Width : current.X;
            int y = anchor?.Contains("Bottom", StringComparison.Ordinal) == true ? current.Y + current.Height - size.Height : current.Y;
            targets = WidgetMagneticResizeLayout.Arrange(targets, pair.Key, new(x, y, size.Width, size.Height), expanded, anchor, spacing, tolerance);
        }
        return targets;
    }

    internal static IReadOnlyDictionary<string, RectInt32> RestoreExpanded(
        IReadOnlyDictionary<string, RectInt32> expanded, PointInt32 origin,
        IReadOnlyDictionary<string, RectInt32>? attachment = null, string? anchor = null,
        int spacing = 8, int tolerance = 12)
    {
        if (expanded.Count == 0) return new Dictionary<string, RectInt32>();
        IReadOnlyDictionary<string, RectInt32> restored = expanded;
        if (attachment is not null)
        {
            restored = attachment.ToDictionary();
            foreach (var pair in expanded.OrderBy(p => attachment[p.Key].Y)
                .ThenBy(p => attachment[p.Key].X).ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value.Equals(attachment[pair.Key]) || pair.Value.Equals(restored[pair.Key])) continue;
                restored = WidgetMagneticResizeLayout.Arrange(restored, pair.Key, pair.Value,
                    attachment, anchor, spacing, tolerance);
            }
        }
        RectInt32 savedOrigin = WidgetCoordinatedMoveCalculator.GetUnion(restored.Values);
        return restored.ToDictionary(p => p.Key, p => new RectInt32(
            p.Value.X + origin.X - savedOrigin.X, p.Value.Y + origin.Y - savedOrigin.Y,
            p.Value.Width, p.Value.Height));
    }

    internal static IReadOnlyDictionary<string, RectInt32> KeepVisible(
        IReadOnlyDictionary<string, RectInt32> targets, RectInt32 workArea)
    {
        if (targets.Count == 0 || workArea.Width <= 0 || workArea.Height <= 0) return targets;
        RectInt32 union = WidgetCoordinatedMoveCalculator.GetUnion(targets.Values);
        int x = Math.Clamp(union.X, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - union.Width));
        int y = Math.Clamp(union.Y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - union.Height));
        return targets.ToDictionary(kv => kv.Key, kv => new RectInt32(
            kv.Value.X + x - union.X, kv.Value.Y + y - union.Y, kv.Value.Width, kv.Value.Height));
    }

    internal static IReadOnlyDictionary<string, RectInt32> RestoreCompact(
        IReadOnlyDictionary<string, RectInt32> expanded, IReadOnlyDictionary<string, RectInt32> homes,
        IReadOnlyDictionary<string, SizeInt32> sizes,
        IReadOnlyDictionary<string, RectInt32>? attachment = null, WidgetMagneticLayout? layout = null)
    {
        var currentOrigin = WidgetCoordinatedMoveCalculator.GetUnion(expanded.Values);
        var targets = ArrangeVisible(attachment ?? expanded, sizes, true,
            origin: new PointInt32(currentOrigin.X, currentOrigin.Y), layout: layout);
        if (!expanded.Keys.All(homes.ContainsKey)) return targets;
        var origin = WidgetCoordinatedMoveCalculator.GetUnion(homes.Values);
        var arranged = WidgetCoordinatedMoveCalculator.GetUnion(targets.Values);
        return targets.ToDictionary(p => p.Key, p => new RectInt32(
            p.Value.X + origin.X - arranged.X, p.Value.Y + origin.Y - arranged.Y,
            p.Value.Width, p.Value.Height));
    }

    internal static IReadOnlyDictionary<string, RectInt32> ArrangeVisible(
        IReadOnlyDictionary<string, RectInt32> expanded,
        IReadOnlyDictionary<string, SizeInt32> sizes, bool hasCompactMembers,
        string? anchorId = null, RectInt32? anchorBounds = null,
        PointInt32? origin = null, int? expandedGap = null, WidgetMagneticLayout? layout = null)
    {
        if (expanded.Count == 0) return new Dictionary<string, RectInt32>();
        var union = WidgetCoordinatedMoveCalculator.GetUnion(expanded.Values);
        var targets = (layout ?? WidgetMagneticLayout.Capture(expanded)).Arrange(sizes,
            origin?.X ?? union.X, origin?.Y ?? union.Y, hasCompactMembers ? 2 : expandedGap);
        if (anchorId is null || anchorBounds is not { } anchor) return targets;
        var placed = targets[anchorId];
        return targets.ToDictionary(p => p.Key, p => new RectInt32(
            p.Value.X + anchor.X - placed.X, p.Value.Y + anchor.Y - placed.Y,
            p.Value.Width, p.Value.Height));
    }

    /// <summary>Compact the captured spatial partition using each member's own capsule size.</summary>
    internal static IReadOnlyDictionary<string, RectInt32> Arrange(
        IReadOnlyList<MagneticCompactMember> members, int gap = 2, int alignmentTolerance = 12)
    {
        if (members.Count == 0) return new Dictionary<string, RectInt32>();
        var expanded = members.ToDictionary(m => m.Id, m => m.Expanded);
        var sizes = members.ToDictionary(m => m.Id, m => m.CompactSize);
        var union = WidgetCoordinatedMoveCalculator.GetUnion(expanded.Values);
        return WidgetMagneticLayout.Capture(expanded, alignmentTolerance).Arrange(sizes, union.X, union.Y, Math.Max(0, gap));
    }
}
