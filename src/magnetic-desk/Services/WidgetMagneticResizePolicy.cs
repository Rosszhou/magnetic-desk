using Windows.Graphics;

namespace MagneticDesk.Services;

internal static class WidgetMagneticResizePolicy
{
    internal static RectInt32 ConstrainGrowth(RectInt32 original, RectInt32 proposed,
        IReadOnlyList<RectInt32> obstacles, int spacing)
    {
        RectInt32 vacated = RemoveGrowth(original, proposed);
        var (left, right) = LimitAxis(vacated.X, vacated.X + vacated.Width,
            proposed.X, proposed.X + proposed.Width, vacated.Y, vacated.Y + vacated.Height,
            original.Y, original.Y + original.Height, horizontal: true);
        var (top, bottom) = LimitAxis(vacated.Y, vacated.Y + vacated.Height,
            proposed.Y, proposed.Y + proposed.Height, left, right,
            original.X, original.X + original.Width, horizontal: false);
        return new RectInt32(left, top, right - left, bottom - top);

        (int Start, int End) LimitAxis(int start, int end, int requestedStart, int requestedEnd,
            int crossStart, int crossEnd, int originalCrossStart, int originalCrossEnd, bool horizontal)
        {
            foreach (RectInt32 obstacle in obstacles)
            {
                int obstacleCrossStart = horizontal ? obstacle.Y : obstacle.X;
                int obstacleCrossEnd = obstacleCrossStart + (horizontal ? obstacle.Height : obstacle.Width);
                if (crossStart >= obstacleCrossEnd || obstacleCrossStart >= crossEnd) continue;

                // Retain a previously smaller spacing. Applying the full gap
                // to a newly overlapping projection could pull an untouched edge inward.
                bool alreadyFacing = originalCrossStart < obstacleCrossEnd && obstacleCrossStart < originalCrossEnd;
                int near = horizontal ? obstacle.X : obstacle.Y;
                int far = near + (horizontal ? obstacle.Width : obstacle.Height);
                if (requestedStart < start && near < start)
                {
                    int clearance = alreadyFacing ? Math.Clamp(start - far, 0, spacing) : spacing;
                    requestedStart = Math.Max(requestedStart, Math.Min(start, far + clearance));
                }
                if (requestedEnd > end && far > end)
                {
                    int clearance = alreadyFacing ? Math.Clamp(near - end, 0, spacing) : spacing;
                    requestedEnd = Math.Min(requestedEnd, Math.Max(end, near - clearance));
                }
            }
            return (requestedStart, requestedEnd);
        }
    }

    internal static RectInt32 RemoveGrowth(RectInt32 original, RectInt32 proposed)
    {
        int left = Math.Max(original.X, proposed.X);
        int top = Math.Max(original.Y, proposed.Y);
        int right = Math.Min(original.X + original.Width, proposed.X + proposed.Width);
        int bottom = Math.Min(original.Y + original.Height, proposed.Y + proposed.Height);
        return new RectInt32(left, top, right - left, bottom - top);
    }
}
