using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

internal static class CallHistoryFilterHelper
{
    internal static bool IsMissedDisposition(string? disposition) =>
        disposition?.Equals("NO ANSWER", StringComparison.OrdinalIgnoreCase) == true
        || disposition?.Equals("BUSY", StringComparison.OrdinalIgnoreCase) == true;

    internal static IEnumerable<CallRecord> FilterMissed(IEnumerable<CallRecord> records) =>
        records.Where(record => IsMissedDisposition(record.Disposition));
}
