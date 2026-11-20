using System.Runtime.InteropServices;
using Windows.Graphics;

namespace MagneticDesk.Platform;

public static partial class Win32Helper
{
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    internal static bool SetAnimationWindowClip(IntPtr window, RectInt32 bounds)
    {
        // The compositor draws the rounded edge. A simple native rectangle
        // clips input and avoids rebuilding a complex GDI region every tick.
        IntPtr region = CreateRectRgn(bounds.X, bounds.Y, bounds.X + bounds.Width + 1, bounds.Y + bounds.Height + 1);
        if (region == IntPtr.Zero) return false;
        if (SetWindowRgn(window, region, false) != 0) return true;
        // Ownership transfers to Windows only after a successful assignment.
        DeleteObject(region);
        return false;
    }

    internal static void ClearAnimationWindowClip(IntPtr window) => SetWindowRgn(window, IntPtr.Zero, false);
}
