using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone.Services;

public sealed class ContactsService
{
    private readonly PbxApiClient _apiClient;
    private readonly OfflineDataCacheService _cache;
    private readonly string _listPath;
    private readonly string _createPath;
    private readonly string _updatePath;
    private readonly string _deletePath;

    public ContactsService(PbxApiClient apiClient, IConfiguration configuration, OfflineDataCacheService? cache = null)
    {
        _apiClient = apiClient;
        _cache = cache ?? new OfflineDataCacheService();
        _listPath = configuration["PbxApi:ContactsPath"] ?? "/public/api/phone-contact/getall";
        _createPath = configuration["PbxApi:ContactCreatePath"] ?? "/public/api/phone-contact";
        _updatePath = configuration["PbxApi:ContactUpdatePath"] ?? "/public/api/phone-contact/update/";
        _deletePath = configuration["PbxApi:ContactDeletePath"] ?? "/public/api/phone-contact/delete/";
    }

    public async Task<OfflinePagedResult<Contact>> GetContactsAsync(
        string extension,
        int page = 1,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _apiClient.GetPagedAsync<Contact>(
                _listPath,
                new Dictionary<string, string?>
                {
                    ["extension_name"] = extension,
                    ["page"] = page.ToString(),
                    ["perPage"] = _apiClient.PageSize.ToString(),
                    ["searchQry"] = search
                },
                cancellationToken);

            if (page == 1 && string.IsNullOrWhiteSpace(search))
            {
                _cache.SaveContacts(extension, result.Items);
            }

            return OfflinePagedResult<Contact>.Online(result);
        }
        catch (Exception ex) when (page == 1)
        {
            var cached = _cache.LoadContacts(extension);
            if (cached?.Items.Count > 0)
            {
                var filtered = string.IsNullOrWhiteSpace(search)
                    ? cached.Items
                    : cached.Items.Where(c =>
                        (c.Name ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (c.ContactNumber ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

                return OfflinePagedResult<Contact>.Offline(
                    new PagedResult<Contact>
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

    public async Task CreateContactAsync(
        string extension,
        string name,
        string number,
        CancellationToken cancellationToken = default)
    {
        var payload = PbxContactPayload.Build(extension, name, number);
        await _apiClient.PostJsonAsync(_createPath, payload, cancellationToken);
    }

    public async Task UpdateContactAsync(
        string extension,
        int contactId,
        string name,
        string number,
        CancellationToken cancellationToken = default)
    {
        var payload = PbxContactPayload.Build(extension, name, number);
        await _apiClient.PatchJsonAsync($"{_updatePath}{contactId}", payload, cancellationToken);
    }

    public Task DeleteContactAsync(int contactId, CancellationToken cancellationToken = default) =>
        _apiClient.DeleteAsync($"{_deletePath}{contactId}", cancellationToken);
}

public sealed class OfflinePagedResult<T>
{
    public required PagedResult<T> Result { get; init; }
    public bool IsOffline { get; init; }
    public DateTimeOffset? CachedUtc { get; init; }
    public string? OfflineMessage { get; init; }

    public static OfflinePagedResult<T> Online(PagedResult<T> result) =>
        new() { Result = result };

    public static OfflinePagedResult<T> Offline(PagedResult<T> result, DateTimeOffset cachedUtc, string message) =>
        new()
        {
            Result = result,
            IsOffline = true,
            CachedUtc = cachedUtc,
            OfflineMessage = message
        };
}
