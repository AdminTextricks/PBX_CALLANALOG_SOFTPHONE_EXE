using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone.Services;

public sealed class LoginService
{
    private readonly HttpClient _httpClient;
    private readonly string _loginUrl;

    public LoginService(IConfiguration configuration)
    {
        var baseUrl = (configuration["PbxApi:BaseUrl"] ?? "https://pbxbackend.callanalog.com").TrimEnd('/');
        var loginPath = configuration["PbxApi:LoginPath"] ?? "/public/api/extension_login";
        var timeoutSeconds = configuration.GetValue("PbxApi:TimeoutSeconds", 30);

        _loginUrl = $"{baseUrl}{(loginPath.StartsWith('/') ? loginPath : $"/{loginPath}")}";
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CallAnalog-Softphone/1.2.0");
    }

    public async Task<LoginResult> LoginAsync(string extension, string password, CancellationToken cancellationToken = default)
    {
        extension = extension.Trim();

        if (string.IsNullOrWhiteSpace(extension))
        {
            return Failure(extension, 400, "Extension number is required");
        }

        if (!extension.All(char.IsDigit))
        {
            return Failure(extension, 400, "Extension must contain digits only");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return Failure(extension, 400, "Password is required");
        }

        var payload = JsonSerializer.Serialize(new
        {
            name = extension,
            secret = password
        });

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_loginUrl, content, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                return response.IsSuccessStatusCode
                    ? Success(extension, (int)response.StatusCode, "Login successful", null, null)
                    : Failure(extension, (int)response.StatusCode, response.ReasonPhrase ?? "Login failed");
            }

            return ParseResponse(extension, response.StatusCode, responseText);
        }
        catch (TaskCanceledException)
        {
            return Failure(extension, 408, "Request timed out");
        }
        catch (HttpRequestException)
        {
            return Failure(extension, -1, "Unable to connect to CallAnalog server");
        }
        catch (Exception)
        {
            return Failure(extension, -2, "Unexpected error during login");
        }
    }

    private static LoginResult ParseResponse(string extension, HttpStatusCode httpStatus, string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            var code = root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : (int)httpStatus;

            var message = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "Login failed"
                : "Login failed";

            var statusOk = root.TryGetProperty("status", out var statusElement)
                && statusElement.ValueKind is JsonValueKind.True;

            if (statusOk || httpStatus is HttpStatusCode.OK)
            {
                var (domainName, domainPort) = ParseLoginDomain(root);
                return Success(extension, code, message, domainName, domainPort);
            }

            return Failure(extension, code, message);
        }
        catch (JsonException)
        {
            if (httpStatus is HttpStatusCode.OK)
            {
                return Success(extension, (int)httpStatus, "Login successful", null, null);
            }

            return Failure(extension, (int)httpStatus, "Invalid server response");
        }
    }

    private static (string? DomainName, int? DomainPort) ParseLoginDomain(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        string? domainName = dataElement.TryGetProperty("domain_name", out var domainElement)
            ? domainElement.GetString()?.Trim()
            : null;

        int? domainPort = dataElement.TryGetProperty("domain_port", out var portElement)
            && portElement.TryGetInt32(out var parsedPort)
            && parsedPort > 0
            ? parsedPort
            : null;

        return (domainName, domainPort);
    }

    private static LoginResult Success(string extension, int code, string message, string? domainName, int? domainPort) =>
        new()
        {
            Success = true,
            Code = code,
            Message = message,
            Explanation = LoginErrorCatalog.Explain(code, message),
            Extension = extension,
            LoginDomainName = domainName,
            LoginDomainPort = domainPort
        };

    private static LoginResult Failure(string extension, int code, string message) =>
        new()
        {
            Success = false,
            Code = code,
            Message = message,
            Explanation = LoginErrorCatalog.Explain(code, message),
            Extension = extension
        };
}
