namespace MagneticDesk.Views;

public abstract partial class WidgetWindowBase
{
    private void InitializeSurfaceClip()
    {
        RootElement.SizeChanged += (_, _) => RefreshSurfaceClip();
        RootElement.Loaded += (_, _) => DispatcherQueue.TryEnqueue(RefreshSurfaceClip);
        RefreshSurfaceClip();
    }

    private void RefreshSurfaceClip() => WidgetShellControl.RefreshCapsuleClip();
}
