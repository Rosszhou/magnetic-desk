#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Reflection;
using System.Text.Json;
using MagneticDesk.Models;
using MagneticDesk.Views;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal async Task VerifyMagneticLifecycleAsync()
    {
        if (!MagneticDeskDataPathService.Current.IsDevelopmentRoot)
            throw new InvalidOperationException("Lifecycle verification requires an isolated development profile.");
        string root = MagneticDeskDataPathService.Current.RootPath;
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }
        var dragging = typeof(WidgetWindowBase).GetField("IsDragging", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var moved = typeof(WidgetWindowBase).GetField("HasMovedTitleBarDrag", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? Invoke(WidgetWindowBase window, string name, params object[] arguments) =>
            typeof(WidgetWindowBase).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
        void Drag(WidgetWindowBase window, bool active)
        {
            bool wasDragging = (bool)dragging.GetValue(window)!;
            if (active && !wasDragging)
            {
                typeof(WidgetWindowBase).GetField("_magneticDockCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
                typeof(WidgetWindowBase).GetField("_controlDragPreviousCompanions", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, Array.Empty<string>());
                Invoke(window, "BeginWidgetBoundsInteraction");
            }
            dragging.SetValue(window, active);
            moved.SetValue(window, active);
            if (!active && wasDragging)
            {
                Invoke(window, "CompleteCompactArrangementDrag");
                Invoke(window, "CompleteExpandedWidgetDrag", window.CoordinatedMoveBounds, true);
                Invoke(window, "CommitUserPlacementFromWindow", WidgetPlacementSource.Drag);
                Invoke(window, "EndWidgetBoundsInteraction");
            }
        }
        void Place(WidgetWindowBase window, RectInt32 bounds)
        {
            window.ApplyMagneticResizeBounds(bounds);
            window.CommitMagneticResizeLayout(bounds);
        }
        try
        {
            await Task.Delay(1800);
            var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().OrderBy(w => w.Config.Id).ToArray();
            Check(windows.Length == 4, "four native widget windows loaded");
            RectInt32[] initial = [new(100, 100, 540, 610), new(650, 100, 600, 610),
                new(1500, 100, 430, 500), new(2500, 100, 500, 500)];
            for (int i = 0; i < windows.Length; i++)
            {
                Check(GetMagneticGroupId(windows[i].Config) is null && !windows[i].Config.MagneticCollapseLinked,
                    "profile starts with free unlinked widget " + i);
                WidgetCollapseBehaviorNames.SetOverride(windows[i].Config, WidgetCollapseBehavior.Smart);
                windows[i].RefreshMagneticCollapseBehavior();
                windows[i].RequestCompactState(false, false);
                Place(windows[i], initial[i]);
            }
            var left = windows[0];
            var right = windows[1];
            var incoming = windows[2];
            JoinMagneticGroup(left.Config.Id, right.Config.Id);
            _mouseLeaveTimer?.Stop();
            string firstGroup = GetMagneticGroupId(left.Config)!;
            Check(firstGroup == GetMagneticGroupId(right.Config) && left.Config.MagneticCollapseLinked && right.Config.MagneticCollapseLinked,
                "first free pair automatically links collapse state");
            Check(WidgetCollapseBehaviorNames.Resolve(left.Config, "Expanded") == WidgetCollapseBehavior.Smart,
                "new member inherits target collapse behavior");
            var pair = new[] { left, right };
            var joinedBounds = pair.ToDictionary(w => w.Config.Id, w => w.CoordinatedMoveBounds);
            left.RequestCompactState(true, false);
            Check(pair.All(w => w.IsCompactArrangementActive), "manual collapse closes fresh pair together");
            right.RequestCompactState(false, false);
            Check(pair.All(w => !w.IsCompactArrangementActive), "manual expansion opens fresh pair together");
            Check(pair.All(w => w.CoordinatedMoveBounds.Equals(joinedBounds[w.Config.Id])), "fresh pair restores canonical expanded geometry");
            left.RequestCompactState(true, false);
            Check(DissolveMagneticGroup(left.Config), "collapsed pair dissolves through normal command");
            Check(pair.All(w => GetMagneticGroupId(w.Config) is null && !w.Config.MagneticCollapseLinked &&
                w.Config.MagneticAttachmentPlacement is null && w.Config.MagneticCompactPlacement is null),
                "dissolve clears membership and attachment homes");
            foreach (var window in pair) window.RequestCompactState(false, false);
            JoinMagneticGroup(left.Config.Id, right.Config.Id);
            _mouseLeaveTimer?.Stop();
            Check(pair.All(w => w.Config.MagneticCollapseLinked) && GetMagneticGroupId(left.Config) != firstGroup,
                "regroup creates a fresh synchronized group");
            left.RequestCompactState(true, false);
            right.RequestCompactState(false, false);
            Check(pair.All(w => !w.IsCompactArrangementActive), "regroup expansion remains synchronized");

            string staleId = GetMagneticGroupId(left.Config)!;
            _pendingMagneticCollapse.Add(staleId);
            var staleClose = RetryMagneticCollapseAsync(left, staleId, MagneticMembershipRevision(staleId));
            DissolveMagneticGroup(left.Config);
            JoinMagneticGroup(left.Config.Id, right.Config.Id);
            _mouseLeaveTimer?.Stop();
            await staleClose;
            Check(pair.All(w => !w.IsCompactArrangementActive), "old delayed collapse cannot affect regrouped widgets");
            foreach (var window in pair) window.Config.MagneticCollapseLinked = false;
            right.RequestCompactState(true, false);
            Check(pair.All(w => w.Config.MagneticCollapseLinked && w.IsCompactArrangementActive),
                "legacy false linked flags repair through normal collapse entry");
            right.RequestCompactState(false, false);
            DetachMagneticCollapse(left.Config);
            left.RequestCompactState(true, false);
            Check(left.IsCompactArrangementActive && !right.IsCompactArrangementActive && !left.Config.MagneticCollapseLinked,
                "explicit independent collapse scope remains independent");
            SetMagneticGroupCollapseBehavior(right.Config, WidgetCollapseBehavior.Smart);
            await Task.Delay(500);
            right.RequestCompactState(false, false);
            _mouseLeaveTimer?.Stop();
            Check(pair.All(w => w.Config.MagneticCollapseLinked && !w.IsCompactArrangementActive), "whole group setting restores linked scope");
            BeginLayoutOperation(left.Config.Id, "Magnetic.Undo.Move");
            Drag(left, true);
            Check(left.DetachMagneticMemberForControlDrag(true) && ((IDesktopWidgetWindow)left).IsTitleBarDragTransitionActive &&
                GetMagneticGroupId(left.Config) is null, "control drag splits membership and keeps the same gesture active");
            Check(!left.CanMagneticallyDockTo(right.Config.Id), "control drag avoids immediately rejoining former companions");
            Drag(left, false);
            CompleteLayoutOperation(left.Config.Id);
            Check(UndoLayout() && GetMagneticGroupId(left.Config) == GetMagneticGroupId(right.Config), "control split remains undoable");
            _mouseLeaveTimer?.Stop();

            right.RequestCompactState(true, false);
            var capsule = right.CoordinatedMoveBounds;
            Place(incoming, new(capsule.X, capsule.Y + capsule.Height + 10, 430, 500));
            BeginLayoutOperation(incoming.Config.Id, "Magnetic.Undo.Move");
            Drag(incoming, true);
            Check(UpdateMagneticDockDrag(incoming.Config.Id), "approach finds collapsed target");
            Check(pair.All(w => !w.IsCompactArrangementActive), "approach expands entire target group");
            Check(_magneticDockContact is null && _magneticDockExpansion is not null, "expansion settles before accepting preview");
            await Task.Delay(650);
            UpdateMagneticDockDrag(incoming.Config.Id);
            Check(_magneticDockContact is not null, "approach below original capsule resolves expanded target edge");
            var preview = _magneticDockContact!.Value;
            Check(preview.DockedBounds.Y >= right.CoordinatedMoveBounds.Y + right.CoordinatedMoveBounds.Height,
                "preview reserves the full expanded target body");
            var beforeJitter = _magneticDockCountdown.Progress;
            var sourceBounds = incoming.CoordinatedMoveBounds;
            Place(incoming, new(sourceBounds.X + 1, sourceBounds.Y, sourceBounds.Width, sourceBounds.Height));
            UpdateMagneticDockDrag(incoming.Config.Id);
            Check(_magneticDockContact!.Value.DockedBounds.Equals(preview.DockedBounds) && _magneticDockCountdown.Progress >= beforeJitter,
                "one pixel jitter preserves destination and countdown");
            CancelMagneticDockDrag(incoming.Config.Id);
            Drag(incoming, false);
            CancelLayoutOperation(incoming.Config.Id);
            await Task.Delay(600);
            Check(pair.All(w => w.IsCompactArrangementActive) && _magneticDockTarget is null && _magneticDockContact is null &&
                _magneticDockExpansion is null, "cancel restores closed target and clears preview session");

            capsule = right.CoordinatedMoveBounds;
            Place(incoming, new(capsule.X, capsule.Y + capsule.Height + 10, 430, 500));
            BeginLayoutOperation(incoming.Config.Id, "Magnetic.Undo.Move");
            Drag(incoming, true);
            UpdateMagneticDockDrag(incoming.Config.Id);
            await Task.Delay(650);
            UpdateMagneticDockDrag(incoming.Config.Id);
            preview = _magneticDockContact!.Value;
            await Task.Delay(3250);
            Drag(incoming, false);
            CompleteLayoutOperation(incoming.Config.Id);
            ClearMagneticDockFeedback();
            _mouseLeaveTimer?.Stop();
            Check(GetMagneticGroupId(incoming.Config) == GetMagneticGroupId(right.Config), "countdown commits incoming widget to expanded target group");
            Check(incoming.CoordinatedMoveBounds.Equals(preview.DockedBounds), "native committed body equals accepted outline");
            Check(windows.Take(3).All(w => w.Config.MagneticCollapseLinked && !w.IsCompactArrangementActive),
                "joined group retains synchronized expanded state");
            var accepted = windows.Take(3).ToDictionary(w => w.Config.Id, w => w.CoordinatedMoveBounds);
            right.RequestCompactState(true, false);
            right.RequestCompactState(false, false);
            Check(windows.Take(3).All(w => w.CoordinatedMoveBounds.Equals(accepted[w.Config.Id])), "group collapse cycle preserves accepted geometry");
            string undoGroup = GetMagneticGroupId(right.Config)!;
            long undoRevision = MagneticMembershipRevision(undoGroup);
            _pendingMagneticCollapse.Add(undoGroup);
            var undoClose = RetryMagneticCollapseAsync(right, undoGroup, undoRevision);
            Check(UndoLayout() && GetMagneticGroupId(incoming.Config) is null, "dock undo restores incoming independence");
            await undoClose;
            Check(MagneticMembershipRevision(undoGroup) != undoRevision,
                "undo advances membership revision to invalidate old delayed requests");
            _pendingMagneticCollapse.Remove(undoGroup);
            _mouseLeaveTimer?.Stop();
            right.RequestCompactState(true, false);
            capsule = right.CoordinatedMoveBounds;
            incoming.RequestCompactState(true, false);
            incoming.ApplyMagneticReflowBounds(new(capsule.X + capsule.Width + 10, capsule.Y,
                incoming.CoordinatedMoveBounds.Width, incoming.CoordinatedMoveBounds.Height));
            Drag(incoming, true);
            UpdateMagneticDockDrag(incoming.Config.Id);
            await Task.Delay(650);
            UpdateMagneticDockDrag(incoming.Config.Id);
            Check(incoming.IsCompactArrangementActive && _magneticDockContact is not null, "compact incoming widget previews full body while remaining compact");
            preview = _magneticDockContact!.Value;
            await Task.Delay(3250);
            Drag(incoming, false);
            ClearMagneticDockFeedback();
            _mouseLeaveTimer?.Stop();
            Check(GetMagneticGroupId(incoming.Config) == GetMagneticGroupId(right.Config) && !incoming.IsCompactArrangementActive &&
                incoming.CoordinatedMoveBounds.Equals(preview.DockedBounds), "compact incoming widget expands at accepted commit");

            var detached = windows[3];
            right.RequestCompactState(true, false);
            capsule = right.CoordinatedMoveBounds;
            Place(detached, new(capsule.X, capsule.Y + capsule.Height + 10, 500, 500));
            Drag(detached, true);
            UpdateMagneticDockDrag(detached.Config.Id);
            Check(_magneticDockExpansion is not null, "second preview starts before membership changes");
            Check(DissolveMagneticGroup(right.Config), "dissolve during preview succeeds");
            Check(_magneticDockSource is null && _magneticDockExpansion is null && _magneticDockContact is null,
                "dissolve invalidates preview and pending group session");
            await Task.Delay(3400);
            Check(windows.All(w => GetMagneticGroupId(w.Config) is null), "invalidated countdown never recreates dissolved group");
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].RequestCompactState(false, false);
                Place(windows[i], initial[i]);
            }
            detached.RequestCompactState(true, false);
            capsule = detached.CoordinatedMoveBounds;
            Place(incoming, new(capsule.X, capsule.Y + capsule.Height + 10, 430, 500));
            Drag(incoming, true);
            UpdateMagneticDockDrag(incoming.Config.Id);
            await Task.Delay(650);
            UpdateMagneticDockDrag(incoming.Config.Id);
            Check(!detached.IsCompactArrangementActive && _magneticDockContact is not null,
                "standalone collapsed target expands and accepts preview");
            Place(incoming, new(1600, 1400, 430, 500));
            UpdateMagneticDockDrag(incoming.Config.Id);
            Drag(incoming, false);
            ClearMagneticDockFeedback();
            await Task.Delay(500);
            Check(detached.IsCompactArrangementActive, "leaving standalone target restores its compact state");
            incoming.RequestCompactState(true, false);
            JoinMagneticGroup(incoming.Config.Id, detached.Config.Id);
            _mouseLeaveTimer?.Stop();
            Check(GetMagneticGroupId(incoming.Config) == GetMagneticGroupId(detached.Config) &&
                !incoming.IsCompactArrangementActive && !detached.IsCompactArrangementActive,
                "menu join expands compact source and compact target before placement");
            Check(incoming.Config.MagneticExpandedPlacement!.ToBounds().Equals(incoming.CoordinatedMoveBounds),
                "menu join saves the full native expanded body");
            Check(UndoLayout() && incoming.IsCompactArrangementActive && detached.IsCompactArrangementActive &&
                GetMagneticGroupId(incoming.Config) is null, "menu join undo restores both original compact states");
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].RequestCompactState(false, false);
                Place(windows[i], initial[i]);
            }
            JoinMagneticGroup(left.Config.Id, right.Config.Id);
            JoinMagneticGroup(detached.Config.Id, right.Config.Id);
            _mouseLeaveTimer?.Stop();
            RectInt32[] collision = [new(100, 100, 547, 768), new(657, 100, 604, 547),
                new(253, 657, 418, 614), new(681, 657, 580, 614)];
            for (int i = 0; i < windows.Length; i++)
            {
                Place(windows[i], collision[i]);
                windows[i].Config.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(collision[i]);
                windows[i].Config.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(collision[i]);
                windows[i].Config.MagneticAttachmentAnchor = "LeftTop";
                windows[i].Config.MagneticCompactPlacement = null;
            }
            Drag(incoming, true);
            UpdateMagneticDockDrag(incoming.Config.Id);
            Check(ReferenceEquals(_magneticDockTarget, detached) &&
                _magneticDockContact!.Value.DockedBounds.Equals(new RectInt32(253, 878, 418, 614)),
                "docking beside lower right slides below upper left obstacle");
            preview = _magneticDockContact!.Value;
            Place(left, new(100, 100, 547, 900));
            CompleteMagneticDock((IDesktopWidgetWindow)incoming, (IDesktopWidgetWindow)detached, preview);
            Check(GetMagneticGroupId(incoming.Config) is null, "commit rechecks geometry and rejects a stale colliding preview");
            Place(left, collision[0]);
            Place(left, new(100, 100, 547, 1800));
            Check(!UpdateMagneticDockDrag(incoming.Config.Id) && _magneticDockContact is null,
                "fully blocked edge has no accepting outline");
            ClearMagneticDockFeedback();
            Place(left, collision[0]);
            BeginLayoutOperation(incoming.Config.Id, "Magnetic.Undo.Move");
            UpdateMagneticDockDrag(incoming.Config.Id);
            preview = _magneticDockContact!.Value;
            await Task.Delay(3250);
            Drag(incoming, false);
            CompleteLayoutOperation(incoming.Config.Id);
            ClearMagneticDockFeedback();
            _mouseLeaveTimer?.Stop();
            Check(incoming.CoordinatedMoveBounds.Equals(preview.DockedBounds) &&
                windows.All(w => GetMagneticGroupId(w.Config) == GetMagneticGroupId(right.Config)),
                "four member countdown joins without covering an existing member");
            Check(left.CoordinatedMoveBounds.Equals(collision[0]) && right.CoordinatedMoveBounds.Equals(collision[1]) &&
                detached.CoordinatedMoveBounds.Equals(collision[3]), "joining preserves every original target member position");
            var fourBounds = windows.ToDictionary(w => w.Config.Id, w => w.CoordinatedMoveBounds);
            BeginLayoutOperation(left.Config.Id, "Magnetic.Undo.Resize");
            BeginMagneticResize(left);
            UpdateMagneticResize(left, new(100, 100, 547, 968));
            CompleteMagneticResize(left);
            CompleteLayoutOperation(left.Config.Id);
            Check(incoming.CoordinatedMoveBounds.Y == fourBounds[incoming.Config.Id].Y + 200 &&
                detached.CoordinatedMoveBounds.Equals(fourBounds[detached.Config.Id]),
                "height growth moves only the colliding lower column");
            right.RequestCompactState(true, false);
            right.RequestCompactState(false, false);
            Check(detached.CoordinatedMoveBounds.Equals(fourBounds[detached.Config.Id]),
                "collapse cycle preserves the separate right column after resize");
            Check(UndoLayout() && windows.All(w => w.CoordinatedMoveBounds.Equals(fourBounds[w.Config.Id])),
                "resize undo restores all four accepted bodies");
            var leftHandle = ((IDesktopWidgetWindow)left).WindowHandle;
            BeginLayoutOperation(left.Config.Id, "Magnetic.Undo.Move");
            Check(TryBeginCoordinatedMove(leftHandle), "group drag begins through coordinated move entry");
            UpdateCoordinatedMove(leftHandle, 80, 50);
            CompleteCoordinatedMove(leftHandle, true);
            CompleteLayoutOperation(left.Config.Id);
            Check(windows.All(w => w.CoordinatedMoveBounds.X == fourBounds[w.Config.Id].X + 80 &&
                w.CoordinatedMoveBounds.Y == fourBounds[w.Config.Id].Y + 50), "group drag preserves every relative offset");
            Check(UndoLayout(), "group movement remains undoable");
            Check(SetMagneticLayoutLocked(right.Config, true) && !left.CanParticipateInCoordinatedMove,
                "group layout lock prevents accidental movement");
            Check(!TryBeginCoordinatedMove(leftHandle), "locked group rejects coordinated drag");
            SetMagneticLayoutLocked(right.Config, false);
            await _settingsService.FlushPendingSaveAsync(false);
            var reloaded = new SettingsService(Path.Combine(root, "data"));
            await reloaded.LoadAsync();
            Check(reloaded.Settings.WidgetLayout.Widgets.Where(w => w.Id == left.Config.Id || w.Id == right.Config.Id)
                .All(w => w.MagneticCollapseLinked && w.MagneticGroupId == GetMagneticGroupId(right.Config)),
                "synchronized membership survives settings reload");
            DissolveMagneticGroup(right.Config);
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].RequestCompactState(false, false);
                Place(windows[i], initial[i]);
            }
            JoinMagneticGroup(left.Config.Id, right.Config.Id);
            right.RequestCompactState(true, false);
            incoming.RequestCompactState(true, false);
            detached.RequestCompactState(true, false);
            _settingsService.Settings.WidgetCapsuleArrangementMode = SettingsService.WidgetCapsuleArrangementBar;
            _settingsService.Settings.WidgetCapsuleBarPlacement = SettingsService.WidgetCapsuleBarPlacementFloating;
            RefreshCapsuleBarLayout();
            BeginLayoutOperation(incoming.Config.Id, "Magnetic.Undo.Move");
            Drag(incoming, true);
            Check((bool)Invoke(incoming, "BeginCompactArrangementDrag")!, "compact bar drag starts through normal window entry");
            capsule = right.CoordinatedMoveBounds;
            TryMoveCapsuleBar(incoming.Config.Id, new(capsule.X + capsule.Width + 10, capsule.Y,
                incoming.CoordinatedMoveBounds.Width, incoming.CoordinatedMoveBounds.Height), out _);
            UpdateMagneticDockDrag(incoming.Config.Id);
            await Task.Delay(650);
            UpdateMagneticDockDrag(incoming.Config.Id);
            Check(_magneticDockContact is not null, "bar member can preview docking beside a closed magnetic group");
            preview = _magneticDockContact!.Value;
            await Task.Delay(3250);
            Drag(incoming, false);
            CompleteLayoutOperation(incoming.Config.Id);
            ClearMagneticDockFeedback();
            _mouseLeaveTimer?.Stop();
            Check(_capsuleBarDragSession is null && GetMagneticGroupId(incoming.Config) == GetMagneticGroupId(right.Config) &&
                incoming.CoordinatedMoveBounds.Equals(preview.DockedBounds) &&
                incoming.Config.MagneticExpandedPlacement!.ToBounds().Equals(preview.DockedBounds),
                "bar completion preserves the accepted expanded dock body after release");
            await File.WriteAllTextAsync(Path.Combine(root, "lifecycle-passed.json"), JsonSerializer.Serialize(new { checks, passed = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "lifecycle-failed.json"), JsonSerializer.Serialize(new { checks, error = ex.ToString() }));
        }
        finally
        {
            ClearMagneticDockFeedback();
            _mouseLeaveTimer?.Stop();
        }
    }
}
#endif
