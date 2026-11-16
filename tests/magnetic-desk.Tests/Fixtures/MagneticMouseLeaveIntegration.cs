#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Reflection;
using System.Text.Json;
using MagneticDesk.Models;
using MagneticDesk.Views;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal async Task RunMagneticMouseLeaveIntegrationAsync()
    {
        string root = MagneticDeskDataPathService.Current.RootPath;
        string path = Path.Combine(root, "magnetic-mouse-leave-integration.json");
        var dragging = typeof(WidgetWindowBase).GetField("IsDragging", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var moved = typeof(WidgetWindowBase).GetField("HasMovedTitleBarDrag", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var beginInteraction = typeof(WidgetWindowBase).GetMethod("BeginCompactInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var endInteraction = typeof(WidgetWindowBase).GetMethod("EndCompactInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var expandClick = typeof(WidgetWindowBase).GetMethod("WidgetShellControl_CompactBodyExpandRequested", BindingFlags.Instance | BindingFlags.NonPublic)!;
        WidgetWindowBase? notes = null;
        try
        {
            notes = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().First(w => w.Config.WidgetKind == WidgetKind.QuickCapture);
            var files = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().First(w => w.Config.WidgetKind == WidgetKind.File);
            var todo = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().First(w => w.Config.WidgetKind == WidgetKind.Todo);
            var windows = new[] { notes, files, todo };
            using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "magnetic-collapse-integration.json")));
            string group = previous.RootElement.GetProperty("group").GetString()!;
            foreach (var window in windows) SetMagneticGroupId(window.Config, group);
            dragging.SetValue(notes, true);
            moved.SetValue(notes, false);
            Check(!notes.DetachMagneticMemberForControlDrag(true), "Ctrl click detached a group member");
            moved.SetValue(notes, true);
            Check(notes.DetachMagneticMemberForControlDrag(true), "Ctrl drag did not detach its member");
            Check(!notes.CanMagneticallyDockTo(files.Config.Id) && !notes.CanMagneticallyDockTo(todo.Config.Id),
                "The same Ctrl drag could immediately reattach to its former companions");
            Check(GetMagneticGroupId(notes.Config) is null && GetMagneticGroupId(files.Config) == GetMagneticGroupId(todo.Config),
                "Ctrl drag changed the remaining group");
            dragging.SetValue(notes, false);
            moved.SetValue(notes, false);
            dragging.SetValue(files, true);
            moved.SetValue(files, true);
            Check(files.DetachMagneticMemberForControlDrag(true), "Second Ctrl drag did not detach");
            dragging.SetValue(files, false);
            moved.SetValue(files, false);
            Check(windows.All(w => GetMagneticGroupId(w.Config) is null), "A singleton magnetic group was left behind");
            foreach (var window in windows) SetMagneticGroupId(window.Config, group);
            SetMagneticGroupCollapseBehavior(notes.Config, WidgetCollapseBehavior.Click);
            await Task.Delay(600);
            expandClick.Invoke(notes, [null, null]);
            await Task.Delay(700);
            Check(windows.All(w => !w.IsCompactArrangementActive && HasMouseLeaveAutoCollapse(w)), "Click expansion did not arm the group");
            beginInteraction.Invoke(files, null);
            await Task.Delay(5200);
            Check(windows.All(w => !w.IsCompactArrangementActive), "Activity on another member did not keep the group open");
            endInteraction.Invoke(files, null);
            await Task.Delay(4600);
            Check(windows.All(w => !w.IsCompactArrangementActive), "Group collapsed before five seconds outside");
            await Task.Delay(900);
            Check(windows.All(w => w.IsCompactArrangementActive), "Group did not collapse five seconds after activity ended");
            await Task.Delay(500);
            LeaveMagneticGroup(notes.Config);
            expandClick.Invoke(notes, [null, null]);
            await Task.Delay(700);
            Check(HasMouseLeaveAutoCollapse(notes) && !notes.IsCompactArrangementActive, "Independent click expansion did not arm");
            UpdateMouseLeaveAutoCollapse(TimeSpan.Zero);
            UpdateMouseLeaveAutoCollapse(TimeSpan.FromMilliseconds(4999));
            Check(!notes.IsCompactArrangementActive, "Independent widget collapsed early");
            UpdateMouseLeaveAutoCollapse(TimeSpan.FromSeconds(5));
            Check(notes.IsCompactArrangementActive, "Independent widget did not collapse at five seconds");
            await Task.Delay(500);
            foreach (var window in windows) SetMagneticGroupId(window.Config, group);
            SetMagneticGroupCollapseBehavior(notes.Config, WidgetCollapseBehavior.Click);
            await Task.Delay(500);
            await _settingsService.FlushPendingSaveAsync(false);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, controlClickPreserved = true,
                controlDragDetached = true, singletonCleaned = true, groupInteractionHeld = true,
                groupWaitedFiveSeconds = true, singleWaitedFiveSeconds = true }));
            await RunMagneticResizeIntegrationAsync();
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
            App.Log($"[MouseLeaveIntegration] Failed: {ex}");
        }
        finally
        {
            foreach (var window in GetLoadedDesktopWindows().OfType<WidgetWindowBase>())
            {
                dragging.SetValue(window, false);
                moved.SetValue(window, false);
            }
        }
    }
}
#endif
