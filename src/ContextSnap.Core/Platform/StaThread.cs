using System.Runtime.ExceptionServices;

namespace ContextSnap.Platform;

/// <summary>Runs a COM-dependent function on a dedicated single-threaded apartment thread with a timeout.</summary>
internal static class StaThread
{
    public static T Run<T>(Func<T> function, TimeSpan timeout)
    {
        T? result = default;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = function();
            }
            catch (Exception ex)
            {
                error = ex; // Rethrown on the calling thread below.
            }
        })
        {
            IsBackground = true,
            Name = "Snapctx STA worker",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(timeout))
            throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0} seconds.");

        if (error is not null)
            ExceptionDispatchInfo.Throw(error);

        return result!;
    }
}
