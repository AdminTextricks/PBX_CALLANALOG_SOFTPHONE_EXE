using System.Text;
using System.Text.RegularExpressions;

namespace CallAnalog.Softphone.Services;

internal static class SipLogRedaction
{
    private static readonly Regex AuthorizationHeaderRegex = new(@"(?i)(Authorization:\s*)(.+)", RegexOptions.Multiline);
    private static readonly Regex ProxyAuthorizationHeaderRegex = new(@"(?i)(Proxy-Authorization:\s*)(.+)", RegexOptions.Multiline);
    private static readonly Regex WwwAuthenticateHeaderRegex = new(@"(?i)(WWW-Authenticate:\s*)(.+)", RegexOptions.Multiline);
    private static readonly Regex ProxyAuthenticateHeaderRegex = new(@"(?i)(Proxy-Authenticate:\s*)(.+)", RegexOptions.Multiline);
    private static readonly Regex DigestResponseRegex = new(@"(?i)(response\s*=\s*"")[^""]+("")", RegexOptions.Multiline);
    private static readonly Regex DigestUriRegex = new(@"(?i)(uri\s*=\s*"")[^""]+("")", RegexOptions.Multiline);

    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var redacted = AuthorizationHeaderRegex.Replace(message, "$1[REDACTED]");
        redacted = ProxyAuthorizationHeaderRegex.Replace(redacted, "$1[REDACTED]");
        redacted = WwwAuthenticateHeaderRegex.Replace(redacted, "$1[REDACTED]");
        redacted = ProxyAuthenticateHeaderRegex.Replace(redacted, "$1[REDACTED]");
        redacted = DigestResponseRegex.Replace(redacted, "$1[REDACTED]$2");
        redacted = DigestUriRegex.Replace(redacted, "$1[REDACTED]$2");
        return redacted;
    }
}
