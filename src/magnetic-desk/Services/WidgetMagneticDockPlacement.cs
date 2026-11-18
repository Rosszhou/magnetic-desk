using MagneticDesk.Models;
using Windows.Graphics;

namespace MagneticDesk.Services;

internal static class WidgetMagneticDockPlacement
{
    internal static MagneticDockContact? Resolve(MagneticDockContact contact, RectInt32 source,
        RectInt32 target, IReadOnlyList<RectInt32> obstacles, IReadOnlyList<RectInt32> moving,
        int spacing, int minimumOverlap)
    {
        if (source.Width <= 0 || source.Height <= 0 || target.Width <= 0 || target.Height <= 0 || moving.Count == 0) return null;
        spacing = Math.Max(0, spacing);
        RectInt32 snapped = contact.SourceEdge switch
        {
            MagneticDockEdge.Left => new(target.X + target.Width + spacing, contact.DockedBounds.Y, source.Width, source.Height),
            MagneticDockEdge.Right => new(target.X - spacing - source.Width, contact.DockedBounds.Y, source.Width, source.Height),
            MagneticDockEdge.Top => new(contact.DockedBounds.X, target.Y + target.Height + spacing, source.Width, source.Height),
            _ => new(contact.DockedBounds.X, target.Y - spacing - source.Height, source.Width, source.Height)
        };
        contact = contact with { DockedBounds = snapped };
        bool vertical = contact.SourceEdge is MagneticDockEdge.Left or MagneticDockEdge.Right;
        int sourceLength = vertical ? source.Height : source.Width;
        int targetLength = vertical ? target.Height : target.Width;
        int overlap = Math.Max(1, minimumOverlap);
        if (overlap > Math.Min(sourceLength, targetLength)) return null;
        int targetStart = vertical ? target.Y : target.X;
        int minimum = targetStart + overlap - sourceLength;
        int maximum = targetStart + targetLength - overlap;
        int preferred = vertical ? contact.DockedBounds.Y : contact.DockedBounds.X;
        var positions = new HashSet<int> { Math.Clamp(preferred, minimum, maximum) };
        var occupied = obstacles.Append(target).Where(bounds => bounds.Width > 0 && bounds.Height > 0).Distinct().ToArray();
        int normalDelta = vertical ? contact.DockedBounds.X - source.X : contact.DockedBounds.Y - source.Y;
        foreach (RectInt32 member in moving)
        {
            int offset = vertical ? member.Y - source.Y : member.X - source.X;
            int length = vertical ? member.Height : member.Width;
            int normalStart = (vertical ? member.X : member.Y) + normalDelta;
            int normalLength = vertical ? member.Width : member.Height;
            foreach (RectInt32 obstacle in occupied)
            {
                int obstacleNormal = vertical ? obstacle.X : obstacle.Y;
                int obstacleNormalLength = vertical ? obstacle.Width : obstacle.Height;
                if (normalStart >= obstacleNormal + obstacleNormalLength + spacing ||
                    obstacleNormal >= normalStart + normalLength + spacing) continue;
                int obstacleStart = vertical ? obstacle.Y : obstacle.X;
                int obstacleLength = vertical ? obstacle.Height : obstacle.Width;
                positions.Add(Math.Clamp(obstacleStart - spacing - offset - length, minimum, maximum));
                positions.Add(Math.Clamp(obstacleStart + obstacleLength + spacing - offset, minimum, maximum));
            }
        }
        foreach (int position in positions.OrderBy(value => Math.Abs((long)value - preferred)).ThenBy(value => value))
        {
            RectInt32 bounds = contact.DockedBounds;
            if (vertical) bounds.Y = position;
            else bounds.X = position;
            if (!Fits(source, bounds, occupied, moving, spacing)) continue;
            int start = Math.Max(position, targetStart);
            int end = Math.Min(position + sourceLength, targetStart + targetLength);
            return contact with { DockedBounds = bounds, ContactCoordinate = start + (end - start) / 2 };
        }
        return null;
    }

    internal static MagneticDockContact? FindBeside(RectInt32 source, RectInt32 target,
        IReadOnlyList<RectInt32> obstacles, IReadOnlyList<RectInt32> moving, int spacing, int minimumOverlap)
    {
        spacing = Math.Max(0, spacing);
        MagneticDockContact[] contacts =
        [
            new(MagneticDockEdge.Left, MagneticDockEdge.Right, 0, 0,
                new(target.X + target.Width + spacing, target.Y, source.Width, source.Height)),
            new(MagneticDockEdge.Right, MagneticDockEdge.Left, 0, 0,
                new(target.X - source.Width - spacing, target.Y, source.Width, source.Height)),
            new(MagneticDockEdge.Top, MagneticDockEdge.Bottom, 0, 0,
                new(target.X, target.Y + target.Height + spacing, source.Width, source.Height)),
            new(MagneticDockEdge.Bottom, MagneticDockEdge.Top, 0, 0,
                new(target.X, target.Y - source.Height - spacing, source.Width, source.Height))
        ];
        return contacts.Select(contact => Resolve(contact, source, target, obstacles, moving, spacing, minimumOverlap))
            .OfType<MagneticDockContact>()
            .OrderBy(contact => Math.Pow((double)contact.DockedBounds.X - source.X, 2) +
                Math.Pow((double)contact.DockedBounds.Y - source.Y, 2))
            .Select(contact => (MagneticDockContact?)contact)
            .FirstOrDefault();
    }

    private static bool Fits(RectInt32 source, RectInt32 destination,
        IReadOnlyList<RectInt32> obstacles, IReadOnlyList<RectInt32> moving, int spacing)
    {
        int dx = destination.X - source.X;
        int dy = destination.Y - source.Y;
        foreach (RectInt32 member in moving)
        foreach (RectInt32 obstacle in obstacles)
        {
            if (member.X + dx < obstacle.X + obstacle.Width + spacing &&
                obstacle.X < member.X + dx + member.Width + spacing &&
                member.Y + dy < obstacle.Y + obstacle.Height + spacing &&
                obstacle.Y < member.Y + dy + member.Height + spacing) return false;
        }
        return true;
    }
}
