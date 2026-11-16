using Windows.Graphics;

namespace MagneticDesk.Models;

public sealed class MagneticExpandedPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public RectInt32 ToBounds() => new(X, Y, Width, Height);

    public static MagneticExpandedPlacement FromBounds(RectInt32 bounds) => new()
    {
        X = bounds.X,
        Y = bounds.Y,
        Width = bounds.Width,
        Height = bounds.Height
    };
}
