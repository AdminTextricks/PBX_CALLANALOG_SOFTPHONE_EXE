using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace CallAnalog.Softphone.Services;

public static class CodecConfiguration
{
    public const string Pcmu = "PCMU";
    public const string Pcma = "PCMA";
    public const string Opus = "OPUS";

    /// <summary>Codecs the softphone can request. Opus is offered when Prefer Opus is enabled and the encoder supports it.</summary>
    public static readonly IReadOnlyList<string> SupportedCodecIds = [Pcmu, Pcma, Opus];

    public static IReadOnlyList<string> DefaultEnabledCodecs => [Pcmu, Pcma];

    public static IReadOnlyList<string> NormalizeEnabledCodecs(IEnumerable<string>? codecs)
    {
        if (codecs is null)
        {
            return DefaultEnabledCodecs.ToList();
        }

        var normalized = new List<string>();
        foreach (var codec in codecs)
        {
            if (codec.Equals("G711", StringComparison.OrdinalIgnoreCase))
            {
                if (!normalized.Contains(Pcmu, StringComparer.OrdinalIgnoreCase))
                {
                    normalized.Add(Pcmu);
                }

                if (!normalized.Contains(Pcma, StringComparer.OrdinalIgnoreCase))
                {
                    normalized.Add(Pcma);
                }

                continue;
            }

            var id = NormalizeCodecId(codec);
            if (id is not null && !normalized.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(id);
            }
        }

        return normalized.Count > 0 ? normalized : DefaultEnabledCodecs.ToList();
    }

    public static IReadOnlyList<string> BuildEnabledCodecs(IEnumerable<string>? codecs, bool preferOpus)
    {
        var enabled = NormalizeEnabledCodecs(codecs).ToList();
        if (preferOpus && !enabled.Contains(Opus, StringComparer.OrdinalIgnoreCase))
        {
            enabled.Insert(0, Opus);
        }
        else if (!preferOpus)
        {
            enabled.RemoveAll(c => c.Equals(Opus, StringComparison.OrdinalIgnoreCase));
        }

        if (!enabled.Any(c => c.Equals(Pcmu, StringComparison.OrdinalIgnoreCase) || c.Equals(Pcma, StringComparison.OrdinalIgnoreCase)))
        {
            enabled.Add(Pcmu);
            enabled.Add(Pcma);
        }

        return enabled;
    }

    public static AudioEncoder CreateEncoder(IReadOnlyList<string> enabledCodecs)
    {
        var enabled = NormalizeEnabledCodecs(enabledCodecs);
        var includeG711 = enabled.Any(codec =>
            codec.Equals(Pcmu, StringComparison.OrdinalIgnoreCase)
            || codec.Equals(Pcma, StringComparison.OrdinalIgnoreCase)
            || codec.Equals("G711", StringComparison.OrdinalIgnoreCase));

        if (!includeG711)
        {
            includeG711 = true;
        }

        // SIPSorcery AudioEncoder(includeG711, includeG722) — G.722 off; Opus offered via format filter when present.
        return new AudioEncoder(includeG711, false);
    }

    public static HashSet<int> GetNegotiableRtpPayloadIds(IReadOnlyList<string> enabledCodecs)
    {
        var ids = new HashSet<int>();
        var enabled = NormalizeEnabledCodecs(enabledCodecs);
        foreach (var codec in enabled)
        {
            if (codec.Equals(Pcmu, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(0);
            }
            else if (codec.Equals(Pcma, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(8);
            }
            else if (codec.Equals(Opus, StringComparison.OrdinalIgnoreCase))
            {
                // Common dynamic Opus payload types; also matched by name in IsFormatAllowed.
                ids.Add(111);
                ids.Add(96);
                ids.Add(97);
            }
        }

        if (ids.Count == 0)
        {
            ids.Add(0);
            ids.Add(8);
        }

        return ids;
    }

    public static bool IsFormatAllowed(AudioFormat format, HashSet<int> allowedPayloadIds)
    {
        if (allowedPayloadIds.Contains(format.FormatID))
        {
            return true;
        }

        var name = format.FormatName ?? string.Empty;
        if (name.Contains("PCMU", StringComparison.OrdinalIgnoreCase) || name.Contains("ULAW", StringComparison.OrdinalIgnoreCase))
        {
            return allowedPayloadIds.Contains(0);
        }

        if (name.Contains("PCMA", StringComparison.OrdinalIgnoreCase) || name.Contains("ALAW", StringComparison.OrdinalIgnoreCase))
        {
            return allowedPayloadIds.Contains(8);
        }

        if (name.Contains("OPUS", StringComparison.OrdinalIgnoreCase))
        {
            return allowedPayloadIds.Contains(111)
                || allowedPayloadIds.Contains(96)
                || allowedPayloadIds.Contains(97)
                || allowedPayloadIds.Contains(format.FormatID);
        }

        return false;
    }

    private static string? NormalizeCodecId(string codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return null;
        }

        var value = codec.Trim().ToUpperInvariant();
        return value switch
        {
            "G711" or "G711U" or "G711ULAW" or "ULAW" or "PCMU" => Pcmu,
            "G711A" or "G711ALAW" or "ALAW" or "PCMA" => Pcma,
            "OPUS" => Opus,
            _ => value is Pcmu or Pcma or Opus ? value : null
        };
    }
}
