using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MagneticDesk.Platform;

internal sealed class LayoutGuideSurface : IDisposable
{
    private readonly IntPtr _handle;
    private (int Width, int Height)? _painted;

    internal LayoutGuideSurface()
    {
        uint extendedStyle = (uint)(Win32Helper.WS_EX_NOACTIVATE | Win32Helper.WS_EX_LAYERED |
            Win32Helper.WS_EX_TOOLWINDOW | Win32Helper.WS_EX_TRANSPARENT | Win32Helper.WS_EX_TOPMOST);
        const uint popupStyle = 0x80000000;
        _handle = CreateWindowExW(extendedStyle, "STATIC", "MagneticDesk Layout Guide",
            popupStyle, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal void Show(Windows.Graphics.RectInt32 bounds)
    {
        var paint = (bounds.Width, bounds.Height);
        if (_painted != paint)
        {
            Paint(bounds);
            _painted = paint;
        }
        Win32Helper.SetWindowPos(_handle, Win32Helper.HWND_TOPMOST,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_SHOWWINDOW);
    }

    private void Paint(Windows.Graphics.RectInt32 bounds)
    {
        using var bitmap = new Bitmap(Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height), PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(235, 37, 99, 235));
        }

        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr image = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            image = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = SelectObject(dc, image);
            var destination = new NativePoint(bounds.X, bounds.Y);
            var origin = new NativePoint(0, 0);
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(_handle, IntPtr.Zero, ref destination, ref size,
                dc, ref origin, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(dc, previous);
            if (image != IntPtr.Zero) DeleteObject(image);
            DeleteDC(dc);
        }
    }

    internal void Hide() => Win32Helper.ShowWindow(_handle, Win32Helper.SW_HIDE);
    public void Dispose() => DestroyWindow(_handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int width, int height) { public int Width = width; public int Height = height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDC, ref NativePoint destination,
        ref NativeSize size, IntPtr sourceDC, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
}
