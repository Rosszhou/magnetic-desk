using MagneticDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Views;

public abstract partial class WidgetWindowBase
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<MenuFlyout, object> _trackedWidgetFlyouts = new();

    internal void RefreshFrontPinState()
    {
        WidgetLayerService.SetPinnedToFront(HWnd, Config.IsAlwaysOnTop);
        WidgetShellControl.SetPinnedToFront(Config.IsAlwaysOnTop);
        if (Config.IsAlwaysOnTop)
        {
            TopMostSafetyTimer?.Stop();
            IsAtDesktopLayer = false;
        }
    }

    private void WidgetShellControl_FrontPinRequested(object? sender, RoutedEventArgs e)
    {
        if (App.Current.WidgetManager is not { } manager) return;
        var menu = manager.CreateFrontPinMenu(Config);
        TrackWidgetFlyout(menu);
        menu.ShowAt(WidgetShellControl.FrontPinAnchor);
    }
    protected void TrackWidgetFlyout(MenuFlyout menu)
    {
        if (_trackedWidgetFlyouts.TryGetValue(menu, out _)) return;
        _trackedWidgetFlyouts.Add(menu, new object());
        IDisposable? interaction = null;
        FlyoutBridgeBandPromoter? promoter = null;
        menu.Opened += (_, _) =>
        {
            interaction ??= AcquireCompactInteraction("widget-menu");
            if (Config.IsAlwaysOnTop) promoter ??= FlyoutBridgeBandPromoter.TryBegin();
        };
        menu.Closed += (_, _) =>
        {
            promoter?.Dispose();
            promoter = null;
            interaction?.Dispose();
            interaction = null;
        };
    }

}
