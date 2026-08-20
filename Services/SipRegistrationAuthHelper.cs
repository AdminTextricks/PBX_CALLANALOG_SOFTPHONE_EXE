using CallAnalog.Softphone.Models;
using SIPSorcery.SIP;

namespace CallAnalog.Softphone.Services;

internal static class SipRegistrationAuthHelper
{
    public static bool TryApplyPreemptiveAuth(
        SIPRequest request,
        string extension,
        string password,
        SipRegistrationDigestCacheEntry? cacheEntry)
    {
        if (cacheEntry is null || !cacheEntry.IsUsable || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        if (request.Header.AuthenticationHeaders.Count > 0)
        {
            return false;
        }

        var challenge = BuildChallengeHeader(request.URI, cacheEntry);
        var authHeader = SIPAuthChallenge.GetAuthenticationHeader(
            [challenge],
            request.URI,
            SIPMethodsEnum.REGISTER,
            extension.Trim(),
            password,
            ParseDigestAlgorithm(cacheEntry.Algorithm));

        request.Header.AuthenticationHeaders.Add(authHeader);
        return true;
    }

    public static SipRegistrationDigestCacheEntry? CaptureFromChallenge(SIPResponse? response, string extension)
    {
        var challenge = GetRegisterChallengeHeader(response);
        if (challenge?.SIPDigest is not { } digest || string.IsNullOrWhiteSpace(digest.Realm))
        {
            return null;
        }

        return CreateCacheEntry(extension, digest);
    }

    public static SipRegistrationDigestCacheEntry? CaptureFromAuthenticatedRequest(SIPRequest? request, string extension)
    {
        if (request?.Method != SIPMethodsEnum.REGISTER)
        {
            return null;
        }

        var authHeader = request.Header.AuthenticationHeaders.FirstOrDefault();
        if (authHeader?.SIPDigest is not { } digest
            || string.IsNullOrWhiteSpace(digest.Realm)
            || string.IsNullOrWhiteSpace(digest.Nonce))
        {
            return null;
        }

        return CreateCacheEntry(extension, digest);
    }

    public static bool IsRegisterForbidden(SIPResponse? response) =>
        response?.Status is SIPResponseStatusCodesEnum.Forbidden
        && response.Header?.CSeqMethod == SIPMethodsEnum.REGISTER;

    private static SIPAuthenticationHeader BuildChallengeHeader(SIPURI uri, SipRegistrationDigestCacheEntry cacheEntry)
    {
        var digest = new SIPAuthorisationDigest(
            SIPAuthorisationHeadersEnum.WWWAuthenticate,
            cacheEntry.Realm,
            string.Empty,
            string.Empty,
            uri.ToString(),
            cacheEntry.Nonce,
            cacheEntry.Opaque ?? string.Empty,
            ParseDigestAlgorithm(cacheEntry.Algorithm))
        {
            Qop = NormalizeQop(cacheEntry.Qop)
        };

        return new SIPAuthenticationHeader(digest);
    }

    private static SIPAuthenticationHeader? GetRegisterChallengeHeader(SIPResponse? response)
    {
        if (response?.Header is null || response.Header.CSeqMethod != SIPMethodsEnum.REGISTER)
        {
            return null;
        }

        return response.Header.AuthenticationHeaders.FirstOrDefault();
    }

    private static SipRegistrationDigestCacheEntry CreateCacheEntry(string extension, SIPAuthorisationDigest digest) =>
        new()
        {
            Extension = extension.Trim(),
            Realm = digest.Realm ?? string.Empty,
            Nonce = digest.Nonce ?? string.Empty,
            Qop = NormalizeQop(digest.Qop),
            Algorithm = digest.DigestAlgorithm.ToString(),
            Opaque = digest.Opaque,
            Cnonce = digest.Cnonce,
            Nc = digest.NonceCount > 0 ? digest.NonceCount.ToString() : null,
            LastSuccessUtc = DateTimeOffset.UtcNow
        };

    private static DigestAlgorithmsEnum ParseDigestAlgorithm(string? algorithm) =>
        algorithm?.Trim().Equals("SHA-256", StringComparison.OrdinalIgnoreCase) == true
            ? DigestAlgorithmsEnum.SHA256
            : DigestAlgorithmsEnum.MD5;

    private static string? NormalizeQop(string? qop)
    {
        if (string.IsNullOrWhiteSpace(qop))
        {
            return null;
        }

        if (qop.Contains(',', StringComparison.Ordinal)
            || qop.Contains("auth-int", StringComparison.OrdinalIgnoreCase))
        {
            return SIPAuthorisationDigest.QOP_AUTHENTICATION_VALUE;
        }

        return qop;
    }
}
