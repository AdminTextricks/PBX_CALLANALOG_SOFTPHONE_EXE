using System.Text;
using System.Text.RegularExpressions;

namespace CallAnalog.Softphone.Services;

internal static partial class SipLogRedaction
{
    [GeneratedRegex(@"(?i)(Authorization:\s*)(.+)", RegexOptions.Multiline)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"(?i)(Proxy-Authorization:\s*)(.+)", RegexOptions.Multiline)]
    private static partial Regex ProxyAuthorizationHeaderRegex();

    [GeneratedRegex(@"(?i)(WWW-Authenticate:\s*)(.+)", RegexOptions.Multiline)]
    private static partial Regex WwwAuthenticateHeaderRegex();

    [GeneratedRegex(@"(?i)(Proxy-Authenticate:\s*)(.+)", RegexOptions.Multiline)]
    private static partial Regex ProxyAuthenticateHeaderRegex();

    [GeneratedRegex(@"(?i)(response\s*=\s*"")[^""]+("")", RegexOptions.Multiline)]
    private static partial Regex DigestResponseRegex();

    [GeneratedRegex(@"(?i)(uri\s*=\s*"")[^""]+("")", RegexOptions.Multiline)]
    private static partial Regex DigestUriRegex();

    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var redacted = AuthorizationHeaderRegex().Replace(message, "$1[REDACTED]");
        redacted = ProxyAuthorizationHeaderRegex().Replace(redacted, "$1[REDACTED]");
        redacted = WwwAuthenticateHeaderRegex().Replace(redacted, "$1[REDACTED]");
        redacted = ProxyAuthenticateHeaderRegex().Replace(redacted, "$1[REDACTED]");
        redacted = DigestResponseRegex().Replace(redacted, "$1[REDACTED]$2");
        redacted = DigestUriRegex().Replace(redacted, "$1[REDACTED]$2");
        return redacted;
    }
}
