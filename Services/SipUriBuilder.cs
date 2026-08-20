using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Builds SIP URIs matching the registered transport (TCP 5065 for CallAnalog OpenSIPS).
/// </summary>
internal static class SipUriBuilder
{
    public static string BuildDialUri(ProvisionConfig config, string number)
    {
        if (number.StartsWith("sip:", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureTransportOnUri(config, number);
        }

        return BuildUserUri(config, NormalizeDialUser(number));
    }

    public static string BuildFromUri(ProvisionConfig config) =>
        BuildUserUri(config, config.Extension);

    public static string BuildServerUri(ProvisionConfig config)
    {
        if (config.UseTcp)
        {
            return $"sip:{config.SipServer}:{config.SipPort};transport=tcp";
        }

        return config.SipPort == 5060
            ? $"sip:{config.SipServer}"
            : $"sip:{config.SipServer}:{config.SipPort}";
    }

    /// <summary>Strips E.164 leading + so INVITE Request-URI uses digits only (e.g. +1866… → 1866…).</summary>
    private static string NormalizeDialUser(string number) =>
        number.StartsWith('+') ? number[1..] : number;

    private static string BuildUserUri(ProvisionConfig config, string user)
    {
        if (config.UseTcp)
        {
            return $"sip:{user}@{config.SipServer}:{config.SipPort};transport=tcp";
        }

        return config.SipPort == 5060
            ? $"sip:{user}@{config.SipServer}"
            : $"sip:{user}@{config.SipServer}:{config.SipPort}";
    }

    private static string EnsureTransportOnUri(ProvisionConfig config, string uri)
    {
        if (config.UseTcp && !uri.Contains("transport=tcp", StringComparison.OrdinalIgnoreCase))
        {
            var separator = uri.Contains(';') ? ";" : ";";
            if (!uri.Contains(':' + config.SipPort.ToString(), StringComparison.Ordinal))
            {
                var atIndex = uri.IndexOf('@');
                if (atIndex >= 0)
                {
                    var hostPart = uri[(atIndex + 1)..];
                    var semiIndex = hostPart.IndexOf(';');
                    if (semiIndex >= 0)
                    {
                        hostPart = hostPart[..semiIndex];
                    }

                    if (!hostPart.Contains(':'))
                    {
                        uri = uri.Replace($"@{hostPart}", $"@{hostPart}:{config.SipPort}", StringComparison.Ordinal);
                    }
                }
            }

            uri += $"{separator}transport=tcp";
        }

        return uri;
    }
}
