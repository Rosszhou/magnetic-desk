using System.Numerics;
using MagneticDesk.Models;
using MagneticDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace MagneticDesk.Controls;

public sealed partial class WidgetShell
{
    private CompositionPropertySet? _compositionBoundsProgress;
    private ScalarKeyFrameAnimation? _compositionBoundsAnimation;
    private ShapeVisual? _compositionBoundsBackground;
    private CompositionRoundedRectangleGeometry? _compositionBoundsGeometry;
    private CompositionRoundedRectangleGeometry? _compositionBoundsClipGeometry;
    private CompositionGeometricClip? _compositionBoundsClip;
    private (double Width, double Height, HorizontalAlignment Horizontal, VerticalAlignment Vertical) _compositionBoundsLayout;
    private Vector2 _compositionBoundsTargetSize;
    private bool _compositionBoundsCollapsing;
    private bool _compositionBoundsAnchorsRight;
    private bool _compositionBoundsAnchorsBottom;
    private bool _isDestinationSizeLocked;
    internal bool SupportsCompositionBounds => _compactPresentation?.UseFullBleedBackground != true;

    internal CompositionPropertySet PrepareCompositionBounds(
        Vector2 fromSize, Vector2 toSize, float fromRadius, float toRadius,
        WidgetCompactExpansionAnchor anchor, bool collapsed, WidgetCompactTransitionVisualProfile profile)
    {
        DisposeCompositionBoundsBackground();
        UpdateCapsuleClip();
        StopCompactCompositionTransitionAnimations(force: true);
        _compositionBoundsLayout = (Width, Height, HorizontalAlignment, VerticalAlignment);
        _compositionBoundsTargetSize = toSize;
        _compositionBoundsCollapsing = collapsed;
        _compositionBoundsAnchorsRight = anchor is WidgetCompactExpansionAnchor.RightTop or WidgetCompactExpansionAnchor.RightBottom;
        _compositionBoundsAnchorsBottom = anchor is WidgetCompactExpansionAnchor.LeftBottom or WidgetCompactExpansionAnchor.RightBottom;
        Width = Math.Max(fromSize.X, toSize.X);
        Height = Math.Max(fromSize.Y, toSize.Y);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Vector2 compactSize = collapsed ? toSize : fromSize;
        CollapsedChromeLayer.Width = compactSize.X;
        CollapsedChromeLayer.Height = compactSize.Y;
        CollapsedChromeLayer.HorizontalAlignment = HorizontalAlignment.Left;
        CollapsedChromeLayer.VerticalAlignment = VerticalAlignment.Top;
        CompactIdentityHost.Opacity = CompactTextContainer.Opacity = CompactBadge.Opacity =
            CompactLiveIndicatorHost.Opacity = collapsed ? 0 : 1;

        var compositor = _capsuleClipGeometry!.Compositor;
        // A prepared transition can be cancelled before its first frame.
        // Initialize every translation property before exposing its cleanup state.
        foreach (var element in new FrameworkElement[] { TitleBarGrid, ShellContentPresenter })
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            ElementCompositionPreview.GetElementVisual(element).Properties.InsertVector3("Translation", Vector3.Zero);
        }
        var state = compositor.CreatePropertySet();
        _compositionBoundsProgress = state;
        state.InsertScalar("Progress", 0);
        state.InsertVector2("From", fromSize);
        state.InsertVector2("To", toSize);
        state.InsertScalar("FromRadius", fromRadius);
        state.InsertScalar("ToRadius", toRadius);
        state.InsertVector2("ContentSize", new Vector2((float)Width, (float)Height));
        string width = "(state.From.X + (state.To.X - state.From.X) * state.Progress)";
        string height = "(state.From.Y + (state.To.Y - state.From.Y) * state.Progress)";
        string radius = $"Min(state.FromRadius + (state.ToRadius - state.FromRadius) * state.Progress, Min({width}, {height}) / 2)";
        void Animate(CompositionObject target, string property, string expression)
        {
            var animation = compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("state", state);
            target.StartAnimation(property, animation);
            animation.Dispose();
        }
        var clipGeometry = compositor.CreateRoundedRectangleGeometry();
        _compositionBoundsClipGeometry = clipGeometry;
        Animate(clipGeometry, "Size", $"Vector2({width}, {height})");
        Animate(clipGeometry, "CornerRadius", $"Vector2({radius}, {radius})");
        _compositionBoundsClip = compositor.CreateGeometricClip(clipGeometry);
        ElementCompositionPreview.GetElementVisual(ShellRoot).Clip = _compositionBoundsClip;

        if (fromSize != toSize)
        {
            var background = compositor.CreateShapeVisual();
            _compositionBoundsBackground = background;
            background.Size = new Vector2((float)Width, (float)Height);
            var geometry = compositor.CreateRoundedRectangleGeometry();
            _compositionBoundsGeometry = geometry;
            float border = (float)Math.Max(BackgroundPlate.BorderThickness.Left, CapsuleSkin.BorderThickness.Left);
            geometry.Offset = new Vector2(border / 2);
            Animate(geometry, "Size", $"Vector2(Max(1, {width} - {border.ToString(System.Globalization.CultureInfo.InvariantCulture)}), Max(1, {height} - {border.ToString(System.Globalization.CultureInfo.InvariantCulture)}))");
            Animate(geometry, "CornerRadius", $"Vector2({radius}, {radius})");
            foreach (var brush in new[] { BackgroundPlate.Background, CapsuleSkin.Background })
            {
                var shape = compositor.CreateSpriteShape(geometry);
                shape.FillBrush = compositor.CreateColorBrush((brush as SolidColorBrush)?.Color ?? Colors.Transparent);
                background.Shapes.Add(shape);
            }
            foreach (var plate in new[] { BackgroundPlate, CapsuleSkin })
            {
                var outline = compositor.CreateSpriteShape(geometry);
                outline.StrokeBrush = compositor.CreateColorBrush((plate.BorderBrush as SolidColorBrush)?.Color ?? Colors.Transparent);
                outline.StrokeThickness = (float)plate.BorderThickness.Left;
                background.Shapes.Add(outline);
            }
            ElementCompositionPreview.SetElementChildVisual(CompositionBackgroundHost, background);
            BackgroundPlate.Opacity = 0;
            CompositionBackgroundHost.Visibility = Visibility.Visible;
        }

        var progress = compositor.CreateScalarKeyFrameAnimation();
        _compositionBoundsAnimation = progress;
        progress.Duration = TimeSpan.FromMilliseconds(profile.DurationMilliseconds);
        if (profile.EasingPower == 3)
        {
            // CubicBezier(1/3, 1, 2/3, 1) is mathematically identical to 1 - (1 - t)^3
            // and evaluates analytically at native display refresh rate on DirectComposition.
            using var cubicOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(1f / 3f, 1f),
                new Vector2(2f / 3f, 1f));
            progress.InsertKeyFrame(0f, 0f);
            progress.InsertKeyFrame(1f, 1f, cubicOut);
        }
        else
        {
            using var linear = compositor.CreateLinearEasingFunction();
            for (int sample = 0; sample <= 48; sample++)
                progress.InsertKeyFrame(sample / 48f, (float)profile.EaseProgress(sample / 48d), linear);
        }

        // Pre-bind only the expressions that actually change during this direction so
        // StartCompositionBounds only starts the single Progress clock on the prepared frame.
        if (_isCompactTransitionActive)
        {
            string Smooth(string value) => $"({value} * {value} * (3 - 2 * {value}))";
            void Opacity(FrameworkElement element, string expression)
            {
                using var animation = compositor.CreateExpressionAnimation(expression);
                animation.SetReferenceParameter("state", state);
                ElementCompositionPreview.GetElementVisual(element).StartAnimation("Opacity", animation);
            }
            if (_compositionBoundsCollapsing)
            {
                string live = $"1 - {Smooth("Clamp((state.Progress - 0.46) / 0.22, 0, 1)")}";
                string identity = Smooth("Clamp((state.Progress - 0.60) / 0.16, 0, 1)");
                string text = Smooth("Clamp((state.Progress - 0.72) / 0.24, 0, 1)");
                Opacity(TitleBarGrid, live);
                Opacity(ShellContentPresenter, live);
                Opacity(CompactIdentityHost, identity);
                Opacity(CompactTextContainer, text);
                Opacity(CompactBadge, text);
                Opacity(CompactLiveIndicatorHost, text);
            }
            else
            {
                string surface = $"1 - {Smooth($"Clamp(state.Progress / {_compactTransitionProfile.BodyRevealStart.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 0, 1)")}";
                Opacity(CollapsedChromeLayer, surface);
            }
            _isCompactCompositionTransitionActive = true;
        }
        if (_compositionBoundsAnchorsRight || _compositionBoundsAnchorsBottom)
        {
            // Keep the frozen live tree attached to the visible edge, including
            // right/bottom anchored transitions, without re-running XAML layout.
            string x = _compositionBoundsAnchorsRight ? "state.From.X + (state.To.X - state.From.X) * state.Progress - state.ContentSize.X" : "0";
            string y = _compositionBoundsAnchorsBottom ? "state.From.Y + (state.To.Y - state.From.Y) * state.Progress - state.ContentSize.Y" : "0";
            foreach (var element in new FrameworkElement[] { TitleBarGrid, ShellContentPresenter })
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                using var translation = compositor.CreateExpressionAnimation($"Vector3({x}, {y}, 0)");
                translation.SetReferenceParameter("state", state);
                visual.Properties.StartAnimation("Translation", translation);
            }
        }
        return state;
    }

    internal void StartCompositionBounds()
    {
        var state = _compositionBoundsProgress!;
        state.StartAnimation("Progress", _compositionBoundsAnimation!);
    }

    internal void SetFinalCompositionBoundsFrame()
    {
        if (_compositionBoundsProgress is not { } state) return;
        state.StopAnimation("Progress");
        state.InsertScalar("Progress", 1);
    }

    internal void EndCompositionBounds(bool useDestinationSize, bool keepDestinationSizeLocked = false)
    {
        if (_compositionBoundsProgress is not { } state) return;
        _compositionBoundsProgress = null;
        _lastCapsuleClip = null;
        // Apply the destination capsule/expanded corner radius BEFORE swapping out the
        // composition shape and lock the clip size until post-collapse layout settles,
        // preventing a 1-frame rectangular/square-corner capsule flash.
        if (_countdownSeconds is not null)
        {
            double settledRadius = WidgetCapsuleAppearance.CornerRadius(_capsuleStyle, _compositionBoundsCollapsing);
            if (CapsuleSkin.CornerRadius.TopLeft != settledRadius)
                CapsuleSkin.CornerRadius = CountdownFillTrack.CornerRadius = new CornerRadius(settledRadius);
            if (BackgroundPlate.CornerRadius.TopLeft != settledRadius)
                BackgroundPlate.CornerRadius = new CornerRadius(settledRadius);
        }
        _pendingSettledCapsuleClipSize = useDestinationSize ? _compositionBoundsTargetSize : null;
        UpdateCapsuleClip(useDestinationSize ? _compositionBoundsTargetSize : null);
        // The former compact clip may still have the capsule's height. Bring
        // the normal clip and background up to date before exposing either.
        ElementCompositionPreview.GetElementVisual(ShellRoot).Clip = _capsuleClipGeometry;
        BackgroundPlate.Opacity = 1;
        state.StopAnimation("Progress");
        _compositionBoundsClip?.Dispose();
        _compositionBoundsClip = null;
        _compositionBoundsClipGeometry?.Dispose();
        _compositionBoundsClipGeometry = null;
        foreach (var element in new FrameworkElement[] { TitleBarGrid, ShellContentPresenter })
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.Properties.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
        bool retainOpaqueShield = keepDestinationSizeLocked && useDestinationSize &&
            _compositionBoundsBackground is not null &&
            _compositionBoundsGeometry is not null &&
            ((CapsuleSkin.Background as SolidColorBrush)?.Color.A == 255 ||
             (BackgroundPlate.Background as SolidColorBrush)?.Color.A == 255);
        if (retainOpaqueShield)
        {
            // Keep the static GPU rounded-pill ShapeVisual directly behind the opaque
            // BackgroundPlate/CapsuleSkin until Phase 2 settles so the XAML raster handoff
            // can never expose a 1-frame transparent blink.
            float border = (float)Math.Max(BackgroundPlate.BorderThickness.Left, CapsuleSkin.BorderThickness.Left);
            float settledRadius = (float)Math.Min(
                WidgetCapsuleAppearance.CornerRadius(_capsuleStyle, _compositionBoundsCollapsing),
                Math.Min(_compositionBoundsTargetSize.X, _compositionBoundsTargetSize.Y) / 2f);
            _compositionBoundsGeometry!.StopAnimation("Size");
            _compositionBoundsGeometry.StopAnimation("CornerRadius");
            _compositionBoundsGeometry.Size = new Vector2(
                Math.Max(1f, _compositionBoundsTargetSize.X - border),
                Math.Max(1f, _compositionBoundsTargetSize.Y - border));
            _compositionBoundsGeometry.CornerRadius = new Vector2(settledRadius, settledRadius);
        }
        else
        {
            DisposeCompositionBoundsBackground();
        }
        _compositionBoundsAnimation?.Dispose();
        _compositionBoundsAnimation = null;
        StopCompactCompositionTransitionAnimations();
        state.Dispose();
        if (keepDestinationSizeLocked && useDestinationSize)
        {
            // Keep the shell laid out at the exact compact capsule size while the
            // transparent host HWND waits 1+ VSync frames to shrink cleanly.
            Width = _compositionBoundsTargetSize.X;
            Height = _compositionBoundsTargetSize.Y;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            _isDestinationSizeLocked = true;
        }
        else
        {
            (Width, Height, HorizontalAlignment, VerticalAlignment) = _compositionBoundsLayout;
            _isDestinationSizeLocked = false;
        }
        CollapsedChromeLayer.Width = CollapsedChromeLayer.Height = double.NaN;
        CollapsedChromeLayer.HorizontalAlignment = HorizontalAlignment.Stretch;
        CollapsedChromeLayer.VerticalAlignment = VerticalAlignment.Stretch;
    }

    internal void ReleaseLockedDestinationSize(bool releaseShield = true)
    {
        if (releaseShield) DisposeCompositionBoundsBackground();
        if (_isDestinationSizeLocked)
        {
            _isDestinationSizeLocked = false;
            (Width, Height, HorizontalAlignment, VerticalAlignment) = _compositionBoundsLayout;
        }
        ClearPendingSettledCapsuleClip();
    }

    internal void DisposeCompositionBoundsBackground()
    {
        ElementCompositionPreview.SetElementChildVisual(CompositionBackgroundHost, null);
        if (_compositionBoundsBackground is { } background)
        {
            foreach (var shape in background.Shapes)
            {
                if (shape is CompositionSpriteShape sprite)
                {
                    sprite.FillBrush?.Dispose();
                    sprite.StrokeBrush?.Dispose();
                }
                shape.Dispose();
            }
            background.Shapes.Clear();
        }
        _compositionBoundsGeometry?.Dispose();
        _compositionBoundsGeometry = null;
        _compositionBoundsBackground?.Dispose();
        _compositionBoundsBackground = null;
        CompositionBackgroundHost.Visibility = Visibility.Collapsed;
    }
}
