using NAudio.Wave;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Local outbound ringback tone while waiting for the remote party to answer.
/// </summary>
public sealed class RingbackService : IDisposable
{
    private readonly object _gate = new();
    private int _generation;
    private WaveOutEvent? _player;
    private IWaveProvider? _provider;

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _player?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    public void Start(string? outputDeviceName = null, string? outputDeviceId = null)
    {
        int generation;
        WaveOutEvent? previous;
        lock (_gate)
        {
            _generation++;
            generation = _generation;
            previous = _player;
            _player = null;
            _provider = null;
        }

        if (previous is not null)
        {
            TearDown(previous);
        }

        WaveOutEvent? created = null;
        WaveOutEvent? published = null;
        try
        {
            var provider = new PleasantToneProvider(ringPattern: true);
            created = WinMmPlaybackHelper.CreateWaveOutOutput(
                WinMmAudioOutputManager.OwnerRingback,
                provider,
                outputDeviceName,
                outputDeviceId);

            var play = false;
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _provider = provider;
                    _player = created;
                    published = created;
                    created = null;
                    play = true;
                }
            }

            if (!play)
            {
                if (created is not null)
                {
                    TearDown(created);
                }

                return;
            }

            lock (_gate)
            {
                if (generation == _generation && ReferenceEquals(_player, published))
                {
                    _player.Play();
                }
            }
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Ringback playback failed: {ex.Message}");
            WaveOutEvent? mine = created;
            lock (_gate)
            {
                if (published is not null
                    && generation == _generation
                    && ReferenceEquals(_player, published))
                {
                    _player = null;
                    _provider = null;
                    mine = published;
                }
            }

            if (mine is not null)
            {
                TearDown(mine);
            }
        }
    }

    public void Stop()
    {
        WaveOutEvent? player;
        lock (_gate)
        {
            _generation++;
            player = _player;
            _player = null;
            _provider = null;
        }

        if (player is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => TearDown(player));
        }
    }

    public void Dispose() => Stop();

    private static void TearDown(WaveOutEvent player)
    {
        try
        {
            player.Stop();
        }
        catch
        {
            // Best-effort.
        }

        WinMmAudioOutputManager.DisposeInstance(
            player,
            WinMmAudioOutputManager.OwnerRingback);
    }
}
