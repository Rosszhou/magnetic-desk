using MagneticDesk.Views;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    private sealed record MagneticResizeSession(WidgetWindowBase Source, WidgetWindowBase[] Windows,
        Dictionary<string, RectInt32> VisibleBounds, Dictionary<string, RectInt32> ExpandedBounds,
        WidgetMagneticLayout VisibleLayout, WidgetMagneticLayout ExpandedLayout)
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
        var expanded = windows.ToDictionary(w => w.Config.Id, w => w.Config.MagneticExpandedPlacement?.ToBounds() ?? w.MagneticExpandedBounds);
        string? anchor = windows.Select(w => w.Config.MagneticAttachmentAnchor).FirstOrDefault(value => value is not null)
            ?? source.Config.PositionAnchor;
        var layout = WidgetMagneticLayout.Capture(expanded, preferredAnchor: anchor, resizingId: source.Config.Id)
            .WithAlignment(MagneticAttachmentBounds(windows), anchor);
        _magneticResizeSession = new(source, windows, visible, expanded,
            layout.WithSpacing(visible), layout.WithSpacing(expanded));
        foreach (var window in windows.Where(w => w != source)) window.BeginCoordinatedMoveParticipation(false);
    }

    private static IReadOnlyDictionary<string, RectInt32> ArrangeMagneticResize(WidgetMagneticLayout layout,
        Dictionary<string, RectInt32> original, string sourceId, RectInt32 sourceBounds)
    {
        var sizes = original.ToDictionary(p => p.Key, p => new SizeInt32(p.Value.Width, p.Value.Height));
        sizes[sourceId] = new(sourceBounds.Width, sourceBounds.Height);
        var union = WidgetCoordinatedMoveCalculator.GetUnion(original.Values);
        var targets = layout.Arrange(sizes, union.X, union.Y);
        var sourceTarget = targets[sourceId];
        int dx = sourceBounds.X - sourceTarget.X;
        int dy = sourceBounds.Y - sourceTarget.Y;
        return targets.ToDictionary(p => p.Key, p => new RectInt32(p.Value.X + dx, p.Value.Y + dy, p.Value.Width, p.Value.Height));
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
        var targets = ArrangeMagneticResize(session.VisibleLayout, session.VisibleBounds, source.Config.Id, bounds);
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
            var expanded = ArrangeMagneticResize(session.ExpandedLayout, session.ExpandedBounds, source.Config.Id, expandedSource);
            foreach (var window in session.Windows.Where(w => w.MagneticCollapseInitialized))
            {
                window.CommitMagneticResizeLayout(expanded[window.Config.Id]);
                foreach (var member in MagneticSurfaceConfigs(window.Config))
                {
                    member.MagneticExpandedPlacement = window.Config.MagneticExpandedPlacement;
                    member.MagneticCompactPlacement = window.Config.MagneticCompactPlacement;
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
