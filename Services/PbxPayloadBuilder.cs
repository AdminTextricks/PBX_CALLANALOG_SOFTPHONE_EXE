namespace CallAnalog.Softphone.Services;

internal static class PbxPayloadBuilder
{
    public static string BuildCallNote(string callId, string note, int? rating = null)
    {
        var callIdPart = long.TryParse(callId, out var numericCallId)
            ? numericCallId.ToString()
            : $"\"{Escape(callId)}\"";

        var ratingPart = rating is > 0 and <= 5
            ? $",\"rating\":{rating.Value}"
            : string.Empty;

        return $"{{\"call_id\":{callIdPart},\"note\":\"{Escape(note)}\"{ratingPart}}}";
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
