using MagneticDesk.Models;

namespace MagneticDesk.Services;

internal static class QuickCaptureChannelPolicy
{
    internal static QuickCaptureViewMode NormalizeView(WidgetKind kind, QuickCaptureViewMode view) =>
        kind == WidgetKind.Clipboard
            ? QuickCaptureViewMode.Recent
            : view == QuickCaptureViewMode.Recent ? QuickCaptureViewMode.Records : view;

    internal static IEnumerable<QuickCaptureItem> SearchItems(
        WidgetKind kind,
        IEnumerable<QuickCaptureItem> notes,
        IEnumerable<QuickCaptureItem> clipboard) =>
        kind == WidgetKind.Clipboard ? clipboard : notes;
}
