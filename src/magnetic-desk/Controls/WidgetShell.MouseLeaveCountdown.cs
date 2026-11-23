using MagneticDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Numerics;

namespace MagneticDesk.Controls;

public sealed partial class WidgetShell
{
    private WidgetCapsuleStyle _capsuleStyle;
    private bool _countdownVisible;
    private double _countdownFraction = 1;
    private int _lastCountdownDisplayedSeconds = -1;
    private double _lastCountdownRingDash = double.NaN;
    private bool _countdownTitlePaddingDeferred;
    private TextBlock? _countdownSeconds;
    private Ellipse? _countdownRing;
    private SolidColorBrush? _capsuleAccent;
    private readonly List<Border> _countdownSegments = [];
    private RectangleClip? _capsuleClipGeometry;
    private (float Width, float Height, float Radius)? _lastCapsuleClip;
    private Vector2? _pendingSettledCapsuleClipSize;

    internal void SetCapsuleStyle(string? value)
    {
        var style = WidgetCapsuleAppearance.Normalize(value);
        if (_countdownSeconds is not null && style == _capsuleStyle) return;
        _capsuleStyle = style;
        RequestedTheme = style switch
        {
            WidgetCapsuleStyle.ClassicBlack => ElementTheme.Dark,
            WidgetCapsuleStyle.ClassicWhite => ElementTheme.Light,
            _ => ElementTheme.Default
        };
        MouseLeaveCountdownHost.Children.Clear();
        _countdownSegments.Clear();
        _countdownRing = null;
        var accent = new SolidColorBrush(style == WidgetCapsuleStyle.Segments
            ? ColorHelper.FromArgb(255, 133, 100, 194) : ColorHelper.FromArgb(255, 35, 139, 145));
        _capsuleAccent = accent;
        _countdownSeconds = new TextBlock { FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = accent };
        var badge = new Grid { Width = 34, Height = 34 };
        if (style == WidgetCapsuleStyle.Ring || WidgetCapsuleAppearance.IsClassic(style))
        {
            badge.Children.Add(new Ellipse { Width = 29, Height = 29, Stroke = accent, Opacity = .17, StrokeThickness = 2 });
            _countdownRing = new Ellipse { Width = 29, Height = 29, Stroke = accent, StrokeThickness = 2,
                RenderTransformOrigin = new Windows.Foundation.Point(.5, .5), RenderTransform = new RotateTransform { Angle = -90 } };
            badge.Children.Add(_countdownRing);
        }
        else if (style == WidgetCapsuleStyle.Fill)
            badge.Children.Add(new Border { Background = new SolidColorBrush(ColorHelper.FromArgb(45, 72, 170, 145)), CornerRadius = new CornerRadius(16), Height = 25 });
        else
        {
            badge.Width = 42;
            var segments = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) };
            for (int i = 0; i < 5; i++)
            {
                var segment = new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(2), Background = accent };
                _countdownSegments.Add(segment);
                segments.Children.Add(segment);
            }
            badge.Children.Add(segments);
            _countdownSeconds.Margin = new Thickness(0, 0, 0, 9);
        }
        badge.Children.Add(_countdownSeconds);
        MouseLeaveCountdownHost.Children.Add(badge);
        CountdownFill.Background = new SolidColorBrush(ColorHelper.FromArgb(65, 97, 192, 164));
        SizeChanged -= CapsuleCountdown_SizeChanged;
        SizeChanged += CapsuleCountdown_SizeChanged;
        ActualThemeChanged -= CapsuleCountdown_ThemeChanged;
        ActualThemeChanged += CapsuleCountdown_ThemeChanged;
        ApplyCapsuleSkin();
        SetMouseLeaveCountdown(_countdownVisible, _countdownFraction, force: true);
    }

    private void CapsuleCountdown_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _pendingSettledCapsuleClipSize = null;
        UpdateCountdownFill();
        UpdateCapsuleClip();
    }

    internal void RefreshCapsuleClip() => UpdateCapsuleClip();

    internal void ClearPendingSettledCapsuleClip()
    {
        if (_pendingSettledCapsuleClipSize is null) return;
        _pendingSettledCapsuleClipSize = null;
        UpdateCapsuleClip();
    }

    private void UpdateCapsuleClip(Vector2? finalSize = null)
    {
        if (_compositionBoundsProgress is not null) return;
        if (_capsuleClipGeometry is null)
        {
            var visual = ElementCompositionPreview.GetElementVisual(ShellRoot);
            _capsuleClipGeometry = visual.Compositor.CreateRectangleClip();
            _capsuleClipGeometry.Left = _capsuleClipGeometry.Top = 0;
            visual.Clip = _capsuleClipGeometry;
            BackgroundPlate.RegisterPropertyChangedCallback(Border.CornerRadiusProperty, (_, _) => UpdateCapsuleClip());
        }
        Vector2? effectiveSize = finalSize ?? _pendingSettledCapsuleClipSize;
        float width = Math.Max(0, effectiveSize?.X ?? (float)ActualWidth);
        float height = Math.Max(0, effectiveSize?.Y ?? (float)ActualHeight);
        float radius = (float)Math.Min(BackgroundPlate.CornerRadius.TopLeft, Math.Min(width, height) / 2);
        if (_lastCapsuleClip?.Width != width) _capsuleClipGeometry.Right = width;
        if (_lastCapsuleClip?.Height != height) _capsuleClipGeometry.Bottom = height;
        if (_lastCapsuleClip?.Radius != radius)
            _capsuleClipGeometry.TopLeftRadius = _capsuleClipGeometry.TopRightRadius =
                _capsuleClipGeometry.BottomLeftRadius = _capsuleClipGeometry.BottomRightRadius = new System.Numerics.Vector2(radius);
        _lastCapsuleClip = (width, height, radius);
    }

    private void CapsuleCountdown_ThemeChanged(FrameworkElement sender, object args) => ApplyCapsuleSkin();

    private void ApplyCapsuleSkin()
    {
        bool dark = ActualTheme == ElementTheme.Dark;
        if (_capsuleAccent is not null)
            _capsuleAccent.Color = _capsuleStyle == WidgetCapsuleStyle.Segments
                ? dark ? ColorHelper.FromArgb(255, 198, 171, 245) : ColorHelper.FromArgb(255, 133, 100, 194)
                : dark ? ColorHelper.FromArgb(255, 110, 222, 213) : ColorHelper.FromArgb(255, 35, 139, 145);
        if (_capsuleAccent is not null && WidgetCapsuleAppearance.IsClassic(_capsuleStyle))
            _capsuleAccent.Color = _capsuleStyle == WidgetCapsuleStyle.ClassicBlack ? Colors.White : Colors.Black;
        var color = _capsuleStyle switch
        {
            WidgetCapsuleStyle.ClassicBlack => ColorHelper.FromArgb(255, 24, 24, 26),
            WidgetCapsuleStyle.ClassicWhite => ColorHelper.FromArgb(255, 250, 250, 250),
            WidgetCapsuleStyle.Fill => dark ? ColorHelper.FromArgb(65, 59, 78, 70) : ColorHelper.FromArgb(110, 255, 248, 231),
            WidgetCapsuleStyle.Segments => dark ? ColorHelper.FromArgb(65, 71, 58, 97) : ColorHelper.FromArgb(100, 237, 227, 255),
            _ => dark ? ColorHelper.FromArgb(45, 55, 100, 116) : ColorHelper.FromArgb(85, 226, 246, 255)
        };
        if (CapsuleSkin.Background is SolidColorBrush existingBg)
        {
            if (existingBg.Color != color) existingBg.Color = color;
        }
        else
        {
            CapsuleSkin.Background = new SolidColorBrush(color);
        }
        var borderColor = ColorHelper.FromArgb(dark ? (byte)30 : (byte)120, 255, 255, 255);
        if (CapsuleSkin.BorderBrush is SolidColorBrush existingBorder)
        {
            if (existingBorder.Color != borderColor) existingBorder.Color = borderColor;
        }
        else
        {
            CapsuleSkin.BorderBrush = new SolidColorBrush(borderColor);
        }
        if (CapsuleSkin.BorderThickness.Left != 1) CapsuleSkin.BorderThickness = new Thickness(1);
        double radius = WidgetCapsuleAppearance.CornerRadius(_capsuleStyle, _isCollapsed);
        if (CapsuleSkin.CornerRadius.TopLeft != radius)
            CapsuleSkin.CornerRadius = CountdownFillTrack.CornerRadius = new CornerRadius(radius);
        if (BackgroundPlate.CornerRadius.TopLeft != radius)
            BackgroundPlate.CornerRadius = new CornerRadius(radius);
        if (_isCollapsed && _countdownTitlePaddingDeferred)
        {
            _countdownTitlePaddingDeferred = false;
            TitleBarGrid.Padding = new Thickness(14, 4, 12, 4);
        }
        UpdateCapsuleClip();
    }

    private void UpdateCountdownFill()
    {
        if (!_countdownVisible || _capsuleStyle != WidgetCapsuleStyle.Fill) return;
        double targetWidth = Math.Max(0, ActualWidth * _countdownFraction);
        if (double.IsNaN(CountdownFill.Width) || Math.Abs(CountdownFill.Width - targetWidth) >= 0.005)
            CountdownFill.Width = targetWidth;
        double targetHeight = _isCollapsed ? ActualHeight : Math.Max(40, TitleBarGrid.ActualHeight);
        if (double.IsNaN(CountdownFillTrack.Height) || Math.Abs(CountdownFillTrack.Height - targetHeight) >= 0.5)
            CountdownFillTrack.Height = targetHeight;
    }

    internal void HideMouseLeaveCountdownForCollapse()
    {
        bool wasVisible = _countdownVisible;
        _countdownVisible = false;
        _countdownFraction = 1;
        _lastCountdownDisplayedSeconds = -1;
        _lastCountdownRingDash = double.NaN;
        if (_countdownSeconds is null) return;
        if (MouseLeaveCountdownHost.Visibility != Visibility.Collapsed)
            MouseLeaveCountdownHost.Visibility = Visibility.Collapsed;
        if (CountdownFillTrack.Visibility != Visibility.Collapsed)
            CountdownFillTrack.Visibility = Visibility.Collapsed;
        CollapsedChromeLayer.Padding = new Thickness(8, 0, 6, 0);
        if (wasVisible) _countdownTitlePaddingDeferred = true;
    }

    internal void SetMouseLeaveCountdown(bool visible, double remainingFraction, bool force = false)
    {
        double fraction = Math.Clamp(remainingFraction, 0, 1);
        if (!force && !_countdownTitlePaddingDeferred && _countdownSeconds is not null &&
            _countdownVisible == visible && _countdownFraction == fraction) return;
        bool visibilityChanged = force || _countdownVisible != visible || _countdownTitlePaddingDeferred;
        _countdownVisible = visible;
        _countdownFraction = fraction;
        if (_countdownSeconds is null) { SetCapsuleStyle(null); return; }
        if (visibilityChanged)
        {
            _countdownTitlePaddingDeferred = false;
            MouseLeaveCountdownHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            CountdownFillTrack.Visibility = visible && _capsuleStyle == WidgetCapsuleStyle.Fill ? Visibility.Visible : Visibility.Collapsed;
            double reserve = visible ? 52 : 0;
            TitleBarGrid.Padding = new Thickness(14, 4, 12 + reserve, 4);
            CollapsedChromeLayer.Padding = new Thickness(8, 0, 6 + reserve, 0);
        }
        int seconds = WidgetCapsuleAppearance.RemainingSeconds(_countdownFraction);
        if (force || _lastCountdownDisplayedSeconds != seconds)
        {
            _lastCountdownDisplayedSeconds = seconds;
            _countdownSeconds.Text = $"{seconds}s";
            for (int i = 0; i < _countdownSegments.Count; i++) _countdownSegments[i].Opacity = i < seconds ? 1 : .15;
        }
        if (_countdownRing is not null && (visible || force))
        {
            double dash = Math.Round(_countdownFraction * 42.41, 2);
            if (force || double.IsNaN(_lastCountdownRingDash) || Math.Abs(dash - _lastCountdownRingDash) >= 0.05)
            {
                _lastCountdownRingDash = dash;
                _countdownRing.StrokeDashArray = new DoubleCollection { dash, 42.42 };
            }
        }
        UpdateCountdownFill();
    }
}
