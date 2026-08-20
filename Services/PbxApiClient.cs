using System.Net.Http;
using System.Text;
using System.Text.Json;
using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone.Services;

public sealed class PbxApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new NullToEmptyStringJsonConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly int _pageSize;

    public PbxApiClient(IConfiguration configuration)
    {
        _baseUrl = (configuration["PbxApi:BaseUrl"] ?? "https://pbxbackend.callanalog.com").TrimEnd('/');
        _pageSize = configuration.GetValue("PbxApi:PageSize", 25);
        var timeoutSeconds = configuration.GetValue("PbxApi:TimeoutSeconds", 30);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CallAnalog-Softphone/1.2.0");
    }

    public int PageSize => _pageSize;

    public async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(ResolveUrl(path), cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(ParseErrorMessage(responseText) ?? $"Request failed ({(int)response.StatusCode}).");
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseText) ? "{}" : responseText);
    }

    public async Task TryUnregisterExtensionAsync(string extension, string unregisterPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        var normalizedPath = unregisterPath.TrimEnd('/');
        var path = $"{normalizedPath}/{Uri.EscapeDataString(extension.Trim())}";

        try
        {
            using var _ = await GetJsonAsync(path, cancellationToken);
        }
        catch
        {
            // Best-effort cleanup before registering from this client.
        }
    }

    public async Task<PagedResult<T>> GetPagedAsync<T>(
        string path,
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(path, query);
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(ParseErrorMessage(responseText) ?? $"Request failed ({(int)response.StatusCode}).");
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseText) ? "{}" : responseText);
        var currentPage = query.TryGetValue("page", out var pageValue) && int.TryParse(pageValue, out var page)
            ? page
            : 1;

        return PbxPagedResponseParser.Parse<T>(document.RootElement, currentPage, JsonOptions);
    }

    public async Task PostJsonAsync(string path, string jsonBody, CancellationToken cancellationToken = default)
    {
        using var _ = await PostJsonDocumentAsync(path, jsonBody, cancellationToken);
    }

    public async Task<JsonDocument> PostJsonDocumentAsync(string path, string jsonBody, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(ResolveUrl(path), content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(ParseErrorMessage(responseText) ?? $"Request failed ({(int)response.StatusCode}).");
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return JsonDocument.Parse("{}");
        }

        var document = JsonDocument.Parse(responseText);
        try
        {
            EnsureApiSuccess(document.RootElement);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    public async Task PatchJsonAsync(string path, string jsonBody, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, ResolveUrl(path))
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync(ResolveUrl(path), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private string ResolveUrl(string path) =>
        $"{_baseUrl}{(path.StartsWith('/') ? path : $"/{path}")}";

    private string BuildUrl(string path, IReadOnlyDictionary<string, string?> query)
    {
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        var pairs = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");

        var queryString = string.Join('&', pairs);
        return string.IsNullOrEmpty(queryString)
            ? $"{_baseUrl}{normalizedPath}"
            : $"{_baseUrl}{normalizedPath}?{queryString}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(ParseErrorMessage(responseText) ?? $"Request failed ({(int)response.StatusCode}).");
        }

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            using var document = JsonDocument.Parse(responseText);
            EnsureApiSuccess(document.RootElement);
        }
    }

    private static void EnsureApiSuccess(JsonElement root)
    {
        if (root.TryGetProperty("status", out var statusElement)
            && statusElement.ValueKind is JsonValueKind.False)
        {
            var message = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "Request was rejected by the server.";
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Request was rejected." : message);
        }
    }

    private static string? ParseErrorMessage(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }
        }
        catch
        {
            // Ignore parse errors.
        }

        return null;
    }

    public void Dispose() => _httpClient.Dispose();
}
