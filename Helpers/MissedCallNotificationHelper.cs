using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

internal static class MissedCallNotificationHelper
{
    internal static string FormatCaller(IncomingCallEventArgs callInfo) =>
        string.IsNullOrWhiteSpace(callInfo.CallerName)
            ? callInfo.CallerNumber
            : callInfo.CallerName;

    internal static (string Title, string Body) FormatMissedCallNotification(IncomingCallEventArgs callInfo)
    {
        var caller = FormatCaller(callInfo);
        return ("Missed call", $"Call from {caller} while you were on another call.");
    }
}
