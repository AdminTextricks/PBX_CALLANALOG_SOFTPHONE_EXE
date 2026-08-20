using NAudio.Wave;

namespace CallAnalog.Softphone.Services;

public sealed class AudioTestService : IDisposable
{
    private const int MaxTestDurationMs = 5000;

    private WaveOutEvent? _waveOut;
    private WaveInEvent? _waveIn;
    private CancellationTokenSource? _speakerAutoStop;
    private CancellationTokenSource? _micMonitor;

    public bool IsSpeakerPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
    public bool IsMicMonitoring => _waveIn is not null;

    public void StartSpeakerTest(string? deviceName, double volume, string? deviceId = null)
    {
        StopSpeaker();

        try
        {
            var signal = new PleasantToneProvider(ringPattern: false);

            _waveOut = WinMmPlaybackHelper.CreateWaveOutOutput(
                WinMmAudioOutputManager.OwnerAudioTest,
                signal,
                deviceName,
                deviceId);
            _waveOut.Volume = 1f;
            _waveOut.Play();

            App.SipLog.Info($"Speaker test started (WinMM, volume {volume:P0}).");

            _speakerAutoStop = new CancellationTokenSource();
            _ = AutoStopSpeakerAsync(_speakerAutoStop.Token);
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Speaker test failed: {ex.Message}");
            StopSpeaker();
            throw;
        }
    }

    public void StartMicrophoneMonitor(string? deviceName, double volume, Action<float> onLevel, string? deviceId = null)
    {
        StopMicrophone();

        try
        {
            var deviceIndex = AudioDeviceHelper.FindInputDeviceIndex(deviceName, deviceId);
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(8000, 16, 1),
                BufferMilliseconds = 50
            };
            if (deviceIndex >= 0)
            {
                _waveIn.DeviceNumber = deviceIndex;
            }

            _waveIn.DataAvailable += (_, args) =>
            {
                var peak = CalculatePeak(args.Buffer, args.BytesRecorded, volume);
                onLevel(peak);
            };

            _waveIn.StartRecording();
            _micMonitor = new CancellationTokenSource();
            _ = AutoStopMicrophoneAsync(_micMonitor.Token);
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Microphone test failed: {ex.Message}");
            StopMicrophone();
            throw;
        }
    }

    public void StopSpeaker()
    {
        _speakerAutoStop?.Cancel();
        _speakerAutoStop?.Dispose();
        _speakerAutoStop = null;

        if (_waveOut is null)
        {
            return;
        }

        try
        {
            _waveOut.Stop();
        }
        catch
        {
            // Best-effort.
        }

        WinMmAudioOutputManager.Release(WinMmAudioOutputManager.OwnerAudioTest);
        _waveOut = null;
    }

    public void StopMicrophone()
    {
        _micMonitor?.Cancel();
        _micMonitor?.Dispose();
        _micMonitor = null;

        if (_waveIn is null)
        {
            return;
        }

        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;
    }

    public void StopAll()
    {
        StopSpeaker();
        StopMicrophone();
    }

    public void Dispose() => StopAll();

    private async Task AutoStopSpeakerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(MaxTestDurationMs, cancellationToken);
            StopSpeaker();
        }
        catch (OperationCanceledException)
        {
            // Expected when stopped manually.
        }
    }

    private async Task AutoStopMicrophoneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(MaxTestDurationMs, cancellationToken);
            StopMicrophone();
        }
        catch (OperationCanceledException)
        {
            // Expected when stopped manually.
        }
    }

    private static float CalculatePeak(byte[] buffer, int bytesRecorded, double volume)
    {
        var peak = 0f;
        for (var i = 0; i < bytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(buffer, i) / 32768f * (float)volume;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return Math.Clamp(peak, 0f, 1f);
    }
}

