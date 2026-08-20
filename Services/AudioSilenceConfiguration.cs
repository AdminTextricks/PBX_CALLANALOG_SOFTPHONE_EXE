using SIPSorceryMedia.Abstractions;

namespace CallAnalog.Softphone.Services;

internal static class AudioSilenceConfiguration
{
    internal readonly record struct SilenceSpec(bool ZeroFill, byte SilenceByte);

    public static SilenceSpec GetSilenceSpec(AudioFormat format)
    {
        var formatName = format.FormatName ?? string.Empty;
        if (format.FormatID == 9 || formatName.Contains("G722", StringComparison.OrdinalIgnoreCase))
        {
            return new SilenceSpec(true, 0);
        }

        var silenceByte = formatName.Contains("PCMA", StringComparison.OrdinalIgnoreCase)
            || format.FormatID == 8
            ? (byte)0xD5
            : (byte)0xFF;
        return new SilenceSpec(false, silenceByte);
    }

    public static byte[] CreateSilenceBuffer(int length, SilenceSpec spec)
    {
        var silence = new byte[length];
        if (!spec.ZeroFill)
        {
            Array.Fill(silence, spec.SilenceByte);
        }

        return silence;
    }
}
