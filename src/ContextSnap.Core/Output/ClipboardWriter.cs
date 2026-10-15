using System.Runtime.InteropServices;
using ContextSnap.Platform;

namespace ContextSnap.Output;

/// <summary>
/// Writes Unicode text directly through the Win32 clipboard API (CF_UNICODETEXT). Retries while
/// another application holds the clipboard, and can mark the content as excluded from Windows
/// clipboard history and cloud clipboard sync.
/// </summary>
public sealed class Win32ClipboardWriter : IClipboardWriter
{
    private const int OpenAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(40);

    public ClipboardResult SetText(string text, bool excludeFromHistory)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.ReplaceLineEndings("\r\n");

        if (!TryOpenClipboard())
            return new ClipboardResult(false, "The clipboard is being used by another application. Try again in a moment.");

        try
        {
            if (!NativeMethods.EmptyClipboard())
                return Failure("clear the clipboard");

            var memory = AllocateUnicode(normalized);
            if (memory == 0)
                return Failure("allocate clipboard memory");

            if (NativeMethods.SetClipboardData(NativeMethods.CfUnicodeText, memory) == 0)
            {
                NativeMethods.GlobalFree(memory); // Ownership transfers only on success.
                return Failure("write to the clipboard");
            }

            if (excludeFromHistory)
            {
                // Documented opt-out formats; failure here is non-fatal because the text is already set.
                SetDwordFormat("CanIncludeInClipboardHistory", 0);
                SetDwordFormat("CanUploadToCloudClipboard", 0);
            }

            return ClipboardResult.Ok;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryOpenClipboard()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(0))
                return true;
            Thread.Sleep(RetryDelay);
        }

        return false;
    }

    private static nint AllocateUnicode(string text)
    {
        var bytes = (nuint)((text.Length + 1) * sizeof(char));
        var memory = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, bytes);
        if (memory == 0)
            return 0;

        var pointer = NativeMethods.GlobalLock(memory);
        if (pointer == 0)
        {
            NativeMethods.GlobalFree(memory);
            return 0;
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
        }
        finally
        {
            NativeMethods.GlobalUnlock(memory);
        }

        return memory;
    }

    private static void SetDwordFormat(string formatName, int value)
    {
        var format = NativeMethods.RegisterClipboardFormat(formatName);
        if (format == 0)
            return;

        var memory = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, sizeof(int));
        if (memory == 0)
            return;

        var pointer = NativeMethods.GlobalLock(memory);
        if (pointer == 0)
        {
            NativeMethods.GlobalFree(memory);
            return;
        }

        Marshal.WriteInt32(pointer, value);
        NativeMethods.GlobalUnlock(memory);
        if (NativeMethods.SetClipboardData(format, memory) == 0)
            NativeMethods.GlobalFree(memory);
    }

    private static ClipboardResult Failure(string action) =>
        new(false, $"Could not {action} (Windows error {Marshal.GetLastPInvokeError()}).");
}
