using System.IO;
using CallAnalog.Softphone.Helpers;
using NAudio.Wave;

namespace CallAnalog.Softphone.Services;

public sealed class RingtoneService : IDisposable
{
    private WaveOutEvent? _player;
    private WaveStream? _reader;
    private IWaveProvider? _generatedProvider;
    private MeteringWaveProvider? _meteringProvider;
    private bool _loop;
    private string? _currentResolvedPath;
    private string? _currentDeviceName;
    private string? _currentDeviceId;

    public bool IsPlaying => _player?.PlaybackState == PlaybackState.Playing;

    public event EventHandler<double>? LevelChanged;

    public void Start(string? ringtonePath, string? outputDeviceName = null, string? outputDeviceId = null)
    {
        var resolvedPath = MediaFileStorage.ResolveRingtonePath(ringtonePath);
        if (IsPlaying
            && string.Equals(_currentResolvedPath, resolvedPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_currentDeviceName, outputDeviceName, StringComparison.Ordinal)
            && string.Equals(_currentDeviceId, outputDeviceId, StringComparison.Ordinal))
        {
            App.SipLog.Info("Ringtone: already playing same file on same device; skipping duplicate start.");
            return;
        }

        Stop();

        try
        {
            _loop = true;
            _currentResolvedPath = resolvedPath;
            _currentDeviceName = outputDeviceName;
            _currentDeviceId = outputDeviceId;

            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            {
                App.SipLog.Info($"Ringtone: playing custom file {resolvedPath}");
                _reader = AudioFilePlaybackHelper.OpenAudioFile(resolvedPath);
                _meteringProvider = new MeteringWaveProvider(_reader, OnLevelSampled);
                _player = CreateRingtoneOutput(_meteringProvider, outputDeviceName, outputDeviceId);
                App.SipLog.Info("Ringtone: custom file PCM stream feeding WaveOut.");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(ringtonePath))
                {
                    App.SipLog.Warn($"Ringtone: custom file not found at '{ringtonePath}'; using default tone.");
                }
                else
                {
                    App.SipLog.Info("Ringtone: no custom file configured; using default tone.");
                }

                _generatedProvider = CreateDefaultRingtoneProvider();
                _meteringProvider = new MeteringWaveProvider(_generatedProvider, OnLevelSampled);
                _player = CreateRingtoneOutput(_meteringProvider, outputDeviceName, outputDeviceId);
                App.SipLog.Info("Ringtone: generated default tone feeding WaveOut.");
            }

            _player.PlaybackStopped += OnPlaybackStopped;
            _player.Play();
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Ringtone playback failed: {ex.Message}");
            Stop();
        }
    }

    /// <summary>
    /// Immediately silences and tears down playback so buffered ring audio cannot bleed into a call.
    /// </summary>
    public void StopForAnswer()
    {
        _loop = false;
        DrainAndStop();
    }

    public void Stop() => DrainAndStop();

    public void Dispose() => DrainAndStop();

    private static WaveOutEvent CreateRingtoneOutput(
        IWaveProvider provider,
        string? outputDeviceName,
        string? outputDeviceId)
    {
        try
        {
            return WinMmPlaybackHelper.CreateWaveOutOutput(
                WinMmAudioOutputManager.OwnerRingtone,
                provider,
                outputDeviceName,
                outputDeviceId);
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(outputDeviceName) || !string.IsNullOrWhiteSpace(outputDeviceId))
        {
            App.SipLog.Warn($"Ringtone device unavailable ({ex.Message}); falling back to system default.");
            return WinMmPlaybackHelper.CreateWaveOutOutput(
                WinMmAudioOutputManager.OwnerRingtone,
                provider,
                null,
                null);
        }
    }

    private void OnLevelSampled(double level) =>
        LevelChanged?.Invoke(this, level);

    private void DrainAndStop()
    {
        _loop = false;

        if (_player is not null)
        {
            _player.PlaybackStopped -= OnPlaybackStopped;

            try
            {
                _player.Volume = 0f;
                _player.Stop();
            }
            catch
            {
                // Best-effort immediate silence.
            }

            WinMmAudioOutputManager.Release(WinMmAudioOutputManager.OwnerRingtone);
            _player = null;
        }

        if (_reader is not null)
        {
            if (_reader is AudioFileReader audioFileReader)
            {
                try
                {
                    audioFileReader.Volume = 0f;
                }
                catch
                {
                    // Best-effort.
                }
            }

            AudioFilePlaybackHelper.SafeDispose(_reader);
            _reader = null;
        }

        _generatedProvider = null;
        _meteringProvider = null;
        _currentResolvedPath = null;
        _currentDeviceName = null;
        _currentDeviceId = null;
        LevelChanged?.Invoke(this, 0);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!_loop || _player is null)
        {
            return;
        }

        try
        {
            if (_reader is not null)
            {
                _reader.Position = 0;
            }

            _player.Play();
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Ringtone loop playback failed: {ex.Message}");
            DrainAndStop();
        }
    }

    private static IWaveProvider CreateDefaultRingtoneProvider() =>
        new PleasantToneProvider(ringPattern: true);

    private sealed class MeteringWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;
        private readonly Action<double> _onLevel;

        public MeteringWaveProvider(IWaveProvider source, Action<double> onLevel)
        {
            _source = source;
            _onLevel = onLevel;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read <= 0)
            {
                _onLevel(0);
                return read;
            }

            double sum = 0;
            var sampleCount = read / 2;
            for (var i = 0; i < sampleCount; i++)
            {
                var sampleIndex = offset + (i * 2);
                var sample = BitConverter.ToInt16(buffer, sampleIndex);
                var normalized = sample / 32768.0;
                sum += normalized * normalized;
            }

            var rms = Math.Sqrt(sum / Math.Max(1, sampleCount));
            _onLevel(Math.Min(1.0, rms * 2.5));
            return read;
        }
    }
}
