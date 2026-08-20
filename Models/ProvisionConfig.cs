namespace CallAnalog.Softphone.Models;

public sealed class ProvisionConfig
{
    public string Extension { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SipServer { get; set; } = string.Empty;
    public int SipPort { get; set; } = 5065;
    public string Transport { get; set; } = "tcp";
    public string? DisplayName { get; set; }

    /// <summary>Optional IP/hostname for the TCP socket (defaults to SipServer).</summary>
    public string? SipConnectHost { get; set; }

    public string SipUri => $"sip:{Extension}@{SipServer}";

    public bool UseTcp => Transport.Equals("tcp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// MicroSIP-style registrar string passed to SIPSorcery (host:port;transport=tcp).
    /// Builds AOR sip:ext@host:port;transport=tcp matching CallAnalog C++ client.
    /// </summary>
    public string RegistrarServer =>
        UseTcp
            ? $"{SipServer}:{SipPort};transport=tcp"
            : SipPort == 5060
                ? SipServer
                : $"{SipServer}:{SipPort}";
}
