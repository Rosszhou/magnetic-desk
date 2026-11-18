namespace MagneticDesk.Views;

public sealed partial class ContentWidgetWindow
{
    internal void RefreshMagneticGroupChrome()
    {
        var manager = App.Current.WidgetManager;
        ContentWidgetShell.SetMagneticGroupState(manager?.HasMagneticGroup(_config) == true,
            manager?.IsMagneticLayoutLocked(_config) == true);
    }
}
