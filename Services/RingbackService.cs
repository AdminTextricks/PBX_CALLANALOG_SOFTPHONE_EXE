using NAudio.Wave;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Local outbound ringback tone while waiting for the remote party to answer.
/// </summary>
public sealed class RingbackService : IDisposable
{
    private WaveOutEvent? _player;
    private IWaveProvider? _provider;

    public bool IsPlaying => _player?.PlaybackState == PlaybackState.Playing;

    public void Start(string? outputDeviceName = null, string? outputDeviceId = null)
    {
        Stop();

        try
        {
            _provider = new PleasantToneProvider(ringPattern: true);
            _player = WinMmPlaybackHelper.CreateWaveOutOutput(
                WinMmAudioOutputManager.OwnerRingback,
                _provider,
                outputDeviceName,
                outputDeviceId);
            _player.Play();
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Ringback playback failed: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        if (_player is not null)
        {
            try
            {
                _player.Stop();
            }
            catch
            {
                // Best-effort.
            }

            WinMmAudioOutputManager.Release(WinMmAudioOutputManager.OwnerRingback);
            _player = null;
        }

        _provider = null;
    }

    public void Dispose() => Stop();
}
