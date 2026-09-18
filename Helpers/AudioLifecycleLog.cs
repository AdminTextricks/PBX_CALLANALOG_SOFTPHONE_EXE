using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Diagnostics-only audio/media lifecycle lines for sip.log. Does not change control flow.
/// </summary>
internal static class AudioLifecycleLog
{
    public static void Write(string eventName, string? callId = null, string? detail = null)
    {
        try
        {
            var extra = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
            App.SipLog.Info(
                SipLogTag.Media,
                $"AUDIO-LC {eventName} tid={Environment.CurrentManagedThreadId} call={callId ?? "-"} {WinMmAudioOutputManager.DescribeForLog()}{extra}");
        }
        catch
        {
            // Diagnostics must never affect call audio.
        }
    }
}
