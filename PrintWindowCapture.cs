using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AgentCapture;

internal static class PrintWindowCapture
{
    public static CapturedPixels Capture(nint hwnd, uint flags)
    {
        var window = Windows.Read(hwnd);
        Windows.Validate(window, "printwindow");
        using var bitmap = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            nint hdc = graphics.GetHdc();
            try
            {
                // Synchronous: the supervisor terminates this worker if the target hangs.
                if (!Native.PrintWindow(hwnd, hdc, flags)) throw new CaptureError("printwindow_failed", "PrintWindow returned false.");
            }
            finally { graphics.ReleaseHdc(hdc); }
        }
        var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        var bytes = new byte[checked(window.Width * window.Height * 4)];
        try { for (int y = 0; y < window.Height; y++) Marshal.Copy(locked.Scan0 + y * locked.Stride, bytes, y * window.Width * 4, window.Width * 4); }
        finally { bitmap.UnlockBits(locked); }
        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        return new(window.Width, window.Height, bytes);
    }
}
