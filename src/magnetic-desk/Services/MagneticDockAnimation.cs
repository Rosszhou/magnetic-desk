using MagneticDesk.Models;

namespace MagneticDesk.Services;

internal readonly record struct MagneticAnimationFrame(
    double Attraction, double ScaleX, double ScaleY, double Rotation,
    double RingPhase, double CompletionOpacity, bool ShowMagnet);

internal readonly record struct MagneticParticleFrame(double X, double Y, double Size, double Opacity);

internal static class MagneticDockAnimation
{
    internal static MagneticAnimationStyle Normalize(string? value) =>
        Enum.TryParse(value, true, out MagneticAnimationStyle style) && Enum.IsDefined(style)
            ? style : MagneticAnimationStyle.Classic;

    internal static string CountText(double progress, bool completed) =>
        completed ? "✓" : Math.Max(1, (int)Math.Ceiling((1 - Progress(progress)) * 3)).ToString();

    internal static MagneticAnimationFrame Calculate(MagneticAnimationStyle style, double progress, bool completed, double time)
    {
        progress = Progress(progress);
        time = Time(time);
        double wave = Math.Sin(time * Math.PI * 4);
        double settle = Math.Exp(-time * 5);
        double bounce = settle * Math.Sin(time * Math.PI * 7);
        double attraction = completed ? 18 + 2 * bounce : 2 + 10 * progress + 2 * wave * (1 - progress);
        double scaleX = completed ? 1 + .14 * bounce : 1 + .035 * wave;
        double scaleY = scaleX;
        double rotation = 0;
        bool showMagnet = true;
        switch (style)
        {
            case MagneticAnimationStyle.Ripple:
                scaleX = scaleY = .78;
                break;
            case MagneticAnimationStyle.Particles:
                scaleX = scaleY = completed ? .85 + .15 * bounce : .8;
                rotation = completed ? 0 : 5 * wave * (1 - progress);
                break;
            case MagneticAnimationStyle.Elastic:
                scaleX = completed ? 1 + .28 * bounce : 1 - .12 * wave;
                scaleY = completed ? 1 - .2 * bounce : 1 + .22 * wave + .12 * progress;
                attraction = completed ? 18 + 4 * bounce : 2 + 10 * progress + 4 * wave * (1 - progress);
                break;
            case MagneticAnimationStyle.Orbit:
                showMagnet = false;
                attraction = completed ? 26 : 8 * progress;
                break;
        }
        return new(attraction, scaleX, scaleY, rotation, (time * .9) % 1,
            completed ? Math.Clamp(1 - time / .75, 0, 1) : 1, showMagnet);
    }

    internal static MagneticParticleFrame Particle(MagneticAnimationStyle style, int index, double progress, bool completed, double time)
    {
        progress = Progress(progress);
        time = Time(time);
        if (style == MagneticAnimationStyle.Orbit)
        {
            if (index > 1) return default;
            double radius = completed ? 3 * Math.Exp(-time * 8) : 23 * (1 - progress) + 3;
            double angle = time * Math.PI * 2 + index * Math.PI;
            return new(32 + radius * Math.Cos(angle), 32 + radius * Math.Sin(angle),
                completed ? 11 + 5 * Math.Exp(-time * 6) : 10, 1);
        }
        if (style != MagneticAnimationStyle.Particles) return default;
        double phase = (time * .85 + index / 6.0) % 1;
        double radius2 = completed ? 8 + time * 42 : 27 * (1 - phase);
        double angle2 = index * Math.PI / 3 + time * 1.4;
        double opacity = completed ? Math.Clamp(1 - time / .6, 0, 1) : Math.Sin(phase * Math.PI);
        return new(32 + radius2 * Math.Cos(angle2), 32 + radius2 * Math.Sin(angle2),
            completed ? 4 : 3 + 3 * phase, opacity);
    }

    private static double Progress(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double Time(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1e9) : 0;
}
