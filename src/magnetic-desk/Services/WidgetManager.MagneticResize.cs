using MagneticDesk.Views;
using Windows.Graphics;
using MagneticDesk.Models;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private sealed record MagneticResizeSession(WidgetWindowBase Source, WidgetWindowBase[] Windows,
        Dictionary<string, RectInt32> VisibleBounds, Dictionary<string, RectInt32> ExpandedBounds,
        Dictionary<string, RectInt32> AttachmentBounds, string? Anchor, int Spacing, int Tolerance,
        bool AllExpanded, WidgetWindowBase[] OtherWindows)
    {
        internal bool RejectionFeedbackShown { get; set; }
    }

    private MagneticResizeSession? _magneticResizeSession;

    internal void BeginMagneticResize(WidgetWindowBase source)
    {
        if (_magneticResizeSession is not null) return;
        var visibleWindows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(w => w.Visible && w.MagneticCollapseInitialized).ToArray();
        string? id = GetMagneticGroupId(source.Config);
        WidgetWindowBase[] windows = id is null ? [source] : visibleWindows
            .Where(w => GetMagneticGroupId(w.Config) == id)
            .Append(source).Distinct().ToArray();
        var otherWindows = visibleWindows.Except(windows).ToArray();
        if (windows.Length == 1 && otherWindows.Length == 0) return;
        var visible = windows.ToDictionary(w => w.Config.Id, w => w.CoordinatedMoveBounds);
        bool allExpanded = windows.All(w => !w.IsCompactArrangementActive);
        var expanded = allExpanded ? visible : windows.ToDictionary(w => w.Config.Id,
            w => w.Config.MagneticExpandedPlacement?.ToBounds() ?? w.MagneticExpandedBounds);
        string? anchor = windows.Select(w => w.Config.MagneticAttachmentAnchor).FirstOrDefault(value => value is not null)
            ?? source.Config.PositionAnchor;
        double scale = MagneticDesk.Platform.Win32Helper.GetDpiScaleForWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(source), source.WindowContentRoot?.XamlRoot);
        _magneticResizeSession = new(source, windows, visible, expanded,
            windows.Length > 1 ? MagneticAttachmentBounds(windows) : visible, anchor,
            Math.Max(0, (int)Math.Round(_settingsService.Settings.WidgetShell.WidgetSnapSpacing * scale)),
            Math.Max(1, (int)Math.Round(12 * scale)), allExpanded, otherWindows);
        foreach (var window in windows.Where(w => w != source)) window.BeginCoordinatedMoveParticipation(false);
    }

    private static IReadOnlyDictionary<string, RectInt32> ArrangeMagneticResize(MagneticResizeSession session,
        Dictionary<string, RectInt32> original, RectInt32 sourceBounds, IReadOnlyList<RectInt32> otherBounds, out bool reattached) =>
        WidgetMagneticResizeLayout.ArrangeInteractive(original, session.Source.Config.Id, sourceBounds,
            session.AttachmentBounds, session.Anchor, session.Spacing, session.Tolerance, out reattached,
            otherBounds, reconnect: session.AllExpanded);

    private static RectInt32[] ReadOtherResizeBounds(MagneticResizeSession session) => session.OtherWindows
        .Where(w => w.Visible && w.MagneticCollapseInitialized).Select(w => w.CoordinatedMoveBounds).ToArray();

    private static RectInt32 ConstrainResizeGrowth(MagneticResizeSession session,
        IReadOnlyDictionary<string, RectInt32> original, RectInt32 bounds, IReadOnlyList<RectInt32> otherBounds) =>
        WidgetMagneticResizePolicy.ConstrainGrowth(original[session.Source.Config.Id], bounds,
            original.Where(pair => pair.Key != session.Source.Config.Id).Select(pair => pair.Value)
                .Concat(otherBounds).ToArray(), session.Spacing);

    internal RectInt32 ConstrainMagneticResize(WidgetWindowBase source, RectInt32 bounds)
    {
        if (_magneticResizeSession is not { } session || session.Source != source) return bounds;
        RectInt32 accepted = ConstrainResizeGrowth(session, session.VisibleBounds, bounds, ReadOtherResizeBounds(session));
        if (!accepted.Equals(bounds) && !session.RejectionFeedbackShown)
        {
            session.RejectionFeedbackShown = true;
            source.ShowResizeRejectedFeedback();
        }
        return accepted;
    }

    internal void UpdateMagneticResize(WidgetWindowBase source, RectInt32 bounds)
    {
        if (_magneticResizeSession is not { } session || session.Source != source || session.Windows.Length == 1) return;
        bounds = ConstrainMagneticResize(source, bounds);
        var targets = ArrangeMagneticResize(session, session.VisibleBounds, bounds, ReadOtherResizeBounds(session), out _);
        foreach (var window in session.Windows.Where(w => w.MagneticCollapseInitialized))
        {
            RectInt32 target = targets[window.Config.Id];
            if (!window.CoordinatedMoveBounds.Equals(target)) window.ApplyMagneticResizeBounds(target);
        }
    }

    internal void CompleteMagneticResize(WidgetWindowBase source)
    {
        if (_magneticResizeSession is not { } session || session.Source != source) return;
        try
        {
            if (session.Windows.Length == 1) return;
            RectInt32 bounds = source.CoordinatedMoveBounds;
            UpdateMagneticResize(source, bounds);
            bounds = source.CoordinatedMoveBounds;
            RectInt32 expandedSource = source.ResolveMagneticExpandedResizeBounds(session.ExpandedBounds[source.Config.Id], bounds);
            var otherBounds = ReadOtherResizeBounds(session);
            expandedSource = ConstrainResizeGrowth(session, session.ExpandedBounds, expandedSource, otherBounds);
            var expanded = ArrangeMagneticResize(session, session.ExpandedBounds, expandedSource, otherBounds, out bool reattached);
            foreach (var window in session.Windows.Where(w => w.MagneticCollapseInitialized))
            {
                window.CommitMagneticResizeLayout(expanded[window.Config.Id]);
                foreach (var member in MagneticSurfaceConfigs(window.Config))
                {
                    member.MagneticExpandedPlacement = window.Config.MagneticExpandedPlacement;
                    member.MagneticCompactPlacement = window.Config.MagneticCompactPlacement;
                    if (reattached)
                        member.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(expanded[window.Config.Id]);
                }
            }
            _settingsService.UpdateWidgetsBatch(session.Windows.Where(w => w.MagneticCollapseInitialized).Select(w => w.Config), notifySubscribers: false);
            _settingsService.SaveDebounced(notifySubscribers: false);
            App.Log($"[MagneticResize] Committed source={source.Config.Id} count={session.Windows.Length}");
        }
        finally
        {
            _magneticResizeSession = null;
            foreach (var window in session.Windows.Where(w => w != source))
                window.CompleteCoordinatedMoveParticipation(false, false);
        }
    }
}
