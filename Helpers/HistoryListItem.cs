using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

public sealed class HistoryListItem
{
    public bool IsHeader { get; init; }
    public string? HeaderText { get; init; }
    public CallRecord? Record { get; init; }

    public static IEnumerable<HistoryListItem> GroupByDate(IEnumerable<CallRecord> records)
    {
        string? currentHeader = null;
        foreach (var record in records)
        {
            var header = GetDateHeader(record);
            if (!string.Equals(header, currentHeader, StringComparison.Ordinal))
            {
                currentHeader = header;
                yield return new HistoryListItem { IsHeader = true, HeaderText = header };
            }

            yield return new HistoryListItem { IsHeader = false, Record = record };
        }
    }

    public static string GetDateHeader(CallRecord record) =>
        CallRecordAnalytics.GetDateHeader(record.CallDate);
}
