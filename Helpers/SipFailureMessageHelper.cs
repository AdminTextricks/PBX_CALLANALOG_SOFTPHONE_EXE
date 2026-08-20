using SIPSorcery.SIP;

namespace CallAnalog.Softphone.Helpers;

internal static class SipFailureMessageHelper
{
    internal static string FormatFailureMessage(string? errorMessage, SIPResponse? response)
    {
        if (response is null)
        {
            return string.IsNullOrWhiteSpace(errorMessage) ? "Call failed." : errorMessage;
        }

        return response.Status switch
        {
            SIPResponseStatusCodesEnum.BusyHere => "The line is busy.",
            SIPResponseStatusCodesEnum.Decline => "The call was declined.",
            SIPResponseStatusCodesEnum.NotFound => "The number was not found.",
            SIPResponseStatusCodesEnum.RequestTerminated => "The call was cancelled.",
            SIPResponseStatusCodesEnum.TemporarilyUnavailable => "The destination is unavailable.",
            SIPResponseStatusCodesEnum.Forbidden => "The call was forbidden.",
            _ => string.IsNullOrWhiteSpace(errorMessage)
                ? $"{response.ReasonPhrase} ({response.StatusCode})"
                : errorMessage
        };
    }
}
