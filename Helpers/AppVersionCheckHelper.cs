using System.Text.Json;
using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Helpers;

public static class AppVersionCheckHelper
{
    public static string BuildRequestJson(string applicationKey, string version) =>
        $"{{\"application_key\":\"{Escape(applicationKey)}\",\"version\":\"{Escape(version)}\"}}";

    public static AppVersionCheckResult ParseResponse(JsonElement root, string fallbackInstalledVersion)
    {
        var data = root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object
            ? dataElement
            : root;

        var installed = GetString(data, "installed_version") ?? fallbackInstalledVersion;
        var current = GetString(data, "current_version") ?? installed;
        var updateAvailable = GetBool(data, "update_available")
            ?? IsNewerVersion(current, installed);

        DateTimeOffset? releaseDate = null;
        if (data.TryGetProperty("release_date", out var releaseElement)
            && releaseElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(releaseElement.GetString(), out var parsedRelease))
        {
            releaseDate = parsedRelease;
        }

        var message = root.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;

        return new AppVersionCheckResult
        {
            UpdateAvailable = updateAvailable,
            InstalledVersion = installed,
            CurrentVersion = current,
            ApplicationName = GetString(data, "application_name"),
            ReleaseStatus = GetString(data, "status"),
            ReleaseDate = releaseDate,
            Message = message
        };
    }

    public static bool IsNewerVersion(string candidate, string installed)
    {
        if (!TryParseVersion(candidate, out var candidateVersion)
            || !TryParseVersion(installed, out var installedVersion))
        {
            return !string.Equals(candidate.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return candidateVersion > installedVersion;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        if (Version.TryParse(NormalizeVersion(value), out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }

    private static string NormalizeVersion(string value)
    {
        var trimmed = value.Trim();
        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => $"{parts[0]}.0",
            2 => $"{parts[0]}.{parts[1]}",
            _ => string.Join('.', parts.Take(4))
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? GetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when property.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
