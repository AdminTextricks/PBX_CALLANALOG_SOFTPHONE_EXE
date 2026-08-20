using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone.Services;

public sealed class CallHistoryService
{
    private readonly PbxApiClient _apiClient;
    private readonly OfflineDataCacheService _cache;
    private readonly string _path;

    public CallHistoryService(PbxApiClient apiClient, IConfiguration configuration, OfflineDataCacheService? cache = null)
    {
        _apiClient = apiClient;
        _cache = cache ?? new OfflineDataCacheService();
        _path = configuration["PbxApi:CallHistoryPath"] ?? "/public/api/getCallHistory";
    }

    public async Task<OfflinePagedResult<CallRecord>> GetCallHistoryAsync(
        string extension,
        int page = 1,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _apiClient.GetPagedAsync<CallRecord>(
                _path,
                new Dictionary<string, string?>
                {
                    ["name"] = extension,
                    ["page"] = page.ToString(),
                    ["perPage"] = _apiClient.PageSize.ToString(),
                    ["search"] = search
                },
                cancellationToken);

            if (page == 1 && string.IsNullOrWhiteSpace(search))
            {
                _cache.SaveCallHistory(extension, result.Items);
            }

            return OfflinePagedResult<CallRecord>.Online(result);
        }
        catch (Exception ex) when (page == 1)
        {
            var cached = _cache.LoadCallHistory(extension);
            if (cached?.Items.Count > 0)
            {
                var filtered = string.IsNullOrWhiteSpace(search)
                    ? cached.Items
                    : cached.Items.Where(r =>
                        (r.CallerNumber ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (r.Destination ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (r.ContactName ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

                return OfflinePagedResult<CallRecord>.Offline(
                    new PagedResult<CallRecord>
                    {
                        Items = filtered,
                        Total = filtered.Count,
                        CurrentPage = 1,
                        LastPage = 1,
                        LoadedCount = filtered.Count
                    },
                    cached.SavedUtc,
                    ex.Message);
            }

            throw;
        }
    }

    public async Task<string?> ResolveRecentCallIdAsync(
        string extension,
        string? remoteParty,
        bool isOutbound,
        string? sipCallId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(sipCallId))
        {
            return sipCallId;
        }

        var wrapped = await GetCallHistoryAsync(extension, 1, cancellationToken: cancellationToken);
        var result = wrapped.Result;
        if (result.Items.Count == 0)
        {
            return null;
        }

        var normalizedRemote = NormalizeNumber(remoteParty);
        var recentWindow = DateTimeOffset.UtcNow.AddMinutes(-10);

        foreach (var call in result.Items)
        {
            if (call.Id <= 0)
            {
                continue;
            }

            if (isOutbound != call.IsOutbound)
            {
                continue;
            }

            if (TryParseCallTimestamp(call.CallDate, out var callTime) && callTime < recentWindow)
            {
                continue;
            }

            var callNumber = NormalizeNumber(call.DialNumber);
            if (!string.IsNullOrWhiteSpace(normalizedRemote)
                && !string.Equals(callNumber, normalizedRemote, StringComparison.Ordinal))
            {
                continue;
            }

            if (call.PbxCallId > 0)
            {
                return call.PbxCallId.ToString();
            }
        }

        var latest = result.Items.FirstOrDefault(c => c.Id > 0 && c.IsOutbound == isOutbound);
        return latest?.PbxCallId > 0 ? latest.PbxCallId.ToString() : null;
    }

    private static bool TryParseCallTimestamp(string? callDate, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(callDate))
        {
            return false;
        }

        return DateTimeOffset.TryParse(callDate, out timestamp)
            || (DateTime.TryParse(callDate, out var local)
                && (timestamp = new DateTimeOffset(local)).Ticks > 0);
    }

    private static string NormalizeNumber(string? number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
}
