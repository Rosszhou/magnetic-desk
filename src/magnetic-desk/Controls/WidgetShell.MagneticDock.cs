using MagneticDesk.Models;
using MagneticDesk.Helpers;
using MagneticDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace MagneticDesk.Controls;

public sealed partial class WidgetShell
{
    private Ellipse[]? _magneticDockParticles;

    public void SetMagneticDockFeedback(MagneticDockEdge edge, double offset, double progress,
        bool completed, double animationTime, string? animationStyle = null)
    {
        if (ShellRoot.ActualWidth <= 0 || ShellRoot.ActualHeight <= 0) return;
        MagneticAnimationStyle style = MagneticDockAnimation.Normalize(animationStyle);
        MagneticAnimationFrame frame = MagneticDockAnimation.Calculate(style, progress, completed, animationTime);
        bool vertical = edge is MagneticDockEdge.Left or MagneticDockEdge.Right;
        double left = vertical
            ? (edge == MagneticDockEdge.Left ? 0 : Math.Max(0, ShellRoot.ActualWidth - 64))
            : Math.Clamp(offset - 32, 0, Math.Max(0, ShellRoot.ActualWidth - 64));
        double top = vertical
            ? Math.Clamp(offset - 44, 0, Math.Max(0, ShellRoot.ActualHeight - 88))
            : (edge == MagneticDockEdge.Top ? 0 : Math.Max(0, ShellRoot.ActualHeight - 88));
        MagneticDockBadge.Margin = new Thickness(left, top, 0, 0);
        MagneticDockBadge.Visibility = Visibility.Visible;
        Canvas.SetTop(MagneticDockVisual, edge == MagneticDockEdge.Bottom ? 24 : 0);
        Canvas.SetTop(MagneticDockCounter, edge == MagneticDockEdge.Bottom ? 0 : 64);
        double angle = edge switch
        {
            MagneticDockEdge.Right => 90,
            MagneticDockEdge.Bottom => 180,
            MagneticDockEdge.Left => 270,
            _ => 0
        };
        double direction = edge is MagneticDockEdge.Left or MagneticDockEdge.Top ? -1 : 1;
        MagneticDockRotation.Angle = angle + frame.Rotation;
        MagneticDockFieldRotation.Angle = angle;
        MagneticDockAttraction.X = vertical ? direction * frame.Attraction : 0;
        MagneticDockAttraction.Y = vertical ? 0 : direction * frame.Attraction;
        double fieldTravel = style == MagneticAnimationStyle.Orbit ? frame.Attraction : frame.Attraction * .45;
        MagneticDockFieldAttraction.X = vertical ? direction * fieldTravel : 0;
        MagneticDockFieldAttraction.Y = vertical ? 0 : direction * fieldTravel;
        MagneticDockScale.ScaleX = frame.ScaleX;
        MagneticDockScale.ScaleY = frame.ScaleY;
        MagneticDockMagnet.Visibility = frame.ShowMagnet ? Visibility.Visible : Visibility.Collapsed;

        bool showRings = style == MagneticAnimationStyle.Ripple || completed;
        MagneticDockRingOuter.Visibility = MagneticDockRingInner.Visibility =
            showRings ? Visibility.Visible : Visibility.Collapsed;
        double phase = frame.RingPhase;
        SetMagneticRing(MagneticDockRingOuter, completed ? 18 + 27 * (1 - frame.CompletionOpacity) : 12 + 40 * phase,
            completed ? frame.CompletionOpacity : .75 * (1 - phase));
        double secondPhase = (phase + .5) % 1;
        SetMagneticRing(MagneticDockRingInner, completed ? 12 + 21 * (1 - frame.CompletionOpacity) : 12 + 40 * secondPhase,
            completed ? frame.CompletionOpacity * .7 : .6 * (1 - secondPhase));

        _magneticDockParticles ??= [MagneticDockParticle0, MagneticDockParticle1, MagneticDockParticle2,
            MagneticDockParticle3, MagneticDockParticle4, MagneticDockParticle5];
        for (int index = 0; index < _magneticDockParticles.Length; index++)
        {
            MagneticParticleFrame particle = MagneticDockAnimation.Particle(style, index, progress, completed, animationTime);
            Ellipse dot = _magneticDockParticles[index];
            dot.Width = dot.Height = Math.Max(.5, particle.Size);
            dot.Opacity = particle.Opacity;
            Canvas.SetLeft(dot, particle.X - particle.Size / 2);
            Canvas.SetTop(dot, particle.Y - particle.Size / 2);
        }

        string countText = MagneticDockAnimation.CountText(progress, completed);
        if (MagneticDockCount.Text != countText)
        {
            MagneticDockCount.Text = MagneticDockCountOutline.Text = countText;
            AutomationProperties.SetName(MagneticDockBadge, completed
                ? App.Current.LocalizationService.T("Magnetic.Attached")
                : App.Current.LocalizationService.Format("Magnetic.Countdown", countText));
        }
        MagneticDockCount.Foreground = SharedBrushCache.GetOrCreate(completed
            ? ColorHelper.FromArgb(255, 19, 156, 104) : ColorHelper.FromArgb(255, 22, 125, 204));
    }

    private static void SetMagneticRing(Ellipse ring, double diameter, double opacity)
    {
        ring.Width = ring.Height = diameter;
        ring.Opacity = opacity;
        Canvas.SetLeft(ring, 32 - diameter / 2);
        Canvas.SetTop(ring, 32 - diameter / 2);
    }

    public void HideMagneticDockFeedback()
    {
        MagneticDockBadge.Visibility = Visibility.Collapsed;
        MagneticDockCount.Text = MagneticDockCountOutline.Text = "";
        MagneticDockAttraction.X = MagneticDockAttraction.Y = 0;
        MagneticDockFieldAttraction.X = MagneticDockFieldAttraction.Y = 0;
        MagneticDockScale.ScaleX = MagneticDockScale.ScaleY = 1;
    }
}
