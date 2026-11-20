using MagneticDesk.Models;
using MagneticDesk.Views;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private sealed record LayoutSnapshot(WidgetLayoutState State, RectInt32? VisibleBounds,
        bool Collapsed, string SurfaceKey, string? GroupCollapseBehavior);
    private sealed record LayoutUndoEntry(string Description,
        Dictionary<string, LayoutSnapshot> Before, Dictionary<string, LayoutSnapshot> After,
        string? TopologyKey, long PlacementGeneration, string[]? CapsuleOrder, string[]? ExpectedCapsuleOrder);
    private sealed record LayoutOperation(string OwnerId, string Description,
        Dictionary<string, LayoutSnapshot> Before, HashSet<string> Members, string? TopologyKey, long PlacementGeneration, string[] CapsuleOrder);

    private readonly List<LayoutUndoEntry> _layoutUndoHistory = [];
    private LayoutOperation? _layoutOperation;
    private bool _restoringLayoutHistory;

    private string LayoutSurfaceKey(WidgetConfig config)
    {
        var group = WidgetGroupSettings.FindByMember(_settingsService.Settings, config.Id);
        return group is null ? config.Id : group.SurfaceId + "/" + string.Join("/", group.MemberIds.Order());
    }

    private Dictionary<string, LayoutSnapshot> CaptureLayoutSnapshot()
    {
        var windows = GetLoadedDesktopWindows().ToDictionary(w => w.Config.Id);
        return _settingsService.Settings.WidgetLayout.Widgets.Where(w => !IsDeleted(w.Id)).ToDictionary(
            w => w.Id, w => new LayoutSnapshot(WidgetLayoutState.Capture(w) with { MagneticGroupId = GetMagneticGroupId(w) },
                windows.GetValueOrDefault(w.Id) is WidgetWindowBase view
                    ? view.RestingBoundsForLayout : windows.GetValueOrDefault(w.Id)?.CoordinatedMoveBounds,
                windows.GetValueOrDefault(w.Id)?.IsCompactArrangementActive ?? w.IsCollapsed,
                LayoutSurfaceKey(w), WidgetGroupSettings.FindByMember(_settingsService.Settings, w.Id)?.CollapseBehavior));
    }

    internal void BeginLayoutOperation(string ownerId, string description)
    {
        if (_restoringLayoutHistory) return;
        if (_layoutOperation is null)
        {
            if (_layoutUndoHistory.Count > 0 &&
                (_layoutUndoHistory[^1].PlacementGeneration != _placementGeneration ||
                 _layoutUndoHistory[^1].TopologyKey != _settingsService.Settings.WidgetLayout.ActiveWidgetTopologyKey))
                _layoutUndoHistory.Clear();
            _layoutOperation = new(ownerId, description, CaptureLayoutSnapshot(), [],
                _settingsService.Settings.WidgetLayout.ActiveWidgetTopologyKey, _placementGeneration,
                _settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder.ToArray());
        }
        IncludeLayoutOperationMembers(ownerId);
    }

    internal void CancelLayoutOperation(string ownerId)
    {
        if (_layoutOperation?.OwnerId == ownerId) _layoutOperation = null;
    }

    private void IncludeLayoutOperationMembers(string widgetId)
    {
        if (_layoutOperation is not { } operation || FindConfig(widgetId) is not { } config) return;
        string? groupId = GetMagneticGroupId(config);
        foreach (var widget in _settingsService.Settings.WidgetLayout.Widgets.Where(w =>
            w.Id == widgetId || (groupId is not null && GetMagneticGroupId(w) == groupId) ||
            LayoutSurfaceKey(w) == LayoutSurfaceKey(config)))
            operation.Members.Add(widget.Id);
    }

    internal void CompleteLayoutOperation(string ownerId)
    {
        if (_layoutOperation is not { } operation || operation.OwnerId != ownerId) return;
        _layoutOperation = null;
        var after = CaptureLayoutSnapshot();
        bool capsuleOrderChanged = !operation.CapsuleOrder.SequenceEqual(_settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder);
        var changed = operation.Members.Where(id => operation.Before.TryGetValue(id, out var previous) &&
            after.TryGetValue(id, out var current) && previous.SurfaceKey == current.SurfaceKey &&
            (previous.State != current.State || capsuleOrderChanged)).ToArray();
        if (changed.Length == 0)
        {
            if (_layoutUndoHistory.Count > 0 && _layoutUndoHistory[^1].PlacementGeneration == operation.PlacementGeneration)
                _layoutUndoHistory[^1] = _layoutUndoHistory[^1] with { PlacementGeneration = _placementGeneration };
            return;
        }
        _layoutUndoHistory.Add(new(operation.Description,
            changed.ToDictionary(id => id, id => operation.Before[id]),
            changed.ToDictionary(id => id, id => after[id]), operation.TopologyKey, _placementGeneration,
            capsuleOrderChanged ? operation.CapsuleOrder : null,
            capsuleOrderChanged ? _settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder.ToArray() : null));
        if (_layoutUndoHistory.Count > 30) _layoutUndoHistory.RemoveAt(0);
    }

    private IDisposable TrackLayoutOperation(string ownerId, string description)
    {
        bool ownsOperation = _layoutOperation is null && !_restoringLayoutHistory;
        BeginLayoutOperation(ownerId, description);
        return new LayoutOperationScope(this, ownerId, ownsOperation);
    }

    private sealed class LayoutOperationScope(WidgetManager manager, string ownerId, bool ownsOperation) : IDisposable
    {
        public void Dispose()
        {
            if (ownsOperation) manager.CompleteLayoutOperation(ownerId);
        }
    }

    internal bool CanUndoLayout => _layoutOperation is null && _layoutUndoHistory.Count > 0 &&
        CanRestoreLayoutEntry(_layoutUndoHistory[^1]);

    private bool CanRestoreLayoutEntry(LayoutUndoEntry entry) =>
        entry.PlacementGeneration == _placementGeneration &&
        (entry.ExpectedCapsuleOrder is null || entry.ExpectedCapsuleOrder.SequenceEqual(_settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder)) &&
        entry.TopologyKey == _settingsService.Settings.WidgetLayout.ActiveWidgetTopologyKey &&
        !GetLoadedDesktopWindows().Any(w => w.IsBoundsTransitionActive || w.IsTitleBarDragTransitionActive) &&
        entry.After.All(pair => FindConfig(pair.Key) is { } config && !IsDeleted(pair.Key) &&
            LayoutSurfaceKey(config) == pair.Value.SurfaceKey && !IsMagneticLayoutLocked(config) &&
            GetMagneticGroupId(config) == pair.Value.State.MagneticGroupId);

    internal bool UndoLayout()
    {
        if (!CanUndoLayout) return false;
        LayoutUndoEntry entry = _layoutUndoHistory[^1];
        var rollback = CaptureLayoutSnapshot();
        var previousOrder = _settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder.ToArray();
        _restoringLayoutHistory = true;
        ClearMagneticDockFeedback();
        _layoutGuides?.EndDrag();
        _layoutGuides?.EndResize();
        try
        {
            if (entry.CapsuleOrder is { } order)
            {
                _settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder.Clear();
                _settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder.AddRange(order);
            }
            RestoreLayoutSnapshots(entry.Before, entry.After);
            _layoutUndoHistory.RemoveAt(_layoutUndoHistory.Count - 1);
            if (_layoutUndoHistory.Count > 0)
                _layoutUndoHistory[^1] = _layoutUndoHistory[^1] with { PlacementGeneration = _placementGeneration };
            App.Log($"[LayoutUndo] Restored operation={entry.Description} widgets={entry.Before.Count}");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[LayoutUndo] Restore failed: {ex}");
            _settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder.Clear();
            _settingsService.Settings.WidgetShell.WidgetCapsuleBarOrder.AddRange(previousOrder);
            RestoreLayoutSnapshots(entry.Before.Keys.ToDictionary(id => id, id => rollback[id]));
            return false;
        }
        finally { _restoringLayoutHistory = false; }
    }

    private void RestoreLayoutSnapshots(Dictionary<string, LayoutSnapshot> snapshots,
        Dictionary<string, LayoutSnapshot>? expected = null)
    {
        if (expected is not null)
        {
            snapshots = snapshots.ToDictionary(pair => pair.Key, pair =>
            {
                var current = FindConfig(pair.Key)!;
                var before = pair.Value;
                var after = expected[pair.Key];
                string? currentBehavior = current.Metadata.GetValueOrDefault(WidgetCollapseBehaviorNames.MetadataKey);
                bool preserveBehavior = before.State.CollapseBehavior == after.State.CollapseBehavior ||
                    currentBehavior != after.State.CollapseBehavior;
                return before with
                {
                    State = before.State with
                    {
                        CapsuleStyle = before.State.CapsuleStyle == after.State.CapsuleStyle ||
                            current.CapsuleStyle != after.State.CapsuleStyle ? current.CapsuleStyle : before.State.CapsuleStyle,
                        CollapseBehavior = preserveBehavior ? currentBehavior : before.State.CollapseBehavior
                    },
                    GroupCollapseBehavior = preserveBehavior
                        ? WidgetGroupSettings.FindByMember(_settingsService.Settings, current.Id)?.CollapseBehavior
                        : before.GroupCollapseBehavior
                };
            });
        }
        foreach (string id in snapshots.Keys.Select(id => GetMagneticGroupId(FindConfig(id)!))
            .Concat(snapshots.Values.Select(snapshot => snapshot.State.MagneticGroupId)).OfType<string>().Distinct())
            InvalidateMagneticMembership(id);
        var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(w => snapshots.ContainsKey(w.Config.Id)).ToArray();
        foreach (var window in windows) window.BeginCoordinatedMoveParticipation(isSource: false);
        try
        {
            foreach (var (id, snapshot) in snapshots)
            {
                snapshot.State.Restore(FindConfig(id)!);
                if (WidgetGroupSettings.FindByMember(_settingsService.Settings, id) is { } group)
                {
                    group.MagneticGroupId = snapshot.State.MagneticGroupId;
                    if (snapshot.GroupCollapseBehavior is { } behavior) group.CollapseBehavior = behavior;
                }
            }
            foreach (var window in windows)
            {
                var snapshot = snapshots[window.Config.Id];
                window.RefreshMagneticCollapseBehavior();
                if (snapshot.VisibleBounds is { } bounds)
                {
                    window.ApplyMagneticCollapsedState(snapshot.Collapsed, bounds, persist: false, animate: false);
                    if (_lastCapsuleBarBounds.ContainsKey(window.Config.Id))
                        _lastCapsuleBarBounds[window.Config.Id] = bounds;
                }
                window.ResetMagneticInteractionState();
            }
            // Collapse visuals can recapture bounds. The saved placement remains authoritative.
            foreach (var (id, snapshot) in snapshots)
            {
                var config = FindConfig(id)!;
                snapshot.State.Restore(config);
                config.LastUserPlacementUtc = DateTimeOffset.UtcNow;
                if (WidgetGroupSettings.FindByMember(_settingsService.Settings, id) is { } group)
                {
                    group.MagneticGroupId = config.MagneticGroupId;
                    if (snapshot.GroupCollapseBehavior is { } behavior) group.CollapseBehavior = behavior;
                }
                SynchronizeGroupLayoutFromMember(config);
                MarkSurfaceEntryAuthoritative(config);
            }
            _placementGeneration++;
            _settingsService.UpdateWidgetsBatch(snapshots.Keys.Select(id => FindConfig(id)!), notifySubscribers: false);
            _settingsService.SaveDebounced(notifySubscribers: false);
            RefreshMagneticGroupChrome();
            NoteWidgetWindowsMoved();
            RefreshMouseLeaveMembership();
        }
        finally
        {
            foreach (var window in windows)
                window.CompleteCoordinatedMoveParticipation(hasMoved: false, isSource: false);
        }
    }
}
