using MagneticDesk.Models;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal const string MagneticLayoutLockMetadataKey = "MagneticLayoutLock";

    internal bool IsMagneticLayoutLocked(WidgetConfig config)
    {
        string? id = GetMagneticGroupId(config);
        return id is not null && _settingsService.Settings.WidgetLayout.Widgets.Any(w =>
            !IsDeleted(w.Id) && GetMagneticGroupId(w) == id &&
            w.Metadata.GetValueOrDefault(MagneticLayoutLockMetadataKey) == id);
    }

    internal bool SetMagneticLayoutLocked(WidgetConfig config, bool locked)
    {
        string? id = GetMagneticGroupId(config);
        if (id is null || _layoutOperation is not null ||
            GetLoadedDesktopWindows().Any(w => GetMagneticGroupId(w.Config) == id &&
                (w.IsTitleBarDragTransitionActive || w.IsBoundsTransitionActive))) return false;
        var members = _settingsService.Settings.WidgetLayout.Widgets
            .Where(w => !IsDeleted(w.Id) && GetMagneticGroupId(w) == id).ToArray();
        foreach (var member in members)
        {
            if (locked) member.Metadata[MagneticLayoutLockMetadataKey] = id;
            else member.Metadata.Remove(MagneticLayoutLockMetadataKey);
        }
        RefreshMagneticGroupChrome();
        _settingsService.UpdateWidgetsBatch(members, notifySubscribers: false);
        _settingsService.SaveDebounced(notifySubscribers: false);
        return true;
    }
}
