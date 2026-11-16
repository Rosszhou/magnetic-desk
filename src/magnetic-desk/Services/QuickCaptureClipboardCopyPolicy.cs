using MagneticDesk.Models;
using MagneticDesk.ViewModels;

namespace MagneticDesk.Services;

internal static class QuickCaptureClipboardCopyPolicy
{
    public static bool ShouldCopyBitmap(QuickCaptureItemViewModel item) =>
        item.IsRecent && item.Type == QuickCaptureItemType.Image;
}
