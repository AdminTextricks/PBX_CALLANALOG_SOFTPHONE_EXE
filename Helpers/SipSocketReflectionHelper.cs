using System.Net.Sockets;
using System.Reflection;

namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Locates private Socket fields on SIPSorcery channel / RTP objects so we can mark DSCP
/// without forking the library. Reflection is best-effort and must never break calls.
/// </summary>
internal static class SipSocketReflectionHelper
{
    private static readonly string[] SocketFieldNames =
    [
        "m_udpSocket",
        "m_socket",
        "m_tcpSocket",
        "m_tlsStream",
        "_socket",
        "rtpSocket",
        "controlSocket",
        "m_rtpSocket",
        "m_controlSocket",
        "RtpSocket",
        "ControlSocket"
    ];

    public static IReadOnlyList<Socket> FindSockets(object? target)
    {
        if (target is null)
        {
            return [];
        }

        var found = new List<Socket>();
        CollectSockets(target, found, depth: 0, visited: new HashSet<object>(ReferenceEqualityComparer.Instance));
        return found;
    }

    private static void CollectSockets(object target, List<Socket> found, int depth, HashSet<object> visited)
    {
        if (depth > 4 || !visited.Add(target))
        {
            return;
        }

        if (target is Socket socket)
        {
            found.Add(socket);
            return;
        }

        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var field in type.GetFields(flags))
        {
            object? value;
            try
            {
                value = field.GetValue(target);
            }
            catch
            {
                continue;
            }

            if (value is Socket sock)
            {
                found.Add(sock);
                continue;
            }

            if (value is null || value is string || value.GetType().IsPrimitive)
            {
                continue;
            }

            // Dive into known media/transport containers only.
            var name = field.Name;
            if (SocketFieldNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.Contains("Socket", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Channel", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Rtp", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Audio", StringComparison.OrdinalIgnoreCase))
            {
                CollectSockets(value, found, depth + 1, visited);
            }
        }

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanRead)
            {
                continue;
            }

            if (prop.PropertyType != typeof(Socket)
                && !prop.Name.Contains("Socket", StringComparison.OrdinalIgnoreCase)
                && !prop.Name.Contains("Channel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            object? value;
            try
            {
                value = prop.GetValue(target);
            }
            catch
            {
                continue;
            }

            if (value is Socket sock)
            {
                found.Add(sock);
            }
            else if (value is not null && depth < 2)
            {
                CollectSockets(value, found, depth + 1, visited);
            }
        }
    }
}
