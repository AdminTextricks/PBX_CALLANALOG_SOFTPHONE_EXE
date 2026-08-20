using System.IO;
using System.Text.Json;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

public sealed class SipRegistrationAuthCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);

    private readonly string _cachePath;
    private readonly object _sync = new();
    private Dictionary<string, SipRegistrationDigestCacheEntry> _entries = new(StringComparer.Ordinal);

    public SipRegistrationAuthCacheService(string storageDirectory)
    {
        Directory.CreateDirectory(storageDirectory);
        _cachePath = Path.Combine(storageDirectory, "sip-registration-auth.json");
        LoadFromDisk();
    }

    public SipRegistrationDigestCacheEntry? TryGet(string extension, string? realm = null)
    {
        lock (_sync)
        {
            var key = BuildKey(extension, realm);
            if (!_entries.TryGetValue(key, out var entry) || !entry.IsUsable)
            {
                return null;
            }

            if (DateTimeOffset.UtcNow - entry.LastSuccessUtc > CacheMaxAge)
            {
                _entries.Remove(key);
                PersistLocked();
                return null;
            }

            return entry;
        }
    }

    public void Save(SipRegistrationDigestCacheEntry entry)
    {
        if (!entry.IsUsable)
        {
            return;
        }

        entry.Extension = entry.Extension.Trim();
        entry.Realm = entry.Realm.Trim();
        entry.LastSuccessUtc = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            _entries[BuildKey(entry.Extension, entry.Realm)] = entry;
            PersistLocked();
        }
    }

    public void Clear(string extension)
    {
        var trimmed = extension.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        lock (_sync)
        {
            var removed = _entries.Keys.Where(key => key.StartsWith(trimmed + "|", StringComparison.Ordinal)).ToList();
            if (removed.Count == 0)
            {
                return;
            }

            foreach (var key in removed)
            {
                _entries.Remove(key);
            }

            PersistLocked();
        }
    }

    private static string BuildKey(string extension, string? realm)
    {
        var trimmedExtension = extension.Trim();
        var trimmedRealm = string.IsNullOrWhiteSpace(realm) ? string.Empty : realm.Trim();
        return $"{trimmedExtension}|{trimmedRealm}";
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_cachePath))
        {
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<SipRegistrationDigestCacheEntry>>(
                File.ReadAllText(_cachePath),
                JsonOptions);
            if (loaded is null)
            {
                return;
            }

            _entries = loaded
                .Where(entry => entry.IsUsable)
                .ToDictionary(entry => BuildKey(entry.Extension, entry.Realm), StringComparer.Ordinal);
        }
        catch
        {
            _entries = new Dictionary<string, SipRegistrationDigestCacheEntry>(StringComparer.Ordinal);
        }
    }

    private void PersistLocked()
    {
        SettingsPersistenceHelper.WriteJsonAtomically(_cachePath, _entries.Values.ToList(), JsonOptions);
    }
}
