using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Lightweight capture DSP: noise gate, AGC, and simple echo ducking (no third-party APM dependency).
/// </summary>
internal sealed class CallVoiceProcessor
{
    /// <summary>RMS (~-40 dBFS) below which a frame is treated as room tone rather than speech.</summary>
    private const float SpeechFloor = 0.01f;

    private readonly VoiceEchoControl _echo;
    private readonly VoiceNoiseReduction _noise;
    private readonly bool _autoGain;
    private float _playbackEnergy;
    private float _agcGain = 1f;

    public CallVoiceProcessor(VoiceEchoControl echo, VoiceNoiseReduction noise, bool autoGain)
    {
        _echo = echo;
        _noise = noise;
        _autoGain = autoGain;
    }

    public void ObservePlaybackPcm(byte[] pcmBytes)
    {
        if (pcmBytes.Length < 2)
        {
            _playbackEnergy *= 0.9f;
            return;
        }

        _playbackEnergy = (0.85f * _playbackEnergy) + (0.15f * ComputeRms(pcmBytes));
    }

    public void ProcessCapturePcm(short[] samples)
    {
        if (samples.Length == 0)
        {
            return;
        }

        var rms = ComputeRms(samples);
        ApplyNoiseGate(samples, rms);
        ApplyEchoDuck(samples);

        // Re-measure: gating and ducking may have dropped the frame well below the speech floor.
        ApplyAgc(samples, ComputeRms(samples));
    }

    public static (int CaptureBufferMs, int PlaybackLatencyMs, int PlaybackBuffers) ResolveBuffers(VoiceQualityProfile profile) =>
        profile switch
        {
            VoiceQualityProfile.LowLatency => (10, 40, 2),
            VoiceQualityProfile.StableWifi => (40, 120, 4),
            _ => (20, 60, 3)
        };

    private void ApplyNoiseGate(short[] samples, float rms)
    {
        if (_noise == VoiceNoiseReduction.Off)
        {
            return;
        }

        var threshold = _noise == VoiceNoiseReduction.High ? 0.012f : 0.006f;
        if (rms >= threshold)
        {
            return;
        }

        var attenuate = _noise == VoiceNoiseReduction.High ? 0.05f : 0.2f;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(samples[i] * attenuate);
        }
    }

    private void ApplyEchoDuck(short[] samples)
    {
        if (_echo == VoiceEchoControl.Off || _playbackEnergy < 0.04f)
        {
            return;
        }

        var duck = _echo == VoiceEchoControl.Strong ? 0.15f : 0.45f;
        if (_echo == VoiceEchoControl.On && _playbackEnergy < 0.08f)
        {
            duck = 0.7f;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(samples[i] * duck);
        }
    }

    private void ApplyAgc(short[] samples, float rms)
    {
        if (!_autoGain)
        {
            return;
        }

        if (rms < SpeechFloor)
        {
            // Room tone, not speech. Amplifying it is what makes an idle mic sound like loud hiss
            // to the far end, so hold the gain and let it drift back to unity instead.
            _agcGain = (0.95f * _agcGain) + 0.05f;
        }
        else
        {
            const float target = 0.12f;
            var desired = Math.Clamp(target / rms, 0.5f, 3.0f);
            _agcGain = (0.9f * _agcGain) + (0.1f * desired);
        }

        if (Math.Abs(_agcGain - 1f) < 0.01f)
        {
            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i] * _agcGain;
            samples[i] = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
        }
    }

    private static float ComputeRms(byte[] pcmBytes)
    {
        double sum = 0;
        var count = pcmBytes.Length / 2;
        for (var i = 0; i < count; i++)
        {
            var sample = BitConverter.ToInt16(pcmBytes, i * 2) / 32768.0;
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / Math.Max(1, count));
    }

    private static float ComputeRms(short[] samples)
    {
        double sum = 0;
        foreach (var sample in samples)
        {
            var n = sample / 32768.0;
            sum += n * n;
        }

        return (float)Math.Sqrt(sum / Math.Max(1, samples.Length));
    }
}
