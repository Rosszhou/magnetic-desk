using MagneticDesk.Views;
using Windows.Graphics;
using MagneticDesk.Models;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private sealed record MagneticResizeSession(WidgetWindowBase Source, WidgetWindowBase[] Windows,
        Dictionary<string, RectInt32> VisibleBounds, Dictionary<string, RectInt32> ExpandedBounds,
        Dictionary<string, RectInt32> AttachmentBounds, string? Anchor, int Spacing, int Tolerance,
        bool AllExpanded)
    {
        internal bool RejectionFeedbackShown { get; set; }
    }

    private MagneticResizeSession? _magneticResizeSession;

    internal void BeginMagneticResize(WidgetWindowBase source)
    {
        if (_magneticResizeSession is not null || GetMagneticGroupId(source.Config) is not { } id) return;
        var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(w => w.Visible && w.MagneticCollapseInitialized && GetMagneticGroupId(w.Config) == id).ToArray();
        if (windows.Length < 2) return;
        var visible = windows.ToDictionary(w => w.Config.Id, w => w.CoordinatedMoveBounds);
        bool allExpanded = windows.All(w => !w.IsCompactArrangementActive);
        var expanded = allExpanded ? visible : windows.ToDictionary(w => w.Config.Id,
            w => w.Config.MagneticExpandedPlacement?.ToBounds() ?? w.MagneticExpandedBounds);
        string? anchor = windows.Select(w => w.Config.MagneticAttachmentAnchor).FirstOrDefault(value => value is not null)
            ?? source.Config.PositionAnchor;
        double scale = MagneticDesk.Platform.Win32Helper.GetDpiScaleForWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(source), source.WindowContentRoot?.XamlRoot);
        _magneticResizeSession = new(source, windows, visible, expanded, MagneticAttachmentBounds(windows), anchor,
            Math.Max(0, (int)Math.Round(_settingsService.Settings.WidgetShell.WidgetSnapSpacing * scale)),
            Math.Max(1, (int)Math.Round(12 * scale)), allExpanded);
        foreach (var window in windows.Where(w => w != source)) window.BeginCoordinatedMoveParticipation(false);
    }

    private static IReadOnlyDictionary<string, RectInt32> ArrangeMagneticResize(MagneticResizeSession session,
        Dictionary<string, RectInt32> original, RectInt32 sourceBounds, out bool reattached)
    {
        if (session.AllExpanded)
            return WidgetMagneticResizeLayout.ArrangeInteractive(original, session.Source.Config.Id, sourceBounds,
                session.AttachmentBounds, session.Anchor, session.Spacing, session.Tolerance, out reattached);
        reattached = false;
        return WidgetMagneticResizeLayout.Arrange(original, session.Source.Config.Id, sourceBounds,
            session.AttachmentBounds, session.Anchor, session.Spacing, session.Tolerance);
    }

    internal RectInt32 ConstrainMagneticResize(WidgetWindowBase source, RectInt32 bounds)
    {
        if (_magneticResizeSession is not { } session || session.Source != source ||
            !WidgetMagneticResizePolicy.RejectHeightGrowth(source.Config.Id, bounds, session.VisibleBounds)) return bounds;
        if (!session.RejectionFeedbackShown)
        {
            session.RejectionFeedbackShown = true;
            source.ShowResizeRejectedFeedback();
        }
        RectInt32 original = session.VisibleBounds[source.Config.Id];
        return new RectInt32(bounds.X, original.Y, bounds.Width, original.Height);
    }

    internal void UpdateMagneticResize(WidgetWindowBase source, RectInt32 bounds)
    {
        if (_magneticResizeSession is not { } session || session.Source != source) return;
        bounds = ConstrainMagneticResize(source, bounds);
        var targets = ArrangeMagneticResize(session, session.VisibleBounds, bounds, out _);
        foreach (var window in session.Windows.Where(w => w.MagneticCollapseInitialized))
            window.ApplyMagneticResizeBounds(targets[window.Config.Id]);
    }

    internal void CompleteMagneticResize(WidgetWindowBase source)
    {
        if (_magneticResizeSession is not { } session || session.Source != source) return;
        try
        {
            RectInt32 bounds = source.CoordinatedMoveBounds;
            UpdateMagneticResize(source, bounds);
            bounds = source.CoordinatedMoveBounds;
            RectInt32 expandedSource = source.ResolveMagneticExpandedResizeBounds(session.ExpandedBounds[source.Config.Id], bounds);
            var expanded = ArrangeMagneticResize(session, session.ExpandedBounds, expandedSource, out bool reattached);
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
