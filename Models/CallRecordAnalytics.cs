using System.Globalization;

namespace CallAnalog.Softphone.Models;

public static class CallRecordAnalytics
{
    public static bool IsAttended(CallRecord call) =>
        call.Disposition.Contains("ANSWER", StringComparison.OrdinalIgnoreCase);

    public static bool IsMissed(CallRecord call) =>
        call.Disposition.Contains("NO ANSWER", StringComparison.OrdinalIgnoreCase)
        || call.Disposition.Contains("MISSED", StringComparison.OrdinalIgnoreCase)
        || call.Disposition.Contains("BUSY", StringComparison.OrdinalIgnoreCase);

    public static bool IsToday(CallRecord call) => IsToday(call.CallDate);

    public static bool IsToday(string callDate) =>
        TryParseCallDate(callDate, out var parsed) && parsed.Date == DateTime.Today;

    public static bool TryParseCallDate(string callDate, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(callDate))
        {
            return false;
        }

        if (callDate.StartsWith("Today", StringComparison.OrdinalIgnoreCase))
        {
            date = DateTime.Today;
            return true;
        }

        if (callDate.StartsWith("Yesterday", StringComparison.OrdinalIgnoreCase))
        {
            date = DateTime.Today.AddDays(-1);
            return true;
        }

        if (DateTime.TryParse(
                callDate,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            date = parsed.Date;
            return true;
        }

        if (DateTime.TryParse(
                callDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsed))
        {
            date = parsed.Date;
            return true;
        }

        if (DateTime.TryParseExact(
                callDate,
                ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsed))
        {
            date = parsed.Date;
            return true;
        }

        return false;
    }

    public static string GetDateHeader(string callDate)
    {
        if (!TryParseCallDate(callDate, out var date))
        {
            return "Earlier";
        }

        var today = DateTime.Today;
        if (date == today)
        {
            return "Today";
        }

        if (date == today.AddDays(-1))
        {
            return "Yesterday";
        }

        return date.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
    }

    public static string FormatAverageHandleTime(IEnumerable<CallRecord> calls)
    {
        var durations = calls
            .Where(c => IsAttended(c) && c.DurationSeconds > 0)
            .Select(c => c.DurationSeconds)
            .ToList();

        if (durations.Count == 0)
        {
            return "—";
        }

        return FormatDuration((int)Math.Round(durations.Average()));
    }

    public static string FormatDuration(int totalSeconds)
    {
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0 ? $"{minutes}m {seconds}s" : $"{seconds}s";
    }

    public static bool MatchesFilter(CallRecord call, CallHistoryFilter filter) =>
        filter switch
        {
            CallHistoryFilter.Answered => IsAttended(call),
            CallHistoryFilter.Missed => IsMissed(call),
            _ => true
        };

    public static string GetFilterTitle(CallHistoryFilter filter) =>
        filter switch
        {
            CallHistoryFilter.Answered => "Answered Calls",
            CallHistoryFilter.Missed => "Missed Calls",
            _ => "Call History"
        };
}
