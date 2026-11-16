namespace MagneticDesk.Services;

internal enum WidgetCapsuleStyle { Ring, Fill, Segments, ClassicBlack, ClassicWhite }

internal static class WidgetCapsuleAppearance
{
    internal static WidgetCapsuleStyle Normalize(string? value) =>
        Enum.TryParse<WidgetCapsuleStyle>(value, true, out var style) && Enum.IsDefined(style) ? style : WidgetCapsuleStyle.Ring;

    internal static bool IsClassic(WidgetCapsuleStyle style) => style is WidgetCapsuleStyle.ClassicBlack or WidgetCapsuleStyle.ClassicWhite;

    internal static int RemainingSeconds(double fraction) => Math.Clamp((int)Math.Ceiling(Math.Clamp(fraction, 0, 1) * 5), 1, 5);

    internal static double CornerRadius(WidgetCapsuleStyle style, bool compact) =>
        compact ? style == WidgetCapsuleStyle.Segments ? 12 : 26 : style == WidgetCapsuleStyle.Segments ? 16 : 22;
}
