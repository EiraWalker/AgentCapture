using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AgentCapture;

internal sealed record CapturedPixels(int Width, int Height, byte[] Bgra)
{
    public object Save(string path, string alphaMode)
    {
        if (Width <= 0 || Height <= 0 || (long)Width * Height > 40_000_000 || Bgra.Length != checked(Width * Height * 4))
            throw new CaptureError("invalid_frame", "Invalid pixel buffer.");
        bool black = true, uniform = true, transparent = true;
        for (int i = 0; i < Bgra.Length; i += 4)
        {
            black &= Bgra[i] == 0 && Bgra[i + 1] == 0 && Bgra[i + 2] == 0;
            uniform &= Bgra[i] == Bgra[0] && Bgra[i + 1] == Bgra[1] && Bgra[i + 2] == Bgra[2];
            transparent &= Bgra[i + 3] == 0;
        }
        if (black || transparent) throw new CaptureError("blank_frame", "Frame is entirely black or transparent. No image was published.");
        if (alphaMode == "premultiplied")
        {
            // PNG stores straight alpha, whereas the DWM surface uses premultiplied BGRA.
            for (int i = 0; i < Bgra.Length; i += 4)
            {
                int a = Bgra[i + 3];
                if (a is > 0 and < 255)
                    for (int c = 0; c < 3; c++) Bgra[i + c] = (byte)Math.Min(255, (Bgra[i + c] * 255 + a / 2) / a);
            }
        }
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var locked = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < Height; y++) Marshal.Copy(Bgra, y * Width * 4, locked.Scan0 + y * locked.Stride, Width * 4); }
        finally { bitmap.UnlockBits(locked); }
        bitmap.Save(path, ImageFormat.Png);
        using var file = File.OpenRead(path);
        return new { width = Width, height = Height, sha256 = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(),
            alphaMode = "straight", uniformRgb = uniform, contentVerified = false,
            warnings = uniform ? new[] { "uniform_frame", "freshness_not_verified" } : new[] { "freshness_not_verified" } };
    }
}
