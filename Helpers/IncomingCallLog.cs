using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Helpers;

internal static class IncomingCallLog
{
    public static void Marker(string marker, string? detail = null)
    {
        var extra = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        App.SipLog.Info(
            SipLogTag.Inbound,
            $"INCOMING: {marker} tid={Environment.CurrentManagedThreadId}{extra}");
    }
}
