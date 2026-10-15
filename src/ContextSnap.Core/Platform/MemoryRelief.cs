using System.Runtime;

namespace ContextSnap.Platform;

/// <summary>
/// ContextSnap is idle between captures, so the garbage collector otherwise has no reason to run and a
/// capture's temporary memory (UI Automation COM wrappers that hold handles until finalized, OCR pixel
/// buffers on the large-object heap) would stay allocated until the next capture. One collection right
/// after a capture returns that memory to Windows.
/// </summary>
public static class MemoryRelief
{
    private static int _running;

    /// <summary>Blocking collection; call it off the UI thread (it takes a few tens of milliseconds).</summary>
    public static void ReleaseCaptureMemory()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return; // One release at a time is enough after rapid presses.

        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers(); // Releases the COM objects UI Automation left for finalization.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
