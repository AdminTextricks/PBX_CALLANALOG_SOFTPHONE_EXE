namespace CallAnalog.Softphone.Models;

public sealed class ProxyDomainInfo
{
    public string DomainName { get; init; } = string.Empty;
    public string? DomainIp { get; init; }
    public int DomainPort { get; init; }
    public string Transport { get; init; } = "tcp";
    public bool UsedFallback { get; init; }
    public string Source { get; init; } = string.Empty;

    public string Display => $"{DomainName}:{DomainPort} ({Transport})";
}
