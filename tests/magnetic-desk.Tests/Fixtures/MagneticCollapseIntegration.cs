#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Text.Json;
using System.Reflection;
using MagneticDesk.Controls.WidgetContents;
using MagneticDesk.Models;
using MagneticDesk.Platform;
using MagneticDesk.Views;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal async Task RunMagneticCollapseIntegrationAsync()
    {
        string resultPath = Path.Combine(MagneticDeskDataPathService.Current.RootPath, "magnetic-collapse-integration.json");
        try
        {
            _settingsService.Settings.QuickCaptureClipboardEnabled = false;
            _settingsService.Settings.WidgetCollapseBehavior = "Expanded";
            var notes = await CreateOrShowQuickCaptureWidgetAsync();
            Check(notes.Config.Name == _localizationService.T("QuickCapture.Name"), "Legacy default notes title was not updated");
            var clipboard = await CreateOrShowClipboardWidgetAsync();
            await Task.Delay(700);
            var files = (WidgetWindowBase)GetLoadedDesktopWindows().First(w => w.Config.WidgetKind == WidgetKind.File);
            var todo = (WidgetWindowBase)GetLoadedDesktopWindows().First(w => w.Config.WidgetKind == WidgetKind.Todo);
            var windows = new WidgetWindowBase[] { notes, files, todo };
            string group = Guid.NewGuid().ToString();
            int spacing = (int)Math.Round(_settingsService.Settings.WidgetSnapSpacing *
                Win32Helper.GetDpiScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(notes), notes.WindowContentRoot?.XamlRoot));
            // Keep the fixture above content minimum sizes at the current DPI.
            int width = Math.Max(600, windows.Max(w => w.CoordinatedMoveBounds.Width));
            int height = Math.Max(700, windows.Max(w => w.CoordinatedMoveBounds.Height));
            var expanded = new[]
            {
                new RectInt32(100, 100, width, height),
                new RectInt32(100 + width + spacing, 100, width, height),
                new RectInt32(100 + width + spacing, 100 + height + spacing, width, height)
            };
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].Config.MagneticCollapseLinked = false;
                windows[i].Config.IsCollapsed = false;
                WidgetCollapseBehaviorNames.SetOverride(windows[i].Config, WidgetCollapseBehavior.Expanded);
                windows[i].RefreshMagneticCollapseBehavior();
                SetMagneticGroupId(windows[i].Config, group);
                windows[i].RequestCompactState(false, false);
                windows[i].BeginCoordinatedMoveParticipation(false);
                windows[i].ApplyCoordinatedMoveBoundsFallback(expanded[i]);
                windows[i].CompleteCoordinatedMoveParticipation(true, false);
                expanded[i] = windows[i].CoordinatedMoveBounds;
            }
            // Native content minimums can settle after the first placement.
            // Position the right column using the accepted native dimensions.
            for (int i = 1; i < windows.Length; i++)
            {
                RectInt32 actual = windows[i].CoordinatedMoveBounds;
                int x = expanded[0].X + expanded[0].Width + spacing;
                int y = i == 1 ? expanded[0].Y : expanded[1].Y + expanded[1].Height + spacing;
                windows[i].BeginCoordinatedMoveParticipation(false);
                windows[i].ApplyCoordinatedMoveBoundsFallback(new RectInt32(x, y, actual.Width, actual.Height));
                windows[i].CompleteCoordinatedMoveParticipation(true, false);
                expanded[i] = windows[i].CoordinatedMoveBounds;
            }
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].Config.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(expanded[i]);
                windows[i].Config.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(expanded[i]);
                windows[i].Config.MagneticCompactPlacement = null;
            }
            SetMagneticGroupCollapseBehavior(notes.Config, WidgetCollapseBehavior.Click);
            await Task.Delay(900);
            var compact = windows.Select(w => w.CoordinatedMoveBounds).ToArray();
            Check(windows.All(w => w.IsCompactArrangementActive), "Group did not collapse together");
            Check(compact[0].Y == compact[1].Y && compact[1].X == compact[2].X,
                "L-shaped topology was not preserved");
            Check(compact[0].X + compact[0].Width + 2 == compact[1].X &&
                compact[1].Y + compact[1].Height + 2 == compact[2].Y, "Capsules were not compacted");
            TrySetMagneticGroupCollapsed(notes, false, true, false, true);
            await Task.Delay(350);
            for (int i = 0; i < windows.Length; i++)
                Check(Same(windows[i].CoordinatedMoveBounds, expanded[i]), $"Expanded member {i} did not restore actual={windows[i].CoordinatedMoveBounds.X},{windows[i].CoordinatedMoveBounds.Y},{windows[i].CoordinatedMoveBounds.Width},{windows[i].CoordinatedMoveBounds.Height} expected={expanded[i].X},{expanded[i].Y},{expanded[i].Width},{expanded[i].Height}");
            TrySetMagneticGroupCollapsed(notes, true, true, false, true);
            await Task.Delay(250);
            Check(TryBeginCoordinatedMove(notes.WindowHandle, false), "Collapsed group drag did not begin");
            UpdateCoordinatedMove(notes.WindowHandle, 20, 15);
            CompleteCoordinatedMove(notes.WindowHandle, true);
            Check(windows.All(w => w.Config.X == w.Config.MagneticExpandedPlacement!.X &&
                w.Config.Y == w.Config.MagneticExpandedPlacement.Y), "Compact move did not update persisted expanded placement");
            TrySetMagneticGroupCollapsed(notes, false, true, false, true);
            await Task.Delay(250);
            for (int i = 0; i < windows.Length; i++)
            {
                var moved = windows[i].CoordinatedMoveBounds;
                Check(moved.X == expanded[i].X + 20 && moved.Y == expanded[i].Y + 15,
                    $"Expanded member {i} did not follow collapsed group drag");
            }
            var beforeSingleMove = windows.Select(w => w.CoordinatedMoveBounds).ToArray();
            TrySetMagneticGroupCollapsed(notes, true, true, false, true);
            Check(!TryBeginCoordinatedMove(notes.WindowHandle, true), "Ctrl started a whole-group move");
            var noteCapsule = notes.CoordinatedMoveBounds;
            notes.BeginCoordinatedMoveParticipation(false);
            notes.ApplyCoordinatedMoveBoundsFallback(new RectInt32(noteCapsule.X + 10, noteCapsule.Y, noteCapsule.Width, noteCapsule.Height));
            notes.CompleteCoordinatedMoveParticipation(true, false);
            TrySetMagneticGroupCollapsed(notes, false, true, false, true);
            await Task.Delay(250);
            Check(notes.CoordinatedMoveBounds.X == beforeSingleMove[0].X + 10, "Single capsule move was lost on expansion");
            Check(files.CoordinatedMoveBounds.X == notes.CoordinatedMoveBounds.X + notes.CoordinatedMoveBounds.Width + spacing &&
                todo.CoordinatedMoveBounds.X == files.CoordinatedMoveBounds.X,
                "A moved capsule lost the group's spatial attachment");
            beforeSingleMove = windows.Select(w => w.CoordinatedMoveBounds).ToArray();
            DetachMagneticCollapse(todo.Config);
            TrySetMagneticGroupCollapsed(notes, true, true, false, true);
            await Task.Delay(250);
            Check(!todo.IsCompactArrangementActive && notes.IsCompactArrangementActive && files.IsCompactArrangementActive,
                "Current-widget scope did not detach its collapse behavior");

            var noteVm = ((QuickCaptureWidgetContentAdapter)notes.CurrentContent!).ViewModel;
            var clipVm = ((QuickCaptureWidgetContentAdapter)clipboard.CurrentContent!).ViewModel;
            string token = Guid.NewGuid().ToString("N");
            var note = await _quickCaptureService.AddItemAsync("便签专属 " + token);
            var copied = await _quickCaptureService.AddRecentClipboardItemAsync("剪贴板专属 " + token, 30);
            await noteVm.RefreshItemsAsync();
            await clipVm.RefreshItemsAsync();
            Check(noteVm.IsRecordsView && clipVm.IsRecentView, "Widget channels selected the wrong view");
            Check(noteVm.RecentTabVisibility == Visibility.Collapsed && clipVm.RecordsTabVisibility == Visibility.Collapsed,
                "Notes and clipboard tabs were mixed");
            Check(noteVm.Items.Any(i => i.Id == note.Id) && !noteVm.Items.Any(i => i.Id == copied!.Id), "Notes projected clipboard history");
            Check(clipVm.Items.Any(i => i.Id == copied!.Id) && !clipVm.Items.Any(i => i.Id == note.Id), "Clipboard projected notes");
            noteVm.SearchText = token;
            clipVm.SearchText = token;
            await noteVm.RefreshItemsAsync();
            await clipVm.RefreshItemsAsync();
            Check(noteVm.Items.Count == 1 && clipVm.Items.Count == 1, "Search crossed widget channels");
            noteVm.SearchText = "";
            clipVm.SearchText = "";
            Check(clipVm.InputAreaVisibility == Visibility.Collapsed, "Clipboard exposed the notes composer");
            await SetClipboardWidgetEnabledAsync(clipboard.Config.Id, false);
            Check(!clipboard.Visible && clipboard.Config.IsDisabled, "Clipboard disable did not hide its instance");
            await CreateOrShowClipboardWidgetAsync();
            Check(clipboard.Visible && !clipboard.Config.IsDisabled, "Clipboard recreation did not reveal its existing instance");
            Check(todo.CoordinatedMoveBounds.Y == files.CoordinatedMoveBounds.Y + files.CoordinatedMoveBounds.Height + 2,
                "Settings refresh detached the independently expanded member");
            SetMagneticGroupCollapseBehavior(notes.Config, WidgetCollapseBehavior.Smart);
            await Task.Delay(450);
            typeof(WidgetWindowBase).GetField("_isSmartPinnedOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(notes, true);
            TrySetMagneticGroupCollapsed(notes, false, false, false, true);
            await Task.Delay(350);
            Check(windows.All(w => !w.IsCompactArrangementActive && w.MagneticSmartPinnedOpen), "Smart pinned expansion did not keep the group open");
            Check(Same(todo.CoordinatedMoveBounds, beforeSingleMove[2]), "Changing group mode lost the expanded spatial layout");
            TrySetMagneticGroupCollapsed(notes, true, false, false, true);
            Check(windows.All(w => w.IsCompactArrangementActive && !w.MagneticSmartPinnedOpen), "Manual group collapse did not clear Smart pinning");
            SetMagneticGroupCollapseBehavior(notes.Config, WidgetCollapseBehavior.Click);
            await Task.Delay(700);
            bool restartCollapsed = Environment.GetEnvironmentVariable("MAGNETIC_DESK_TEST_EXPANDED_RESTART") != "1";
            if (!restartCollapsed)
            {
                SetMagneticGroupCollapseBehavior(notes.Config, WidgetCollapseBehavior.Expanded);
                await Task.Delay(700);
            }
            await _settingsService.FlushPendingSaveAsync(false);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(MagneticDeskDataPathService.Current.DataDirectory, "widget-layout.json")));
            var widgets = saved.RootElement.GetProperty("layout").GetProperty("widgets").EnumerateArray().ToArray();
            Check(widgets.Count(w => w.TryGetProperty("magneticCollapseLinked", out var flag) && flag.GetBoolean()) == 3,
                "Whole-group collapse scope was not saved");
            Check(widgets.Any(w => w.GetProperty("widgetKind").GetString() == "Clipboard"), "Clipboard kind was not saved");
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
            {
                passed = true, spatialCapsules = true, restoredExpandedLayout = true,
                compactGroupDrag = true, controlSingleCapsuleDrag = true, currentWidgetScope = true, separatedTabsAndSearch = true,
                independentClipboardLifecycle = true, persisted = true, group,
                smartGroupPinning = true,
                restartCollapsed,
                finalBounds = windows.Select(w => new { id = w.Config.Id, w.CoordinatedMoveBounds.X, w.CoordinatedMoveBounds.Y,
                    w.CoordinatedMoveBounds.Width, w.CoordinatedMoveBounds.Height }),
                noteId = note.Id, clipboardItemId = copied!.Id,
                compactBounds = compact.Select(b => new { b.X, b.Y, b.Width, b.Height })
            }));
            App.Log("[MagneticCollapseIntegration] Passed spatial layout, restore, scope, drag and independent notes/clipboard");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
            App.Log($"[MagneticCollapseIntegration] Failed: {ex}");
        }
    }

    internal async Task VerifyMagneticCollapseRestartAsync()
    {
        string root = MagneticDeskDataPathService.Current.RootPath;
        string previousPath = Path.Combine(root, "magnetic-collapse-integration.json");
        if (!File.Exists(previousPath)) return;
        using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(previousPath));
        if (!previous.RootElement.GetProperty("passed").GetBoolean()) return;
        string resultPath = Path.Combine(root, "magnetic-collapse-restart.json");
        try
        {
            var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
                .Where(w => w.Config.MagneticCollapseLinked && w.Config.MagneticExpandedPlacement is not null).ToArray();
            Check(windows.Length == 3, "Restart lost a linked surface");
            bool collapsed = !previous.RootElement.TryGetProperty("restartCollapsed", out var flag) || flag.GetBoolean();
            Check(windows.All(w => w.IsCompactArrangementActive == collapsed), "Restart lost whole-group compact state");
            IReadOnlyDictionary<string, RectInt32> targets;
            if (collapsed)
            {
                RestoreMagneticCollapseGroups();
                await Task.Delay(150);
                targets = WidgetMagneticCollapseLayout.Arrange(windows.Select(w => new MagneticCompactMember(
                    w.Config.Id, w.Config.MagneticExpandedPlacement!.ToBounds(), w.MagneticCompactSize)).ToArray());
                targets = WidgetMagneticCollapseLayout.KeepVisible(targets, windows[0].MagneticWorkArea);
            }
            else
            {
                targets = previous.RootElement.GetProperty("finalBounds").EnumerateArray().ToDictionary(
                    e => e.GetProperty("id").GetString()!, e => new RectInt32(e.GetProperty("X").GetInt32(),
                        e.GetProperty("Y").GetInt32(), e.GetProperty("Width").GetInt32(), e.GetProperty("Height").GetInt32()));
            }
            foreach (var window in windows)
                Check(Same(window.CoordinatedMoveBounds, targets[window.Config.Id]), "Restart changed compact spatial layout");
            var data = await _quickCaptureService.GetDataAsync();
            if (previous.RootElement.TryGetProperty("noteId", out var noteId))
                Check(data.Items.Any(i => i.Id == noteId.GetString()), "Restart lost a saved note");
            if (previous.RootElement.TryGetProperty("clipboardItemId", out var clipboardId))
                Check(data.RecentItems.Any(i => i.Id == clipboardId.GetString()), "Restart lost clipboard history");
            Check(GetLoadedDesktopWindows().Any(w => w.Config.WidgetKind == WidgetKind.Clipboard), "Restart did not restore the clipboard surface");
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = true, linkedSurfaces = windows.Length,
                spatialLayout = true, notesAndClipboard = true, collapsed }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool Same(RectInt32 a, RectInt32 b) =>
        a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
}
#endif
