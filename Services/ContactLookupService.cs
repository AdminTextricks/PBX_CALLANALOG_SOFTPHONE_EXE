using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

public sealed class ContactLookupService
{
    private readonly OfflineDataCacheService _cache;

    public ContactLookupService(OfflineDataCacheService? cache = null)
    {
        _cache = cache ?? new OfflineDataCacheService();
    }

    public string? ResolveName(string extension, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var normalizedTarget = NormalizeDigits(phoneNumber);
        if (normalizedTarget.Length == 0)
        {
            return null;
        }

        var cached = _cache.LoadContacts(extension);
        if (cached?.Items is null || cached.Items.Count == 0)
        {
            return null;
        }

        foreach (var contact in cached.Items)
        {
            if (string.IsNullOrWhiteSpace(contact.Name))
            {
                continue;
            }

            var contactDigits = NormalizeDigits(contact.Number);
            if (contactDigits.Length == 0)
            {
                continue;
            }

            if (contactDigits == normalizedTarget
                || normalizedTarget.EndsWith(contactDigits, StringComparison.Ordinal)
                || contactDigits.EndsWith(normalizedTarget, StringComparison.Ordinal))
            {
                return contact.Name.Trim();
            }
        }

        return null;
    }

    public void UpdateCache(string extension, IReadOnlyList<Contact> contacts) =>
        _cache.SaveContacts(extension, contacts);

    private static string NormalizeDigits(string value) =>
        new string(value.Where(char.IsDigit).ToArray());
}
