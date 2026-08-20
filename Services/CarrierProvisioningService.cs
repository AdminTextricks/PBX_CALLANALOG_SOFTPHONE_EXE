using System.Text.Json;
using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone.Services;

public sealed class CarrierProvisioningService
{
    private readonly PbxApiClient _apiClient;
    private readonly UserSettingsService _settingsService;
    private readonly SipLogService _log;
    private readonly string _desktopAppDomainPath;
    private readonly string _webPhoneDomainPath;
    private readonly string _androidDomainPath;
    private readonly string _fallbackHost;
    private readonly int _fallbackPort;
    private readonly bool _useCarrierFallback;

    public CarrierProvisioningService(
        PbxApiClient apiClient,
        UserSettingsService settingsService,
        SipLogService log,
        IConfiguration configuration)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _log = log;
        _desktopAppDomainPath = configuration["PbxApi:DesktopAppDomainPath"]
            ?? "/public/api/proxy-domain/get/desktopapp";
        _webPhoneDomainPath = configuration["PbxApi:WebPhoneDomainPath"]
            ?? "/public/api/proxy-domain/get/webphone";
        _androidDomainPath = configuration["PbxApi:AndroidDomainPath"]
            ?? "/public/api/proxy-domain/get/android";
        _fallbackHost = configuration["Sip:CarrierHost"] ?? "user.callanalog.com";
        _fallbackPort = configuration.GetValue("Sip:SipPort", SipDomainParser.DesktopTcpPort);
        _useCarrierFallback = configuration.GetValue("Provisioning:UseCarrierFallback", true);
    }

    public async Task<ProxyDomainInfo> ResolveForRegistrationAsync(
        string? loginDomainName,
        int? loginDomainPort,
        CancellationToken cancellationToken = default)
    {
        if (SipDomainParser.IsUsableHost(loginDomainName))
        {
            var (host, port) = SipDomainParser.SplitHostPort(loginDomainName!, loginDomainPort ?? _fallbackPort);
            var normalized = SipDomainParser.NormalizeForDesktop(host, port, "login-api");
            _log.Info(SipLogTag.Login, $"Using login API domain '{loginDomainName}' → {normalized.Display}");
            return normalized;
        }

        if (!string.IsNullOrWhiteSpace(loginDomainName))
        {
            _log.Info(SipLogTag.Login, $"Login returned domain '{loginDomainName}' — resolving carrier from proxy-domain API.");
        }

        var savedCarrier = TryGetSavedCarrier();
        if (savedCarrier is not null)
        {
            _log.Info(SipLogTag.Login, $"Using saved carrier {savedCarrier.Display} before proxy-domain lookup.");
            return savedCarrier;
        }

        var fromApi = await TryFetchFromAnyApiAsync(cancellationToken);
        if (fromApi is not null)
        {
            return NormalizeApiResult(fromApi, fromApi.Source);
        }

        if (_useCarrierFallback)
        {
            var fallback = BuildSettingsFallback();
            _log.Warn($"Proxy-domain APIs returned no host — using fallback {fallback.Display}");
            return fallback;
        }

        throw new InvalidOperationException("Unable to resolve SIP carrier domain from CallAnalog server.");
    }

    private ProxyDomainInfo? TryGetSavedCarrier()
    {
        var host = _settingsService.Settings.CarrierHost;
        if (!SipDomainParser.IsUsableHost(host))
        {
            return null;
        }

        var port = _settingsService.Settings.SipPort > 0
            ? _settingsService.Settings.SipPort
            : _fallbackPort;
        var (parsedHost, parsedPort) = SipDomainParser.SplitHostPort(host, port);
        var normalized = SipDomainParser.NormalizeForDesktop(parsedHost, parsedPort, "saved-carrier");
        return new ProxyDomainInfo
        {
            DomainName = normalized.DomainName,
            DomainPort = normalized.DomainPort,
            Transport = string.IsNullOrWhiteSpace(_settingsService.Settings.DefaultTransport)
                ? normalized.Transport
                : _settingsService.Settings.DefaultTransport.Trim().ToLowerInvariant(),
            DomainIp = string.IsNullOrWhiteSpace(_settingsService.Settings.CarrierConnectHost)
                ? null
                : _settingsService.Settings.CarrierConnectHost.Trim(),
            Source = normalized.Source
        };
    }

    private async Task<ProxyDomainInfo?> TryFetchFromAnyApiAsync(CancellationToken cancellationToken)
    {
        var lookups = new (string Path, string Source)[]
        {
            (_desktopAppDomainPath, "desktopapp-api"),
            (_webPhoneDomainPath, "webphone-api"),
            (_androidDomainPath, "android-api")
        };

        var pending = lookups
            .Select(lookup => FetchApiCarrierAsync(lookup.Path, lookup.Source, cancellationToken))
            .ToList();

        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);

            var result = await finished;
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private async Task<ProxyDomainInfo?> FetchApiCarrierAsync(
        string path,
        string source,
        CancellationToken cancellationToken)
    {
        var raw = await TryFetchFromApiAsync(path, source, cancellationToken);
        if (raw is null)
        {
            return null;
        }

        return new ProxyDomainInfo
        {
            DomainName = raw.DomainName,
            DomainPort = raw.DomainPort,
            Transport = raw.Transport,
            DomainIp = raw.DomainIp,
            Source = source
        };
    }

    private ProxyDomainInfo NormalizeApiResult(ProxyDomainInfo raw, string source)
    {
        var normalized = SipDomainParser.NormalizeForDesktop(raw.DomainName, raw.DomainPort, source);
        _log.Info(
            raw.DomainPort == normalized.DomainPort
                ? $"Resolved carrier from {source}: {normalized.Display}"
                : $"Resolved carrier from {source}: {raw.DomainName}:{raw.DomainPort} → desktop {normalized.Display}");
        return normalized;
    }

    private async Task<ProxyDomainInfo?> TryFetchFromApiAsync(
        string path,
        string source,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await _apiClient.GetJsonAsync(path, cancellationToken);
            var parsed = TryParseResponse(document.RootElement);
            if (parsed is null)
            {
                _log.Info($"{source} returned no usable domain.");
                return null;
            }

            return new ProxyDomainInfo
            {
                DomainName = parsed.DomainName,
                DomainPort = parsed.DomainPort,
                Transport = parsed.Transport,
                Source = source
            };
        }
        catch (Exception ex)
        {
            _log.Warn($"{source} failed: {ex.Message}");
            return null;
        }
    }

    private static ProxyDomainInfo? TryParseResponse(JsonElement root)
    {
        if (root.TryGetProperty("data", out var dataElement))
        {
            var fromData = TryParseDataElement(dataElement);
            if (fromData is not null)
            {
                return fromData;
            }
        }

        return TryParseDomainElement(root);
    }

    private static ProxyDomainInfo? TryParseDataElement(JsonElement dataElement) =>
        dataElement.ValueKind switch
        {
            JsonValueKind.Object => TryParseDomainElement(dataElement),
            JsonValueKind.Array when dataElement.GetArrayLength() > 0 => TryParseDomainElement(dataElement[0]),
            _ => null
        };

    private static ProxyDomainInfo? TryParseDomainElement(JsonElement element)
    {
        if (!element.TryGetProperty("domain_name", out var domainElement))
        {
            return null;
        }

        var domainName = domainElement.GetString()?.Trim();
        if (!SipDomainParser.IsUsableHost(domainName))
        {
            return null;
        }

        string? domainIp = element.TryGetProperty("domain_ip", out var ipElement)
            ? ipElement.GetString()?.Trim()
            : null;

        var port = SipDomainParser.DesktopTcpPort;
        if (element.TryGetProperty("domain_port", out var portElement)
            && portElement.TryGetInt32(out var parsedPort)
            && parsedPort > 0)
        {
            port = parsedPort;
        }

        var (host, inlinePort) = SipDomainParser.SplitHostPort(domainName!, port);
        return new ProxyDomainInfo
        {
            DomainName = host,
            DomainIp = string.IsNullOrWhiteSpace(domainIp) ? null : domainIp,
            DomainPort = inlinePort,
            Transport = "tcp"
        };
    }

    private ProxyDomainInfo BuildSettingsFallback()
    {
        var host = string.IsNullOrWhiteSpace(_settingsService.Settings.CarrierHost)
            ? _fallbackHost
            : _settingsService.Settings.CarrierHost;

        var (parsedHost, _) = SipDomainParser.SplitHostPort(host, _fallbackPort);
        var normalized = SipDomainParser.NormalizeForDesktop(parsedHost, _fallbackPort, "settings-fallback");
        return new ProxyDomainInfo
        {
            DomainName = normalized.DomainName,
            DomainPort = normalized.DomainPort,
            Transport = normalized.Transport,
            Source = normalized.Source,
            UsedFallback = true
        };
    }
}
