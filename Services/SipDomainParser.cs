using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

internal static class SipDomainParser
{
    public const int DesktopTcpPort = 5065;

    public static bool IsUsableHost(string? domain) =>
        !string.IsNullOrWhiteSpace(domain)
        && !domain.Equals("dynamic", StringComparison.OrdinalIgnoreCase)
        && !domain.Contains(' ');

    public static (string Host, int Port) SplitHostPort(string domainName, int defaultPort)
    {
        var host = domainName.Trim();
        var port = defaultPort;

        if (!host.Contains(':', StringComparison.Ordinal))
        {
            return (host, port);
        }

        var parts = host.Split(':', 2);
        host = parts[0];
        if (parts.Length > 1 && int.TryParse(parts[1], out var inlinePort) && inlinePort > 0)
        {
            port = inlinePort;
        }

        return (host, port);
    }

    /// <summary>
    /// Desktop softphones register to OpenSIPS on TCP 5065 even when the API returns 5063 (web/WSS).
    /// </summary>
    public static ProxyDomainInfo NormalizeForDesktop(string domainName, int apiPort, string source)
    {
        var (host, _) = SplitHostPort(domainName, apiPort);
        return new ProxyDomainInfo
        {
            DomainName = host,
            DomainPort = DesktopTcpPort,
            Transport = "tcp",
            Source = source
        };
    }
}
