using CallAnalog.Softphone.Helpers;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone.Services;

public sealed class AppVersionCheckService
{
    private readonly PbxApiClient _apiClient;
    private readonly string _versionCheckPath;
    private readonly string _applicationKey;

    public AppVersionCheckService(PbxApiClient apiClient, IConfiguration configuration)
    {
        _apiClient = apiClient;
        _versionCheckPath = configuration["PbxApi:VersionCheckPath"]
            ?? "/public/api/application/version-check";
        _applicationKey = configuration["PbxApi:ApplicationKey"] ?? "pbx_desktop_exe";
    }

    public async Task<AppVersionCheckResult> CheckAsync(
        string? installedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var version = string.IsNullOrWhiteSpace(installedVersion)
            ? BuildInfo.Version
            : installedVersion.Trim();

        var payload = AppVersionCheckHelper.BuildRequestJson(_applicationKey, version);
        using var document = await _apiClient.PostJsonDocumentAsync(_versionCheckPath, payload, cancellationToken);
        return AppVersionCheckHelper.ParseResponse(document.RootElement, version);
    }
}
