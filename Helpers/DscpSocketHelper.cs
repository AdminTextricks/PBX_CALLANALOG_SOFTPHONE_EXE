using System.Net.Sockets;

namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Best-effort DSCP EF (46) marking for SIP/RTP sockets. Failures never throw —
/// many networks strip DSCP, and some Windows builds reject the option.
/// </summary>
public static class DscpSocketHelper
{
    /// <summary>DSCP Expedited Forwarding = 46; IP TOS byte is DSCP &lt;&lt; 2.</summary>
    public const int ExpeditedForwardingDscp = 46;
    public const int ExpeditedForwardingTos = ExpeditedForwardingDscp << 2; // 184

    public static bool TryMarkExpeditedForwarding(Socket? socket, out string detail)
    {
        if (socket is null)
        {
            detail = "socket is null";
            return false;
        }

        try
        {
            if (socket.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            {
                // IPv4 TOS / DualMode IPv6 mapped traffic.
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, ExpeditedForwardingTos);
            }

            if (socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // IPv6 Traffic Class (TClass) — same numeric value as TOS for EF.
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.TypeOfService, ExpeditedForwardingTos);
            }

            detail = $"DSCP {ExpeditedForwardingDscp} (EF) applied";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }
}
