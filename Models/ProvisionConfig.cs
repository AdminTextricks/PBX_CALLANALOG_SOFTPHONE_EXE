namespace CallAnalog.Softphone.Models;

public sealed class ProvisionConfig
{
    public string Extension { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SipServer { get; set; } = string.Empty;
    public int SipPort { get; set; } = 5065;
    public string Transport { get; set; } = "udp";
    public string? DisplayName { get; set; }

    /// <summary>Optional IP/hostname for the TCP socket (defaults to SipServer).</summary>
    public string? SipConnectHost { get; set; }

    public string SipUri => $"sip:{Extension}@{SipServer}";

    public bool IsTls => Transport.Equals("tls", StringComparison.OrdinalIgnoreCase);

    public bool UseUdp =>
        Transport.Equals("udp", StringComparison.OrdinalIgnoreCase)
        || Transport.Equals("udp+tcp", StringComparison.OrdinalIgnoreCase);

    public bool UseTcp =>
        Transport.Equals("tcp", StringComparison.OrdinalIgnoreCase)
        || Transport.Equals("udp+tcp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Registrar target. TCP and UDP+TCP register over TCP. UDP registers without a transport suffix.
    /// </summary>
    public string RegistrarServer =>
        UseTcp
            ? $"{SipServer}:{SipPort};transport=tcp"
            : SipPort == 5060
                ? SipServer
                : $"{SipServer}:{SipPort}";
}
