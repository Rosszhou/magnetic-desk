using Windows.Graphics;

namespace MagneticDesk.Services;

/// <summary>A spatial partition captured before resizing. Leaf sizes can change without changing attachment order.</summary>
internal sealed class WidgetMagneticLayout
{
    private readonly string? _id;
    private readonly WidgetMagneticLayout? _first;
    private readonly WidgetMagneticLayout? _second;
    private readonly bool _horizontal;
    private readonly int _gap;
    private readonly double _firstAlignment;
    private readonly double _secondAlignment;

    private WidgetMagneticLayout(string id) => _id = id;

    private WidgetMagneticLayout(WidgetMagneticLayout first, WidgetMagneticLayout second,
        bool horizontal, int gap, double firstAlignment, double secondAlignment)
    {
        _first = first;
        _second = second;
        _horizontal = horizontal;
        _gap = gap;
        _firstAlignment = firstAlignment;
        _secondAlignment = secondAlignment;
    }

    internal static WidgetMagneticLayout Capture(IReadOnlyDictionary<string, RectInt32> bounds, int tolerance = 12,
        string? preferredAnchor = null, string? resizingId = null)
    {
        if (bounds.Count == 0) throw new ArgumentException("A magnetic layout requires at least one member.", nameof(bounds));
        if (bounds.Count == 1) return new WidgetMagneticLayout(bounds.First().Key);
        // A horizontal cut keeps rows and spanning headers together. If a panel
        // spans that cut, a vertical cut preserves its adjoining column instead.
        foreach (bool isolateSource in resizingId is null ? new[] { false } : new[] { true, false })
        {
            foreach (bool horizontal in new[] { true, false })
            {
                var ordered = bounds.OrderBy(p => horizontal ? p.Value.Y : p.Value.X).ThenBy(p => p.Key, StringComparer.Ordinal).ToArray();
                for (int i = 1; i < ordered.Length; i++)
                {
                    bool separatesSource = (i == 1 && ordered[0].Key == resizingId) ||
                        (i == ordered.Length - 1 && ordered[i].Key == resizingId);
                    if (isolateSource && !separatesSource) continue;
                    int end = ordered.Take(i).Max(p => horizontal ? p.Value.Y + p.Value.Height : p.Value.X + p.Value.Width);
                    int start = horizontal ? ordered[i].Value.Y : ordered[i].Value.X;
                    int previousStart = ordered.Take(i).Max(p => horizontal ? p.Value.Y : p.Value.X);
                    // Adjacent rows can overlap by a few physical pixels after DPI
                    // rounding or a resize. Preserve the row before trying columns.
                    if (start > previousStart && start >= end - Math.Max(0, tolerance))
                        return Split(ordered.Take(i).ToDictionary(), ordered.Skip(i).ToDictionary(), horizontal, tolerance, preferredAnchor, resizingId);
                }
            }
        }
        // Older saves may contain overlapping windows. A deterministic spatial
        // partition makes every member reachable while retaining its axis order.
        RectInt32 union = WidgetCoordinatedMoveCalculator.GetUnion(bounds.Values);
        bool fallbackHorizontal = union.Height > union.Width;
        var fallback = bounds.OrderBy(p => fallbackHorizontal ? p.Value.Y : p.Value.X).ThenBy(p => p.Key, StringComparer.Ordinal).ToArray();
        int middle = fallback.Length / 2;
        return Split(fallback.Take(middle).ToDictionary(), fallback.Skip(middle).ToDictionary(), fallbackHorizontal, tolerance, preferredAnchor, resizingId);
    }

    private static WidgetMagneticLayout Split(Dictionary<string, RectInt32> first, Dictionary<string, RectInt32> second,
        bool horizontal, int tolerance, string? preferredAnchor, string? resizingId)
    {
        RectInt32 a = WidgetCoordinatedMoveCalculator.GetUnion(first.Values);
        RectInt32 b = WidgetCoordinatedMoveCalculator.GetUnion(second.Values);
        return CreateBranch(Capture(first, tolerance, preferredAnchor, resizingId),
            Capture(second, tolerance, preferredAnchor, resizingId), a, b, horizontal, tolerance, preferredAnchor);
    }

    private static WidgetMagneticLayout CreateBranch(WidgetMagneticLayout first, WidgetMagneticLayout second,
        RectInt32 a, RectInt32 b, bool horizontal, int tolerance, string? preferredAnchor)
    {
        RectInt32 union = WidgetCoordinatedMoveCalculator.GetUnion(new[] { a, b });
        int firstStart = horizontal ? a.X : a.Y;
        int secondStart = horizontal ? b.X : b.Y;
        int firstEnd = horizontal ? a.X + a.Width : a.Y + a.Height;
        int secondEnd = horizontal ? b.X + b.Width : b.Y + b.Height;
        bool preferTrailing = horizontal
            ? preferredAnchor?.Contains("Right", StringComparison.Ordinal) == true
            : preferredAnchor?.Contains("Bottom", StringComparison.Ordinal) == true;
        bool trailingAligned = Math.Abs(firstEnd - secondEnd) <= tolerance &&
            (Math.Abs(firstStart - secondStart) > tolerance || preferTrailing);
        double Alignment(RectInt32 child)
        {
            if (trailingAligned) return 1;
            int space = horizontal ? union.Width - child.Width : union.Height - child.Height;
            int offset = horizontal ? child.X - union.X : child.Y - union.Y;
            if (space <= tolerance || offset <= tolerance) return 0;
            if (space - offset <= tolerance) return 1;
            return (double)offset / space;
        }
        int gap = horizontal ? b.Y - a.Y - a.Height : b.X - a.X - a.Width;
        return new WidgetMagneticLayout(first, second, horizontal,
            Math.Max(0, gap), Alignment(a), Alignment(b));
    }

    internal IReadOnlyDictionary<string, RectInt32> Arrange(IReadOnlyDictionary<string, SizeInt32> sizes,
        int x, int y, int? gapOverride = null)
    {
        var measured = new Dictionary<WidgetMagneticLayout, SizeInt32>();
        Measure(sizes, gapOverride, measured);
        var result = new Dictionary<string, RectInt32>(StringComparer.Ordinal);
        Place(x, y, gapOverride, measured, result);
        return result;
    }

    internal WidgetMagneticLayout WithSpacing(IReadOnlyDictionary<string, RectInt32> bounds) =>
        CopySpacing(bounds).Layout;

    internal WidgetMagneticLayout WithAlignment(IReadOnlyDictionary<string, RectInt32> reference,
        string? preferredAnchor = null) => CopyAlignment(reference, preferredAnchor).Layout;

    private (WidgetMagneticLayout Layout, RectInt32 Bounds) CopyAlignment(
        IReadOnlyDictionary<string, RectInt32> reference, string? preferredAnchor)
    {
        if (_id is not null) return (this, reference[_id]);
        var a = _first!.CopyAlignment(reference, preferredAnchor);
        var b = _second!.CopyAlignment(reference, preferredAnchor);
        return (CreateBranch(a.Layout, b.Layout, a.Bounds, b.Bounds, _horizontal, 12, preferredAnchor),
            WidgetCoordinatedMoveCalculator.GetUnion(new[] { a.Bounds, b.Bounds }));
    }

    private (WidgetMagneticLayout Layout, RectInt32 Bounds) CopySpacing(IReadOnlyDictionary<string, RectInt32> bounds)
    {
        if (_id is not null) return (this, bounds[_id]);
        var a = _first!.CopySpacing(bounds);
        var b = _second!.CopySpacing(bounds);
        int gap = _horizontal ? b.Bounds.Y - a.Bounds.Y - a.Bounds.Height : b.Bounds.X - a.Bounds.X - a.Bounds.Width;
        return (new WidgetMagneticLayout(a.Layout, b.Layout, _horizontal, Math.Max(0, gap), _firstAlignment, _secondAlignment),
            WidgetCoordinatedMoveCalculator.GetUnion(new[] { a.Bounds, b.Bounds }));
    }

    private SizeInt32 Measure(IReadOnlyDictionary<string, SizeInt32> sizes, int? gapOverride,
        Dictionary<WidgetMagneticLayout, SizeInt32> measured)
    {
        SizeInt32 size;
        if (_id is not null)
        {
            var leaf = sizes[_id];
            size = new SizeInt32(Math.Max(1, leaf.Width), Math.Max(1, leaf.Height));
        }
        else
        {
            var a = _first!.Measure(sizes, gapOverride, measured);
            var b = _second!.Measure(sizes, gapOverride, measured);
            int gap = Math.Max(0, gapOverride ?? _gap);
            size = _horizontal
                ? new SizeInt32(Math.Max(a.Width, b.Width), a.Height + b.Height + gap)
                : new SizeInt32(a.Width + b.Width + gap, Math.Max(a.Height, b.Height));
        }
        measured.Add(this, size);
        return size;
    }

    private void Place(int x, int y, int? gapOverride, Dictionary<WidgetMagneticLayout, SizeInt32> measured,
        Dictionary<string, RectInt32> result)
    {
        var size = measured[this];
        if (_id is not null)
        {
            result.Add(_id, new RectInt32(x, y, size.Width, size.Height));
            return;
        }
        var a = measured[_first!];
        var b = measured[_second!];
        int gap = Math.Max(0, gapOverride ?? _gap);
        if (_horizontal)
        {
            _first!.Place(x + (int)Math.Round((size.Width - a.Width) * _firstAlignment), y, gapOverride, measured, result);
            _second!.Place(x + (int)Math.Round((size.Width - b.Width) * _secondAlignment), y + a.Height + gap, gapOverride, measured, result);
        }
        else
        {
            _first!.Place(x, y + (int)Math.Round((size.Height - a.Height) * _firstAlignment), gapOverride, measured, result);
            _second!.Place(x + a.Width + gap, y + (int)Math.Round((size.Height - b.Height) * _secondAlignment), gapOverride, measured, result);
        }
    }
}
