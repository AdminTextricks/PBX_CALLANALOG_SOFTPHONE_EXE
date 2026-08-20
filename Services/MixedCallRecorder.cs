using System.Collections.Concurrent;
using NAudio.Wave;

using CallAnalog.Softphone.Helpers;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Records a mixed mono WAV of microphone capture plus decoded remote playback PCM.
/// </summary>
public sealed class MixedCallRecorder : IDisposable
{
    private readonly WaveFormat _format = new(8000, 16, 1);
    private readonly ConcurrentQueue<byte[]> _remoteChunks = new();
    private WaveInEvent? _micInput;
    private WaveFileWriter? _writer;
    private string? _outputPath;
    private int _remoteSampleOffset;

    public string? OutputPath => _outputPath;

    public void Start(string outputPath, string? microphoneDevice, string? microphoneDeviceId)
    {
        Stop();

        _outputPath = outputPath;
        _writer = new WaveFileWriter(outputPath, _format);
        _remoteSampleOffset = 0;

        _micInput = new WaveInEvent { WaveFormat = _format };
        var deviceIndex = AudioDeviceHelper.FindInputDeviceIndex(microphoneDevice, microphoneDeviceId);
        if (deviceIndex >= 0)
        {
            _micInput.DeviceNumber = deviceIndex;
        }

        _micInput.DataAvailable += OnMicDataAvailable;
        _micInput.StartRecording();
    }

    public void TapRemotePcm(byte[] pcmBytes)
    {
        if (pcmBytes.Length == 0)
        {
            return;
        }

        var copy = new byte[pcmBytes.Length];
        Buffer.BlockCopy(pcmBytes, 0, copy, 0, pcmBytes.Length);
        _remoteChunks.Enqueue(copy);
    }

    public void Stop()
    {
        if (_micInput is not null)
        {
            _micInput.DataAvailable -= OnMicDataAvailable;
            _micInput.StopRecording();
            _micInput.Dispose();
            _micInput = null;
        }

        _writer?.Dispose();
        _writer = null;
        while (_remoteChunks.TryDequeue(out _))
        {
        }
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_writer is null || e.BytesRecorded <= 0)
        {
            return;
        }

        var mic = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, mic, 0, e.BytesRecorded);
        var remote = DequeueRemoteSamples(e.BytesRecorded);
        var mixed = AudioPcmHelper.MixPcm(mic, remote);
        _writer.Write(mixed, 0, mixed.Length);
    }

    private byte[] DequeueRemoteSamples(int byteCount)
    {
        var result = new byte[byteCount];
        var filled = 0;

        while (filled < byteCount && _remoteChunks.TryPeek(out var chunk))
        {
            var available = chunk.Length - _remoteSampleOffset;
            if (available <= 0)
            {
                _remoteChunks.TryDequeue(out _);
                _remoteSampleOffset = 0;
                continue;
            }

            var toCopy = Math.Min(byteCount - filled, available);
            Buffer.BlockCopy(chunk, _remoteSampleOffset, result, filled, toCopy);
            filled += toCopy;
            _remoteSampleOffset += toCopy;

            if (_remoteSampleOffset >= chunk.Length)
            {
                _remoteChunks.TryDequeue(out _);
                _remoteSampleOffset = 0;
            }
        }

        return result;
    }

    public void Dispose() => Stop();
}
