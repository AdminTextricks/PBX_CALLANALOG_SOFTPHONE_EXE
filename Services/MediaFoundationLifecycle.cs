using NAudio.MediaFoundation;

namespace CallAnalog.Softphone.Services;

internal static class MediaFoundationLifecycle
{
    private static int _startupCount;

    public static void Startup()
    {
        if (Interlocked.Increment(ref _startupCount) == 1)
        {
            MediaFoundationApi.Startup();
        }
    }

    public static void Shutdown()
    {
        while (true)
        {
            var current = Volatile.Read(ref _startupCount);
            if (current <= 0)
            {
                return;
            }

            var next = current - 1;
            if (Interlocked.CompareExchange(ref _startupCount, next, current) != current)
            {
                continue;
            }

            if (next == 0)
            {
                MediaFoundationApi.Shutdown();
            }

            return;
        }
    }

    public static void ForceShutdown()
    {
        if (Interlocked.Exchange(ref _startupCount, 0) > 0)
        {
            try
            {
                MediaFoundationApi.Shutdown();
            }
            catch
            {
                // Best-effort on app exit.
            }
        }
    }
}
