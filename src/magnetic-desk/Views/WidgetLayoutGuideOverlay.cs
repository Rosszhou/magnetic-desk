using MagneticDesk.Platform;
using MagneticDesk.Services;

namespace MagneticDesk.Views;

/// <summary>Small per-pixel-alpha native surfaces keep guides click-through and inexpensive to move.</summary>
internal sealed class WidgetLayoutGuideOverlay : IDisposable
{
    private readonly List<LayoutGuideSurface> _surfaces = [];

    internal void Show(IReadOnlyList<WidgetLayoutGuide> guides)
    {
        for (int i = 0; i < guides.Count; i++)
        {
            if (i == _surfaces.Count) _surfaces.Add(new LayoutGuideSurface());
            _surfaces[i].Show(guides[i].Bounds);
        }
        for (int i = guides.Count; i < _surfaces.Count; i++) _surfaces[i].Hide();
    }

    internal void Hide()
    {
        foreach (var surface in _surfaces) surface.Hide();
    }

    public void Dispose()
    {
        foreach (var surface in _surfaces) surface.Dispose();
        _surfaces.Clear();
    }

}
