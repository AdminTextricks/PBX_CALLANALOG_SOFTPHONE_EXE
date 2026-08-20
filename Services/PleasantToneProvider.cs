using NAudio.Wave;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Soft dual-tone output for speaker tests and default ringtone playback.
/// </summary>
internal sealed class PleasantToneProvider : IWaveProvider
{
    private const int SampleRate = 8000;

    private readonly bool _ringPattern;
    private long _sampleIndex;

    public PleasantToneProvider(bool ringPattern = false)
    {
        _ringPattern = ringPattern;
        WaveFormat = new WaveFormat(SampleRate, 16, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var samples = count / 2;
        for (var i = 0; i < samples; i++)
        {
            var t = _sampleIndex / (double)SampleRate;
            double sample;

            if (_ringPattern)
            {
                // Gentle marimba-like two-note pattern (C5 / E5).
                var phase = (int)(t / 0.55) % 2;
                var freq = phase == 0 ? 523.25 : 659.25;
                var phaseT = t % 0.55;
                var envelope = Math.Min(1.0, phaseT / 0.04) * Math.Max(0, 1.0 - (phaseT / 0.55));
                sample = Math.Sin(2 * Math.PI * freq * t) * 0.22 * envelope;
            }
            else
            {
                // Warm major third for speaker test (C5 + E5 mix).
                var envelope = Math.Min(1.0, t / 0.08) * Math.Max(0.35, 1.0 - (t / 4.5));
                sample = (
                    Math.Sin(2 * Math.PI * 523.25 * t) * 0.14 +
                    Math.Sin(2 * Math.PI * 659.25 * t) * 0.10) * envelope;
            }

            var value = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            buffer[offset + (i * 2)] = (byte)(value & 0xFF);
            buffer[offset + (i * 2) + 1] = (byte)((value >> 8) & 0xFF);
            _sampleIndex++;
        }

        return count;
    }
}
