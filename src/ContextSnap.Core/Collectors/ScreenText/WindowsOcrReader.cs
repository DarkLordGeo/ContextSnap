using System.Runtime.InteropServices.WindowsRuntime;
using ContextSnap.Platform;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ContextSnap.Collectors.ScreenText;

public sealed record OcrReadResult(IReadOnlyList<OcrLine> Lines, string? Warning);

public interface IOcrTextReader
{
    /// <summary>Null when OCR can run; otherwise a user-facing reason (for example, no OCR language installed).</summary>
    string? UnavailableReason { get; }

    /// <param name="region">Area to read, in physical screen pixels.</param>
    OcrReadResult Read(ScreenTextTarget target, PixelRect region, CancellationToken cancellationToken);
}

/// <summary>
/// On-device OCR using the Windows.Media.Ocr engine built into Windows 10/11 and the OCR languages
/// installed with Windows. Nothing is sent anywhere. The engine is created on first use only.
/// </summary>
public sealed class WindowsOcrReader : IOcrTextReader
{
    /// <summary>Windows OCR reads small UI text noticeably better when upscaled.</summary>
    private const double SmallTextScale = 2.0;

    private static readonly Lazy<(OcrEngine? Engine, string? Error)> Engine = new(CreateEngine, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly SemaphoreSlim Gate = new(1, 1); // Recognition is serialized; one engine is shared.

    public string? UnavailableReason => Engine.Value.Error;

    public OcrReadResult Read(ScreenTextTarget target, PixelRect region, CancellationToken cancellationToken)
    {
        var engine = Engine.Value.Engine;
        if (engine is null)
            return new OcrReadResult([], Engine.Value.Error);

        var scale = ChooseScale(region, ScreenPlacement.GetScale(target.Handle));
        using var image = WindowPixelCapture.Capture(target.Handle, region, allowScreenFallback: target.IsFocused, scale);
        if (image is null)
            return new OcrReadResult([], "The window could not be captured for OCR (it may block screen capture).");

        SoftwareBitmap? bitmap = null;
        Gate.Wait(cancellationToken);
        try
        {
            bitmap = SoftwareBitmap.CreateCopyFromBuffer(image.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
            var result = engine.RecognizeAsync(bitmap).AsTask(cancellationToken).GetAwaiter().GetResult();
            var lines = result.Lines.Select(line => ToLine(line, image.Scale, region)).ToArray();
            return new OcrReadResult(lines, null);
        }
        finally
        {
            Gate.Release();
            bitmap?.Dispose();
        }
    }

    /// <summary>Upscale only when the result stays within the engine's size limit and the screen is not already high-DPI.</summary>
    internal static double ChooseScale(PixelRect region, double dpiScale)
    {
        var max = OcrEngine.MaxImageDimension;
        if (dpiScale >= 1.5)
            return 1.0;
        var scale = SmallTextScale;
        if (region.Width * scale > max || region.Height * scale > max)
            scale = Math.Min(1.0, Math.Min((double)max / region.Width, (double)max / region.Height));
        return scale;
    }

    private static OcrLine ToLine(Windows.Media.Ocr.OcrLine line, double scale, PixelRect region)
    {
        double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
        foreach (var word in line.Words)
        {
            left = Math.Min(left, word.BoundingRect.X);
            top = Math.Min(top, word.BoundingRect.Y);
            right = Math.Max(right, word.BoundingRect.X + word.BoundingRect.Width);
            bottom = Math.Max(bottom, word.BoundingRect.Y + word.BoundingRect.Height);
        }

        if (line.Words.Count == 0)
            return new OcrLine(line.Text, default);

        // Convert back to screen pixels so OCR and UI Automation geometry are comparable.
        return new OcrLine(line.Text, new PixelRect(
            region.Left + (int)(left / scale), region.Top + (int)(top / scale),
            region.Left + (int)Math.Ceiling(right / scale), region.Top + (int)Math.Ceiling(bottom / scale)));
    }

    private static (OcrEngine? Engine, string? Error) CreateEngine()
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            return engine is null
                ? (null, "OCR is unavailable: no Windows OCR language is installed for your display languages (Settings → Time & language → Language & region).")
                : (engine, null);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TypeLoadException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return (null, $"OCR is unavailable on this system ({ex.GetType().Name}).");
        }
    }
}
