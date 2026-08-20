using NAudio.Wave;

namespace CallAnalog.Softphone.Helpers;

internal static class PcmFormatConverter
{
    /// <summary>Fraction of the target sample rate kept when downsampling (3.4 kHz for an 8 kHz codec).</summary>
    private const double AntiAliasCutoffRatio = 0.425;

    internal static short[] ConvertToPcm16(
        byte[] buffer,
        int bytesRecorded,
        WaveFormat sourceFormat,
        WaveFormat targetFormat,
        AntiAliasLowPassFilter? antiAliasFilter = null)
    {
        if (bytesRecorded <= 0)
        {
            return [];
        }

        var sourceSamples = ToMonoPcm16(buffer, bytesRecorded, sourceFormat);
        if (sourceFormat.SampleRate == targetFormat.SampleRate)
        {
            return sourceSamples;
        }

        if (antiAliasFilter is not null && targetFormat.SampleRate < sourceFormat.SampleRate)
        {
            antiAliasFilter.Configure(sourceFormat.SampleRate, targetFormat.SampleRate * AntiAliasCutoffRatio);
            antiAliasFilter.ProcessInPlace(sourceSamples);
        }

        return ResampleLinear(sourceSamples, sourceFormat.SampleRate, targetFormat.SampleRate);
    }

    /// <summary>
    /// Downmixes a capture buffer to mono 16-bit PCM. WASAPI shared mode hands back the device mix
    /// format, which on Windows is normally 32-bit IEEE float, so this has to decode by encoding and
    /// bit depth rather than assuming 16-bit PCM.
    /// </summary>
    internal static short[] ToMonoPcm16(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        var resolved = ResolveFormat(format);
        var channels = Math.Max(1, resolved.Channels);
        var bytesPerSample = Math.Max(1, resolved.BitsPerSample / 8);
        var blockAlign = bytesPerSample * channels;
        if (blockAlign <= 0)
        {
            return [];
        }

        var frameCount = bytesRecorded / blockAlign;
        if (frameCount <= 0)
        {
            return [];
        }

        var mono = new short[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * blockAlign;
            double total = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                total += ReadSample(buffer, offset + (channel * bytesPerSample), resolved.Encoding, resolved.BitsPerSample);
            }

            mono[frame] = (short)Math.Clamp(Math.Round(total / channels), short.MinValue, short.MaxValue);
        }

        return mono;
    }

    /// <summary>Resolves WAVE_FORMAT_EXTENSIBLE (what the mix format usually is) to plain PCM or float.</summary>
    private static WaveFormat ResolveFormat(WaveFormat format)
    {
        if (format is WaveFormatExtensible extensible)
        {
            try
            {
                return extensible.ToStandardWaveFormat();
            }
            catch
            {
                // Unknown subformat — fall through and use the reported bit depth.
            }
        }

        return format;
    }

    /// <summary>Reads one sample and scales it to the 16-bit PCM range.</summary>
    private static double ReadSample(byte[] buffer, int offset, WaveFormatEncoding encoding, int bitsPerSample)
    {
        if (encoding == WaveFormatEncoding.IeeeFloat)
        {
            var value = bitsPerSample == 64
                ? BitConverter.ToDouble(buffer, offset)
                : BitConverter.ToSingle(buffer, offset);
            return Math.Clamp(value, -1.0, 1.0) * short.MaxValue;
        }

        return bitsPerSample switch
        {
            8 => (buffer[offset] - 128) * 256, // 8-bit PCM is unsigned.
            16 => BitConverter.ToInt16(buffer, offset),
            24 => ((buffer[offset] | (buffer[offset + 1] << 8) | ((sbyte)buffer[offset + 2] << 16))) / 256.0,
            32 => BitConverter.ToInt32(buffer, offset) / 65536.0,
            _ => 0
        };
    }

    private static short[] ResampleLinear(short[] input, int sourceRate, int targetRate)
    {
        if (input.Length == 0 || sourceRate <= 0 || targetRate <= 0)
        {
            return input;
        }

        var outputLength = (int)((long)input.Length * targetRate / sourceRate);
        if (outputLength <= 0)
        {
            return [];
        }

        var output = new short[outputLength];
        for (var i = 0; i < outputLength; i++)
        {
            var srcPos = i * (double)(input.Length - 1) / Math.Max(1, outputLength - 1);
            var index = (int)srcPos;
            var frac = srcPos - index;
            var s0 = input[index];
            var s1 = input[Math.Min(index + 1, input.Length - 1)];
            output[i] = (short)(s0 + ((s1 - s0) * frac));
        }

        return output;
    }
}
