using System.Text.Json.Serialization;

namespace CallAnalog.Softphone.Models;

public sealed class CallRecord
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("call_id")]
    public long PbxCallId { get; set; }

    [JsonPropertyName("call_type")]
    public string CallType
    {
        get => _callType;
        set => _callType = value ?? string.Empty;
    }

    [JsonPropertyName("contact_name")]
    public string? ContactName { get; set; }

    [JsonPropertyName("caller_num")]
    public string CallerNumber
    {
        get => _callerNumber;
        set => _callerNumber = value ?? string.Empty;
    }

    [JsonPropertyName("destination")]
    public string Destination
    {
        get => _destination;
        set => _destination = value ?? string.Empty;
    }

    [JsonPropertyName("disposition")]
    public string Disposition
    {
        get => _disposition;
        set => _disposition = value ?? string.Empty;
    }

    [JsonPropertyName("call_date")]
    public string CallDate
    {
        get => _callDate;
        set => _callDate = value ?? string.Empty;
    }

    [JsonPropertyName("duration")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("tfn")]
    public string? Tfn { get; set; }

    private string _callType = string.Empty;
    private string _callerNumber = string.Empty;
    private string _destination = string.Empty;
    private string _disposition = string.Empty;
    private string _callDate = string.Empty;

    public bool IsOutbound => CallType.Equals("Outbound", StringComparison.OrdinalIgnoreCase);

    public string DialNumber => IsOutbound ? Destination : CallerNumber;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(ContactName) || ContactName == "Unknown"
            ? DialNumber
            : ContactName;

    public string RecentPrimaryName
    {
        get
        {
            var name = ContactName?.Trim();
            if (string.IsNullOrWhiteSpace(name)
                || name.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Anonymous", StringComparison.OrdinalIgnoreCase))
            {
                return DialNumber;
            }

            return name;
        }
    }

    public string RecentNumberLine =>
        string.Equals(RecentPrimaryName, DialNumber, StringComparison.Ordinal)
            ? string.Empty
            : DialNumber;

    public string RecentDurationLabel => CallRecordAnalytics.FormatClockDuration(DurationSeconds);

    public string RecentTimestampLabel => CallRecordAnalytics.FormatRecentTimestamp(CallDate);

    public string Subtitle => $"{DialNumber} · {Disposition}";

    public string Initial
    {
        get
        {
            var letter = DisplayName.FirstOrDefault(c => char.IsLetterOrDigit(c));
            return letter == default ? "?" : char.ToUpperInvariant(letter).ToString();
        }
    }

    public string DirectionGlyph => IsOutbound ? "↗" : "↙";

    public string DirectionIconKey =>
        IsMissed ? "IconCallMissed"
        : IsOutbound ? "IconCallMade"
        : "IconCallReceived";

    public bool IsMissed =>
        Disposition.Contains("NO ANSWER", StringComparison.OrdinalIgnoreCase)
        || Disposition.Contains("MISSED", StringComparison.OrdinalIgnoreCase)
        || Disposition.Contains("BUSY", StringComparison.OrdinalIgnoreCase);

    public bool IsAnswered =>
        Disposition.Contains("ANSWER", StringComparison.OrdinalIgnoreCase)
        && !IsMissed;

    public bool IsCancelled =>
        Disposition.Contains("CANCEL", StringComparison.OrdinalIgnoreCase);

    /// <summary>Answered, Missed, or Cancel — for history row styling by disposition.</summary>
    public string HistoryTone
    {
        get
        {
            if (IsCancelled)
            {
                return "Cancel";
            }

            if (IsAnswered)
            {
                return "Answered";
            }

            if (IsMissed)
            {
                return "Missed";
            }

            return IsOutbound ? "Outbound" : "Inbound";
        }
    }
}
