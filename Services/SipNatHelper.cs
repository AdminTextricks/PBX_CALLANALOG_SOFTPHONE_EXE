using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using SIPSorcery.Net;

namespace CallAnalog.Softphone.Services;

internal static partial class SipNatHelper
{
    private static readonly TimeSpan StunLookupTimeout = TimeSpan.FromSeconds(4);

    private static IPAddress? _cachedPublicIp;
    private static TurnSettings? _turnSettings;

    public static IPAddress? CachedPublicIp => _cachedPublicIp;

    public static void ConfigureTurn(IConfiguration configuration)
    {
        var host = configuration["Sip:TurnServer"]?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            _turnSettings = null;
            return;
        }

        _turnSettings = new TurnSettings(
            host,
            configuration.GetValue("Sip:TurnPort", 3478),
            configuration["Sip:TurnUsername"],
            configuration["Sip:TurnPassword"]);
    }

    public static void RestoreCachedPublicIp(string? ipText, DateTimeOffset? capturedUtc, SipLogService? log = null)
    {
        if (string.IsNullOrWhiteSpace(ipText) || capturedUtc is null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow - capturedUtc.Value > TimeSpan.FromHours(24))
        {
            return;
        }

        if (!IPAddress.TryParse(ipText.Trim(), out var address) || IsPrivateAddress(address))
        {
            return;
        }

        if (log is null)
        {
            _cachedPublicIp = address;
            return;
        }

        RememberPublicIp(address, log, "saved session cache");
    }

    public static IPAddress GetConnectionAddressForSdp()
    {
        if (_cachedPublicIp is not null)
        {
            return _cachedPublicIp;
        }

        return IPAddress.Any;
    }

    public static async Task<IPAddress?> ResolvePublicIpAsync(SipLogService log, CancellationToken cancellationToken = default)
    {
        if (_cachedPublicIp is not null)
        {
            return _cachedPublicIp;
        }

        foreach (var (host, port) in StunServers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var lookupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lookupCts.CancelAfter(StunLookupTimeout);

                var address = await Task.Run(
                    () => STUNClient.GetPublicIPAddress(host, port),
                    lookupCts.Token);

                if (address is not null && !IsPrivateAddress(address))
                {
                    RememberPublicIp(address, log, $"STUN {host}:{port}");
                    return address;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log.Warn($"STUN lookup timed out for {host}:{port} after {StunLookupTimeout.TotalSeconds:F0}s.");
            }
            catch (Exception ex)
            {
                log.Warn($"STUN lookup failed for {host}:{port} — {ex.Message}");
            }
        }

        if (_turnSettings is not null)
        {
            try
            {
                using var lookupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lookupCts.CancelAfter(StunLookupTimeout);

                var address = await Task.Run(
                    () => TryTurnPublicIp(_turnSettings, log),
                    lookupCts.Token);

                if (address is not null && !IsPrivateAddress(address))
                {
                    RememberPublicIp(address, log, $"TURN {_turnSettings.Host}:{_turnSettings.Port}");
                    return address;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log.Warn($"TURN lookup timed out for {_turnSettings.Host}:{_turnSettings.Port}.");
            }
            catch (Exception ex)
            {
                log.Warn($"TURN lookup failed for {_turnSettings.Host}:{_turnSettings.Port} — {ex.Message}");
            }
        }

        log.Info("STUN/TURN lookup did not resolve a public IP; proceeding with SIP Via learning fallback.");
        return null;
    }

    private static IPAddress? TryTurnPublicIp(TurnSettings settings, SipLogService log)
    {
        // Best-effort: SIPSorcery STUN client against configured TURN host (same binding request path).
        try
        {
            var address = STUNClient.GetPublicIPAddress(settings.Host, settings.Port);
            if (address is not null)
            {
                log.Info($"TURN server {settings.Host}:{settings.Port} returned public IP candidate {address}.");
            }

            return address;
        }
        catch (Exception ex)
        {
            log.Warn($"TURN server probe via STUN binding failed: {ex.Message}");
            return null;
        }
    }

    public static void TryCapturePublicIpFromSipMessage(string? message, SipLogService log)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        foreach (Match match in ReceivedViaRegex().Matches(message))
        {
            if (IPAddress.TryParse(match.Groups[1].Value, out var address) && !IsPrivateAddress(address))
            {
                RememberPublicIp(address, log, "SIP Via received");
                return;
            }
        }
    }

    public static void RememberPublicIp(IPAddress address, SipLogService log, string source)
    {
        if (IsPrivateAddress(address))
        {
            return;
        }

        if (_cachedPublicIp is not null && _cachedPublicIp.Equals(address))
        {
            return;
        }

        _cachedPublicIp = address;
        if (source.Contains("Via", StringComparison.OrdinalIgnoreCase))
        {
            log.Info($"Detected public IP from Via: {address}");
        }
        else
        {
            log.Info($"Using public media IP {address} (from {source}).");
        }
    }

    public static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            127 => true,
            172 when bytes[1] is >= 16 and <= 31 => true,
            192 when bytes[1] == 168 => true,
            _ => false
        };
    }

    private static readonly (string Host, int Port)[] StunServers =
    [
        ("stun.l.google.com", 19302),
        ("stun1.l.google.com", 19302)
    ];

    private sealed record TurnSettings(string Host, int Port, string? Username, string? Password);

    [GeneratedRegex(@"received=([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReceivedViaRegex();
}
