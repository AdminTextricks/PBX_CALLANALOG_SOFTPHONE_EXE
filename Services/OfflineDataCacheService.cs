using System.IO;
using System.Text.Json;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

public sealed class OfflineDataCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _cacheDirectory;

    public OfflineDataCacheService()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog",
            "offline-cache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public void SaveContacts(string extension, IReadOnlyList<Contact> contacts)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        var payload = new CachedPage<Contact>
        {
            Extension = extension.Trim(),
            SavedUtc = DateTimeOffset.UtcNow,
            Items = contacts.ToList()
        };

        Write(GetContactsPath(extension), payload);
    }

    public CachedPage<Contact>? LoadContacts(string extension)
    {
        return Load<CachedPage<Contact>>(GetContactsPath(extension));
    }

    public void SaveCallHistory(string extension, IReadOnlyList<CallRecord> records)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        var payload = new CachedPage<CallRecord>
        {
            Extension = extension.Trim(),
            SavedUtc = DateTimeOffset.UtcNow,
            Items = records.ToList()
        };

        Write(GetHistoryPath(extension), payload);
    }

    public CachedPage<CallRecord>? LoadCallHistory(string extension)
    {
        return Load<CachedPage<CallRecord>>(GetHistoryPath(extension));
    }

    private static string GetContactsPath(string extension) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog",
            "offline-cache",
            $"{SanitizeExtension(extension)}-contacts.json");

    private static string GetHistoryPath(string extension) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog",
            "offline-cache",
            $"{SanitizeExtension(extension)}-history.json");

    private static string SanitizeExtension(string extension) =>
        new(extension.Where(char.IsLetterOrDigit).ToArray());

    private static void Write<T>(string path, T payload) =>
        SettingsPersistenceHelper.WriteJsonAtomically(path, payload, JsonOptions);

    private static T? Load<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return default;
        }
    }
}

public sealed class CachedPage<T>
{
    public string Extension { get; set; } = string.Empty;
    public DateTimeOffset SavedUtc { get; set; }
    public List<T> Items { get; set; } = [];
}
