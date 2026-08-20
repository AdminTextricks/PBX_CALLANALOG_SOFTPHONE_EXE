namespace CallAnalog.Softphone.Helpers;

internal static class OfflineCachePolicy
{
    internal static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(24);

    internal static bool IsStale(DateTimeOffset savedUtc, DateTimeOffset? utcNow = null, TimeSpan? staleAfter = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        return now - savedUtc > (staleAfter ?? DefaultStaleAfter);
    }
}
