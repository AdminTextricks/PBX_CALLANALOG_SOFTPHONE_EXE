using SIPSorcery.SIP;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// OpenSIPS challenges with qop="auth-int,auth". SIPSorcery echoes the full string in Authorization,
/// which OpenSIPS rejects with 400. MicroSIP/PJSIP reply with qop=auth only.
/// </summary>
internal static class OpenSipsAuthHelper
{
    public static bool IsAuthenticationChallenge(SIPResponse? response) =>
        response?.Status is SIPResponseStatusCodesEnum.Unauthorised
            or SIPResponseStatusCodesEnum.ProxyAuthenticationRequired;

    public static bool IsRegistrationAuthChallenge(SIPResponse? response) =>
        IsAuthenticationChallenge(response);

    public static void NormalizeAuthenticationHeaders(SIPResponse? response)
    {
        if (response?.Header?.AuthenticationHeaders is not { Count: > 0 } headers)
        {
            return;
        }

        foreach (var header in headers)
        {
            var qop = header.SIPDigest.Qop;
            if (string.IsNullOrWhiteSpace(qop))
            {
                continue;
            }

            if (qop.Contains(',', StringComparison.Ordinal)
                || qop.Contains("auth-int", StringComparison.OrdinalIgnoreCase))
            {
                header.SIPDigest.Qop = SIPAuthorisationDigest.QOP_AUTHENTICATION_VALUE;
            }
        }
    }
}
