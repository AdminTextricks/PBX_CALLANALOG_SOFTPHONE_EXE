using System.Globalization;

namespace CallAnalog.Softphone.Models;

public static partial class CallRecordAnalytics
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

        if (TryParseCallTimestamp(callDate, out var local))
        {
            date = local.Date;
            return true;
        }

        return false;
    }

    public static string FormatRecentTimestamp(string callDate)
    {
        if (!TryParseCallTimestamp(callDate, out var local))
        {
            return string.Empty;
        }

        var culture = CultureInfo.GetCultureInfo("en-US");
        var hasTime = local.TimeOfDay > TimeSpan.Zero
            || callDate.Contains(':', StringComparison.Ordinal)
            || callDate.Contains('T', StringComparison.OrdinalIgnoreCase)
            || callDate.Contains("AM", StringComparison.OrdinalIgnoreCase)
            || callDate.Contains("PM", StringComparison.OrdinalIgnoreCase);

        return hasTime
            ? local.ToString("dd MMM yyyy • h:mm tt", culture)
            : local.ToString("dd MMM yyyy", culture);
    }

    public static string FormatClockDuration(int totalSeconds)
    {
        var elapsed = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }

        return $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
    }

    public static bool TryParseCallTimestamp(string callDate, out DateTime localTime)
    {
        localTime = default;
        if (string.IsNullOrWhiteSpace(callDate)
            || callDate.StartsWith("Today", StringComparison.OrdinalIgnoreCase)
            || callDate.StartsWith("Yesterday", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var localZone = TimeZoneInfo.Local;

        if (HasExplicitTimeZone(callDate)
            && TryParseOffsetTimestamp(callDate, out var withOffset))
        {
            localTime = TimeZoneInfo.ConvertTime(withOffset, localZone).DateTime;
            return true;
        }

        if (!TryParseNaiveDateTime(callDate, out var naive))
        {
            return false;
        }

        if (naive.TimeOfDay == TimeSpan.Zero && LooksLikeDateOnly(callDate))
        {
            localTime = DateTime.SpecifyKind(naive.Date, DateTimeKind.Local);
            return true;
        }

        var unspecified = DateTime.SpecifyKind(naive, DateTimeKind.Unspecified);
        localTime = TimeZoneInfo.ConvertTime(unspecified, GetNaiveSourceTimeZone(), localZone);
        return true;
    }

    private static bool HasExplicitTimeZone(string callDate)
    {
        var value = callDate.Trim();
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Contains("GMT", StringComparison.OrdinalIgnoreCase)
            || value.Contains("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return OffsetSuffixRegex().IsMatch(value);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[+-]\d{2}:?\d{2}$")]
    private static partial System.Text.RegularExpressions.Regex OffsetSuffixRegex();

    private static bool TryParseOffsetTimestamp(string callDate, out DateTimeOffset timestamp)
    {
        return DateTimeOffset.TryParse(
                   callDate,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                   out timestamp)
            || DateTimeOffset.TryParse(
                   callDate,
                   CultureInfo.CurrentCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                   out timestamp);
    }

    private static bool TryParseNaiveDateTime(string callDate, out DateTime naive)
    {
        return DateTime.TryParseExact(
                   callDate.Trim(),
                   [
                       "yyyy-MM-dd HH:mm:ss",
                       "yyyy-MM-ddTHH:mm:ss",
                       "yyyy-MM-dd HH:mm:ss.FFFFFFF",
                       "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
                       "yyyy-MM-dd"
                   ],
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault,
                   out naive)
            || DateTime.TryParse(
                   callDate,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out naive)
            || DateTime.TryParse(
                   callDate,
                   CultureInfo.CurrentCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out naive);
    }

    private static bool LooksLikeDateOnly(string callDate)
    {
        var trimmed = callDate.Trim();
        return trimmed.Length <= 10 && !trimmed.Contains(':', StringComparison.Ordinal);
    }

    private static TimeZoneInfo GetNaiveSourceTimeZone()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
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

    public static CallRecord CreateLiveCall(
        string? sipCallId,
        string number,
        string? contactName,
        bool isOutbound,
        string disposition,
        int durationSeconds)
    {
        var hash = string.IsNullOrWhiteSpace(sipCallId)
            ? number.GetHashCode(StringComparison.Ordinal)
            : sipCallId.GetHashCode(StringComparison.Ordinal);
        var id = hash == 0 ? -1 : -Math.Abs(hash);
        return new CallRecord
        {
            Id = id,
            CallType = isOutbound ? "Outbound" : "Inbound",
            CallerNumber = isOutbound ? string.Empty : number,
            Destination = isOutbound ? number : string.Empty,
            ContactName = contactName,
            Disposition = disposition,
            CallDate = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            DurationSeconds = Math.Max(0, durationSeconds)
        };
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
