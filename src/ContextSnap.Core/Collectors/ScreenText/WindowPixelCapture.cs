using System.Runtime.InteropServices;
using ContextSnap.Platform;

namespace ContextSnap.Collectors.ScreenText;

/// <summary>BGRA pixels of part of a window. Disposing zeroes the buffer so captured content does not linger in memory.</summary>
public sealed class CapturedImage(byte[] pixels, int width, int height, double scale) : IDisposable
{
    public byte[] Pixels { get; } = pixels;
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Image pixels per screen pixel (greater than 1 when upscaled for OCR).</summary>
    public double Scale { get; } = scale;

    public void Dispose() => Array.Clear(Pixels);
}

/// <summary>
/// Captures a window's pixels with GDI. <c>PrintWindow</c> renders the window itself, so text from
/// windows overlapping it is never attributed to it; the screen copy is used only for the focused
/// window when the app cannot render itself (some GPU-rendered apps return a blank image).
/// Windows that opt out of capture (for example, DRM or WDA_EXCLUDEFROMCAPTURE) stay blank.
/// </summary>
public static class WindowPixelCapture
{
    private const uint PwRenderFullContent = 0x2;
    private const uint SrcCopy = 0x00CC0020;
    private const int HalfTone = 4;

    /// <param name="region">Area to capture, in physical screen pixels.</param>
    /// <param name="scale">Upscale factor applied to the captured image (1 = none).</param>
    public static CapturedImage? Capture(nint window, PixelRect region, bool allowScreenFallback, double scale)
    {
        if (!Gdi.GetWindowRect(window, out var windowRect))
            return null;

        var crop = Intersect(region, new PixelRect(windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom));
        if (crop.Width <= 0 || crop.Height <= 0)
            return null;

        var full = RenderWindow(window, windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top);
        if (full is not null && IsBlank(full.Value.Pixels))
        {
            Array.Clear(full.Value.Pixels);
            full = null;
        }

        var outputWidth = Math.Max(1, (int)Math.Round(crop.Width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(crop.Height * scale));

        if (full is { } rendered)
        {
            try
            {
                return Scale(rendered, crop.Left - windowRect.Left, crop.Top - windowRect.Top, crop.Width, crop.Height, outputWidth, outputHeight, scale);
            }
            finally
            {
                Array.Clear(rendered.Pixels);
            }
        }

        return allowScreenFallback ? CopyFromScreen(crop, outputWidth, outputHeight, scale) : null;
    }

    private readonly record struct Raw(byte[] Pixels, int Width, int Height);

    private static Raw? RenderWindow(nint window, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;

        return WithDib(width, height, (dc, bits) =>
        {
            if (!Gdi.PrintWindow(window, dc, PwRenderFullContent))
                return (Raw?)null;
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new Raw(pixels, width, height);
        });
    }

    private static CapturedImage? CopyFromScreen(PixelRect crop, int outputWidth, int outputHeight, double scale)
    {
        var screen = Gdi.GetDC(0);
        try
        {
            return WithDib(outputWidth, outputHeight, (dc, bits) =>
            {
                Gdi.SetStretchBltMode(dc, HalfTone);
                if (!Gdi.StretchBlt(dc, 0, 0, outputWidth, outputHeight, screen, crop.Left, crop.Top, crop.Width, crop.Height, SrcCopy))
                    return null;
                var pixels = new byte[outputWidth * outputHeight * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                return new CapturedImage(pixels, outputWidth, outputHeight, scale);
            });
        }
        finally
        {
            Gdi.ReleaseDC(0, screen);
        }
    }

    /// <summary>Crops and resamples a rendered window image with GDI's halftone filter.</summary>
    private static CapturedImage? Scale(Raw source, int x, int y, int width, int height, int outputWidth, int outputHeight, double scale)
    {
        return WithDib(source.Width, source.Height, (sourceDc, sourceBits) =>
        {
            Marshal.Copy(source.Pixels, 0, sourceBits, source.Pixels.Length);
            return WithDib(outputWidth, outputHeight, (targetDc, targetBits) =>
            {
                Gdi.SetStretchBltMode(targetDc, HalfTone);
                if (!Gdi.StretchBlt(targetDc, 0, 0, outputWidth, outputHeight, sourceDc, x, y, width, height, SrcCopy))
                    return null;
                var pixels = new byte[outputWidth * outputHeight * 4];
                Marshal.Copy(targetBits, pixels, 0, pixels.Length);
                return new CapturedImage(pixels, outputWidth, outputHeight, scale);
            });
        });
    }

    /// <summary>Creates a top-down 32-bit DIB in a memory DC, runs <paramref name="use"/>, and frees everything.</summary>
    private static T? WithDib<T>(int width, int height, Func<nint, nint, T?> use)
    {
        var dc = Gdi.CreateCompatibleDC(0);
        if (dc == 0)
            return default;

        var header = new Gdi.BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<Gdi.BitmapInfoHeader>(),
            Width = width,
            Height = -height, // Top-down rows.
            Planes = 1,
            BitCount = 32,
        };
        var bitmap = Gdi.CreateDIBSection(dc, ref header, 0, out var bits, 0, 0);
        if (bitmap == 0)
        {
            Gdi.DeleteDC(dc);
            return default;
        }

        var previous = Gdi.SelectObject(dc, bitmap);
        try
        {
            return use(dc, bits);
        }
        finally
        {
            Gdi.SelectObject(dc, previous);
            Gdi.DeleteObject(bitmap);
            Gdi.DeleteDC(dc);
        }
    }

    /// <summary>True when every sampled pixel has the same colour (a failed or protected render).</summary>
    internal static bool IsBlank(byte[] pixels)
    {
        if (pixels.Length < 4)
            return true;

        var first = BitConverter.ToInt32(pixels, 0) & 0x00FFFFFF;
        var step = Math.Max(4, pixels.Length / 4 / 2048 * 4);
        for (var i = 0; i + 3 < pixels.Length; i += step)
        {
            if ((BitConverter.ToInt32(pixels, i) & 0x00FFFFFF) != first)
                return false;
        }

        return true;
    }

    internal static PixelRect Intersect(PixelRect a, PixelRect b) => new(
        Math.Max(a.Left, b.Left), Math.Max(a.Top, b.Top), Math.Min(a.Right, b.Right), Math.Min(a.Bottom, b.Bottom));

    private static class Gdi
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint window, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(nint window, nint dc, uint flags);

        [DllImport("user32.dll")]
        public static extern nint GetDC(nint window);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(nint window, nint dc);

        [DllImport("gdi32.dll")]
        public static extern nint CreateCompatibleDC(nint dc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(nint dc);

        [DllImport("gdi32.dll")]
        public static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);

        [DllImport("gdi32.dll")]
        public static extern nint SelectObject(nint dc, nint obj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(nint obj);

        [DllImport("gdi32.dll")]
        public static extern int SetStretchBltMode(nint dc, int mode);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool StretchBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, int sw, int sh, uint rop);
    }
}
