using SIPSorcery.SIP;

namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Inbound OPTIONS handling. PBXs (OpenSIPS, Asterisk) send out-of-dialog OPTIONS as a
/// reachability probe; a UA that advertises OPTIONS in Allow must answer 200 OK.
/// </summary>
internal static class SipOptionsHelper
{
    internal const string AllowedMethods =
        "ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE";

    internal const string AcceptedContentTypes = "application/sdp";

    internal static bool ShouldAnswerWithOk(SIPRequest? request)
    {
        if (request is null || request.Method != SIPMethodsEnum.OPTIONS)
        {
            return false;
        }

        // In-dialog OPTIONS belong to the dialog owner (SIPUserAgent), not to this probe responder.
        return string.IsNullOrWhiteSpace(request.Header?.To?.ToTag);
    }

    internal static SIPResponse CreateOkResponse(SIPRequest request, string? userAgent = null)
    {
        var response = SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null);
        response.Header.Allow = AllowedMethods;
        response.Header.Accept = AcceptedContentTypes;
        response.Header.ContentLength = 0;

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            response.Header.Server = userAgent;
        }

        return response;
    }

    internal static string DescribeProbe(SIPRequest request, SIPEndPoint? remoteEndPoint)
    {
        var from = remoteEndPoint?.ToString() ?? request.RemoteSIPEndPoint?.ToString() ?? "unknown";
        var callId = string.IsNullOrWhiteSpace(request.Header?.CallId) ? "unknown" : request.Header.CallId;
        return $"OPTIONS probe from {from} (Call-ID: {callId})";
    }
}
