using Windows.Graphics;

namespace MagneticDesk.Services;

internal static class WidgetMagneticResizePolicy
{
    internal static bool RejectHeightGrowth(string sourceId, RectInt32 proposed,
        IReadOnlyDictionary<string, RectInt32> original)
    {
        RectInt32 source = original[sourceId];
        if (proposed.Height <= source.Height) return false;
        return original.Any(pair => pair.Key != sourceId &&
            pair.Value.Y >= source.Y + source.Height - 12 &&
            pair.Value.Width > proposed.Width &&
            pair.Value.X < proposed.X + proposed.Width &&
            proposed.X < pair.Value.X + pair.Value.Width);
    }
}
