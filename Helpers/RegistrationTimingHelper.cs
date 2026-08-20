namespace CallAnalog.Softphone.Helpers;

internal static class RegistrationTimingHelper
{
    internal const int MinRegistrationExpirySeconds = 60;
    internal const int MinKeepAliveSeconds = 5;

    internal static int ClampRegistrationExpiry(int seconds) =>
        Math.Max(MinRegistrationExpirySeconds, seconds);

    internal static int ClampKeepAliveSeconds(int seconds) =>
        Math.Max(MinKeepAliveSeconds, seconds);

    internal static bool ShouldScheduleReconnectAfterKeepAliveFailures(int consecutiveFailures, int threshold = 2) =>
        consecutiveFailures >= threshold;

    internal static bool ShouldReconnectForInactivity(
        DateTimeOffset? lastActivityUtc,
        int keepAliveSeconds,
        DateTimeOffset? utcNow = null)
    {
        if (lastActivityUtc is null)
        {
            return false;
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        var intervalSeconds = ClampKeepAliveSeconds(keepAliveSeconds);
        return now - lastActivityUtc.Value > TimeSpan.FromSeconds(intervalSeconds * 3);
    }

    internal static int GetReconnectDelaySeconds(int reconnectAttempt, string reason)
    {
        var isConnectionAborted = reason.Contains("ConnectionAborted", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("connection aborted", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("connection reset", StringComparison.OrdinalIgnoreCase);
        return isConnectionAborted ? 0 : Math.Min(60, 5 * reconnectAttempt);
    }
}
