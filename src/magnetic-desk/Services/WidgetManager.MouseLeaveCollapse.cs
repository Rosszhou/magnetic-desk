using System.Diagnostics;
using MagneticDesk.Views;
using Microsoft.UI.Dispatching;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private readonly HashSet<WidgetWindowBase> _mouseLeaveExpandedWindows = [];
    private readonly Dictionary<string, (string Members, WidgetMouseLeaveCollapsePolicy Policy)> _mouseLeaveScopes = [];
    private readonly Stopwatch _mouseLeaveClock = Stopwatch.StartNew();
    private DispatcherQueueTimer? _mouseLeaveTimer;

    internal bool HasMouseLeaveAutoCollapse(WidgetWindowBase window) => _mouseLeaveExpandedWindows.Contains(window);

    internal bool HasActiveMouseLeaveCountdown =>
        _mouseLeaveScopes.Values.Any(session => session.Policy.IsCounting);

    private string MouseLeaveScopeId(Models.WidgetConfig config) => config.MagneticCollapseLinked && GetMagneticGroupId(config) is { } id
        ? $"magnetic:{id}" : $"surface:{config.Id}";

    private WidgetWindowBase[] GetMouseLeaveScope(WidgetWindowBase window) =>
        GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(w => w.Visible && MouseLeaveScopeId(w.Config) == MouseLeaveScopeId(window.Config)).ToArray();

    private void ResetMouseLeaveCountdown(IDesktopWidgetWindow? window)
    {
        if (window is not WidgetWindowBase surface) return;
        _mouseLeaveScopes.Remove(MouseLeaveScopeId(surface.Config));
        foreach (var member in GetMouseLeaveScope(surface)) member.ShowMouseLeaveCountdown(false, 1);
    }

    private bool IsMagneticDockScopeActive(WidgetWindowBase window) =>
        _magneticDockSource is { } source &&
        (MouseLeaveScopeId(window.Config) == MouseLeaveScopeId(source.Config) ||
            (_magneticDockTarget is { } target && MouseLeaveScopeId(window.Config) == MouseLeaveScopeId(target.Config)));

    internal void ArmMouseLeaveAutoCollapse(WidgetWindowBase window)
    {
        NormalizeMagneticCollapseMembership(window.Config);
        if (!window.CanCollapseAfterMouseLeave) return;
        foreach (var member in GetMouseLeaveScope(window).Where(w => w.CanCollapseAfterMouseLeave && !w.IsCompactArrangementActive))
            _mouseLeaveExpandedWindows.Add(member);
        if (_mouseLeaveExpandedWindows.Count == 0) return;
        if (_mouseLeaveTimer is null)
        {
            _mouseLeaveTimer = window.DispatcherQueue.CreateTimer();
            _mouseLeaveTimer.Interval = TimeSpan.FromMilliseconds(33);
            _mouseLeaveTimer.Tick += (_, _) => UpdateMouseLeaveAutoCollapse(_mouseLeaveClock.Elapsed);
        }
        _mouseLeaveTimer.Start();
    }

    internal void CancelMouseLeaveAutoCollapse(WidgetWindowBase window, bool wholeScope = true)
    {
        if (wholeScope) NormalizeMagneticCollapseMembership(window.Config);
        var members = wholeScope ? GetMouseLeaveScope(window) : [window];
        foreach (var member in members)
        {
            _mouseLeaveExpandedWindows.Remove(member);
            member.ShowMouseLeaveCountdown(false, 1);
        }
        if (_mouseLeaveExpandedWindows.Count == 0)
        {
            _mouseLeaveScopes.Clear();
            _mouseLeaveTimer?.Stop();
        }
    }

    private void RefreshMouseLeaveMembership()
    {
        foreach (var window in _mouseLeaveExpandedWindows.ToArray()) ArmMouseLeaveAutoCollapse(window);
        // Reset only the scopes whose membership changed. Independent groups
        // retain their own elapsed interval.
        var scopes = _mouseLeaveExpandedWindows.GroupBy(w => MouseLeaveScopeId(w.Config)).ToArray();
        foreach (string obsolete in _mouseLeaveScopes.Keys.Except(scopes.Select(s => s.Key)).ToArray())
            _mouseLeaveScopes.Remove(obsolete);
        foreach (var scope in scopes)
        {
            string members = string.Join("|", scope.Select(w => w.Config.Id).Order(StringComparer.Ordinal));
            if (_mouseLeaveScopes.TryGetValue(scope.Key, out var session) && session.Members == members) continue;
            _mouseLeaveScopes.Remove(scope.Key);
            foreach (var window in scope) window.ShowMouseLeaveCountdown(false, 1);
        }
    }

    internal void UpdateMouseLeaveAutoCollapse(TimeSpan now)
    {
        foreach (var window in _mouseLeaveExpandedWindows.ToArray())
        {
            if (!window.Visible || !window.CanCollapseAfterMouseLeave || window.IsCompactArrangementActive)
            {
                _mouseLeaveExpandedWindows.Remove(window);
                window.ShowMouseLeaveCountdown(false, 1);
            }
        }
        var visibleByScope = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(w => w.Visible)
            .GroupBy(w => MouseLeaveScopeId(w.Config))
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var scopes = _mouseLeaveExpandedWindows.GroupBy(w => MouseLeaveScopeId(w.Config)).Select(g => (Id: g.Key, Windows: g.ToArray())).ToArray();
        foreach (string obsolete in _mouseLeaveScopes.Keys.Except(scopes.Select(s => s.Id)).ToArray())
            _mouseLeaveScopes.Remove(obsolete);
        foreach (var scope in scopes)
        {
            string members = string.Join("|", scope.Windows.Select(w => w.Config.Id).Order(StringComparer.Ordinal));
            if (!_mouseLeaveScopes.TryGetValue(scope.Id, out var session) || session.Members != members)
                _mouseLeaveScopes[scope.Id] = session = (members, new WidgetMouseLeaveCollapsePolicy());
            var scopeMembers = visibleByScope.TryGetValue(scope.Id, out var loaded) ? loaded : scope.Windows;
            bool hasActivity = IsMagneticDockScopeActive(scope.Windows[0]) ||
                scopeMembers.Any(w => w.MouseLeaveScopeHasActivity);
            bool expired = session.Policy.Update(now, hasActivity);
            if (!expired)
            {
                if (session.Policy.ShouldPreArmCollapse(now))
                    scope.Windows[0].PreArmCompactAnimationClock();
                bool counting = session.Policy.IsCounting;
                double remaining = session.Policy.RemainingFraction(now);
                foreach (var member in scope.Windows)
                    member.ShowMouseLeaveCountdown(counting, remaining);
                continue;
            }
            foreach (var member in scope.Windows)
            {
                _mouseLeaveExpandedWindows.Remove(member);
                member.HideMouseLeaveCountdownForCollapse();
            }
            _mouseLeaveScopes.Remove(scope.Id);
            foreach (var member in scope.Windows.Where(w => !w.IsCompactArrangementActive)) member.CollapseAfterMouseLeave();
            App.Log($"[MouseLeaveCollapse] Collapsed count={scope.Windows.Length} delayMs=5000");
        }
        if (_mouseLeaveExpandedWindows.Count == 0) _mouseLeaveTimer?.Stop();
    }
}
