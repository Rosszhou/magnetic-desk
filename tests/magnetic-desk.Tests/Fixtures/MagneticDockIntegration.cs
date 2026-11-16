#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Reflection;
using System.Text.Json;
using MagneticDesk.Models;
using MagneticDesk.Platform;
using MagneticDesk.Views;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal async Task RunMagneticDockIntegrationAsync()
    {
        string resultPath = Path.Combine(MagneticDeskDataPathService.Current.RootPath, "magnetic-dock-integration.json");
        IDesktopWidgetWindow? source = null;
        FieldInfo dragging = typeof(WidgetWindowBase).GetField("IsDragging", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo moved = typeof(WidgetWindowBase).GetField("HasMovedTitleBarDrag", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            await Task.Delay(1000);
            await VerifyRefinementRestartAsync();
            await VerifyCapsuleStyleRestartAsync();
            await VerifyMagneticCollapseRestartAsync();
            await VerifyMagneticResizeRestartAsync();
            await CreateTodoWidgetAsync("磁吸测试");
            await Task.Delay(700);
            source = GetLoadedDesktopWindows().First(w => w.Config.WidgetKind == WidgetKind.File);
            IDesktopWidgetWindow target = GetLoadedDesktopWindows().First(w => w.Config.WidgetKind == WidgetKind.Todo);
            SetMagneticGroupId(source.Config, null);
            SetMagneticGroupId(target.Config, null);
            foreach (var window in new[] { (WidgetWindowBase)source, (WidgetWindowBase)target })
            {
                window.Config.IsCollapsed = false;
                WidgetCollapseBehaviorNames.SetOverride(window.Config, WidgetCollapseBehavior.Click);
                window.RefreshMagneticCollapseBehavior();
                window.RequestCompactState(false, false);
            }
            await Task.Delay(400);
            RectInt32 targetBounds = target.CoordinatedMoveBounds;
            target.BeginCoordinatedMoveParticipation(false);
            target.ApplyCoordinatedMoveBoundsFallback(new RectInt32(1200, 650, targetBounds.Width, targetBounds.Height));
            target.CompleteCoordinatedMoveParticipation(true, false);
            targetBounds = target.CoordinatedMoveBounds;
            RectInt32 sourceBounds = source.CoordinatedMoveBounds;
            double scale = Win32Helper.GetDpiScaleForWindow(source.WindowHandle, source.WindowContentRoot?.XamlRoot);
            int spacing = (int)Math.Round(_settingsService.Settings.WidgetSnapSpacing * scale);
            RectInt32 near = new(targetBounds.X - sourceBounds.Width - spacing - 10,
                targetBounds.Y + 40, sourceBounds.Width, sourceBounds.Height);
            source.BeginCoordinatedMoveParticipation(false);
            source.ApplyCoordinatedMoveBoundsFallback(near);
            foreach (var surface in new[] { (WidgetWindowBase)source, (WidgetWindowBase)target })
            {
                ArmMouseLeaveAutoCollapse(surface);
            }
            await Task.Delay(100);
            var almostExpired = new WidgetMouseLeaveCollapsePolicy();
            almostExpired.Update(_mouseLeaveClock.Elapsed - TimeSpan.FromMilliseconds(4900), false);
            _mouseLeaveScopes[MouseLeaveScopeId(target.Config)] = (target.Config.Id, almostExpired);
            dragging.SetValue(source, true);
            moved.SetValue(source, true);
            if (!UpdateMagneticDockDrag(source.Config.Id)) throw new InvalidOperationException("Near edges did not arm countdown");
            UpdateMouseLeaveAutoCollapse(_mouseLeaveClock.Elapsed + TimeSpan.FromSeconds(10));
            Check(!target.IsCompactArrangementActive && !_mouseLeaveScopes[MouseLeaveScopeId(target.Config)].Policy.IsCounting,
                "Magnetic countdown lost priority to an almost-expired collapse countdown");
            await Task.Delay(1100);
            Check(!target.IsCompactArrangementActive, "Target collapsed during magnetic countdown");
            source.ApplyCoordinatedMoveBoundsFallback(new RectInt32(near.X - 200, near.Y, near.Width, near.Height));
            UpdateMagneticDockDrag(source.Config.Id);
            Check(!_mouseLeaveScopes.ContainsKey(MouseLeaveScopeId(target.Config)), "Cancelled magnetic contact kept its old collapse deadline");
            UpdateMouseLeaveAutoCollapse(_mouseLeaveClock.Elapsed);
            Check(!target.IsCompactArrangementActive, "Cancelled magnetic contact immediately collapsed its target");
            await Task.Delay(2200);
            if (GetMagneticGroupId(source.Config) is not null) throw new InvalidOperationException("Leaving the edge did not cancel");
            source.ApplyCoordinatedMoveBoundsFallback(near);
            UpdateMagneticDockDrag(source.Config.Id);
            await Task.Delay(3300);
            string? group = GetMagneticGroupId(source.Config);
            if (group is null || group != GetMagneticGroupId(target.Config)) throw new InvalidOperationException("Countdown did not link the surfaces");
            RectInt32 docked = source.CoordinatedMoveBounds;
            if (docked.X + docked.Width + spacing != target.CoordinatedMoveBounds.X)
                throw new InvalidOperationException($"Completed surfaces were not aligned source={docked.X},{docked.Y},{docked.Width},{docked.Height} target={target.CoordinatedMoveBounds.X},{target.CoordinatedMoveBounds.Y} spacing={spacing}");
            dragging.SetValue(source, false);
            moved.SetValue(source, false);
            source.CompleteCoordinatedMoveParticipation(true, false);
            if (TryBeginCoordinatedMove(source.WindowHandle, controlPressed: true))
                throw new InvalidOperationException("Ctrl drag started a group move");
            RectInt32 sourceBefore = source.CoordinatedMoveBounds;
            RectInt32 targetBefore = target.CoordinatedMoveBounds;
            source.BeginCoordinatedMoveParticipation(false);
            if (!TryBeginCoordinatedMove(source.WindowHandle, controlPressed: false))
                throw new InvalidOperationException("Default drag did not start a group move");
            UpdateCoordinatedMove(source.WindowHandle, 17, -9);
            CompleteCoordinatedMove(source.WindowHandle, true);
            if (source.CoordinatedMoveBounds.X - sourceBefore.X != 17 ||
                target.CoordinatedMoveBounds.X - targetBefore.X != 17 ||
                source.CoordinatedMoveBounds.Y - sourceBefore.Y != -9 ||
                target.CoordinatedMoveBounds.Y - targetBefore.Y != -9)
                throw new InvalidOperationException($"Default group drag changed relative placement source={sourceBefore.X},{sourceBefore.Y}->{source.CoordinatedMoveBounds.X},{source.CoordinatedMoveBounds.Y} target={targetBefore.X},{targetBefore.Y}->{target.CoordinatedMoveBounds.X},{target.CoordinatedMoveBounds.Y}");
            await _settingsService.FlushPendingSaveAsync(false);
            using JsonDocument saved = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(MagneticDeskDataPathService.Current.DataDirectory, "widget-layout.json")));
            int savedCount = saved.RootElement.GetProperty("layout").GetProperty("widgets").EnumerateArray()
                .Count(widget => widget.TryGetProperty("magneticGroupId", out JsonElement id) && id.GetString() == group);
            if (savedCount != 2) throw new InvalidOperationException("Magnetic membership was not persisted");
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
            {
                passed = true, cancelledOnLeave = true, attachedAfterThreeSeconds = true, magneticCountdownPriority = true, collapseResetOnCancel = true,
                aligned = true, defaultGroupDrag = true, controlSingleDrag = true, persistedMembers = savedCount, group
            }));
            App.Log("[MagneticDockIntegration] Passed cancel, countdown, alignment and persistence");
            await RunMagneticCollapseIntegrationAsync();
            await RunMagneticMouseLeaveIntegrationAsync();
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
            App.Log($"[MagneticDockIntegration] Failed: {error}");
        }
        finally
        {
            if (source is not null)
            {
                dragging.SetValue(source, false);
                moved.SetValue(source, false);
                CancelMagneticDockDrag(source.Config.Id);
            }
        }
    }
}
#endif
