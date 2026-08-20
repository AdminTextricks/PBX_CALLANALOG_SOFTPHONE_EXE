namespace CallAnalog.Softphone.Models;

public sealed class SipRegistrationDigestCacheEntry
{
    public string Extension { get; set; } = string.Empty;

    public string Realm { get; set; } = string.Empty;

    public string Nonce { get; set; } = string.Empty;

    public string? Qop { get; set; }

    public string? Algorithm { get; set; }

    public string? Opaque { get; set; }

    public string? Cnonce { get; set; }

    public string? Nc { get; set; }

    public DateTimeOffset LastSuccessUtc { get; set; }

    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(Extension)
        && !string.IsNullOrWhiteSpace(Realm)
        && !string.IsNullOrWhiteSpace(Nonce);
}
