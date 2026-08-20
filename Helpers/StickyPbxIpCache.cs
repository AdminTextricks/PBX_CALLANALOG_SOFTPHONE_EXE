using System.IO;
using System.Net;
using System.Text.Json;

namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Sticky PBX IP cache: prefer last-good resolved IP for SIP connect until it fails or TTL expires.
/// Fail-open — cache is never authoritative without a successful REGISTER/OPTIONS path.
/// </summary>
public sealed class StickyPbxIpCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly string _cachePath;
    private readonly TimeSpan _ttl;
    private readonly object _sync = new();
    private StickyEntry? _entry;

    public StickyPbxIpCache(string storageDirectory, TimeSpan? ttl = null)
    {
        Directory.CreateDirectory(storageDirectory);
        _cachePath = Path.Combine(storageDirectory, "pbx-ip-cache.json");
        _ttl = ttl ?? DefaultTtl;
        _entry = Load();
    }

    public string? TryGetCachedIp(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        lock (_sync)
        {
            if (_entry is null
                || !string.Equals(_entry.Host, host.Trim(), StringComparison.OrdinalIgnoreCase)
                || DateTimeOffset.UtcNow - _entry.ResolvedUtc > _ttl
                || !IPAddress.TryParse(_entry.Ip, out _))
            {
                return null;
            }

            return _entry.Ip;
        }
    }

    public void RememberSuccess(string host, string ip)
    {
        if (string.IsNullOrWhiteSpace(host) || !IPAddress.TryParse(ip, out var address) || IPAddress.IsLoopback(address))
        {
            return;
        }

        lock (_sync)
        {
            _entry = new StickyEntry
            {
                Host = host.Trim(),
                Ip = address.ToString(),
                ResolvedUtc = DateTimeOffset.UtcNow
            };
            Save(_entry);
        }
    }

    public void Invalidate(string? host = null)
    {
        lock (_sync)
        {
            if (_entry is null)
            {
                return;
            }

            if (host is not null
                && !string.Equals(_entry.Host, host.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _entry = null;
            try
            {
                if (File.Exists(_cachePath))
                {
                    File.Delete(_cachePath);
                }
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    public static async Task<string?> ResolveHostAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        if (IPAddress.TryParse(host.Trim(), out var literal))
        {
            return literal.ToString();
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host.Trim(), cancellationToken);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return (ipv4 ?? addresses.FirstOrDefault())?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private StickyEntry? Load()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            var json = File.ReadAllText(_cachePath);
            return JsonSerializer.Deserialize<StickyEntry>(json);
        }
        catch
        {
            return null;
        }
    }

    private void Save(StickyEntry entry)
    {
        try
        {
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(entry));
        }
        catch
        {
            // Best-effort.
        }
    }

    private sealed class StickyEntry
    {
        public string Host { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public DateTimeOffset ResolvedUtc { get; set; }
    }
}
