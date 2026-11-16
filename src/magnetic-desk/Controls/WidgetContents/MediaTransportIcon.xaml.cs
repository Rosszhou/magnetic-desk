using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Controls.WidgetContents;

public enum MediaTransportIconKind
{
    Previous,
    Play,
    Pause,
    Next
}

public sealed partial class MediaTransportIcon : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(MediaTransportIconKind),
        typeof(MediaTransportIcon),
        new PropertyMetadata(MediaTransportIconKind.Play, OnKindChanged));

    public MediaTransportIcon()
    {
        InitializeComponent();
        ApplyKind();
    }

    public MediaTransportIconKind Kind
    {
        get => (MediaTransportIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnKindChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is MediaTransportIcon icon)
        {
            icon.ApplyKind();
        }
    }

    private void ApplyKind()
    {
        PreviousSurface.Visibility = Kind == MediaTransportIconKind.Previous
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaySurface.Visibility = Kind == MediaTransportIconKind.Play
            ? Visibility.Visible
            : Visibility.Collapsed;
        PauseSurface.Visibility = Kind == MediaTransportIconKind.Pause
            ? Visibility.Visible
            : Visibility.Collapsed;
        NextSurface.Visibility = Kind == MediaTransportIconKind.Next
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
