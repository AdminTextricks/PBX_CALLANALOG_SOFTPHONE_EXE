using NAudio.Wave;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Serializes WinMM WaveOut access so ringtone, hold music, ringback, and call playback
/// never open competing WaveOut instances on the same output device.
/// </summary>
internal static class WinMmAudioOutputManager
{
    public const string OwnerCallPlayback = "CallPlayback";
    public const string OwnerRingtone = "Ringtone";
    public const string OwnerHoldMusic = "HoldMusic";
    public const string OwnerRingback = "Ringback";
    public const string OwnerAudioTest = "AudioTest";

    private static readonly object Sync = new();
    private static WaveOutEvent? _activeOutput;
    private static string? _activeOwner;

    public static WaveOutEvent CreateWaveOut(
        string owner,
        IWaveProvider provider,
        int deviceIndex,
        int desiredLatency = 100,
        int numberOfBuffers = 3)
    {
        lock (Sync)
        {
            if (_activeOutput is not null)
            {
                App.SipLog.Info($"WinMM: releasing '{_activeOwner}' so '{owner}' can use the output device.");
                DisposeActiveOutput();
            }

            var output = new WaveOutEvent
            {
                DesiredLatency = desiredLatency,
                NumberOfBuffers = numberOfBuffers
            };

            if (deviceIndex >= 0)
            {
                output.DeviceNumber = deviceIndex;
                App.SipLog.Info($"WinMM playback device index: {deviceIndex} (owner: {owner})");
            }
            else
            {
                App.SipLog.Info($"WinMM playback using Windows default audio device (owner: {owner}).");
            }

            output.Init(provider);
            _activeOutput = output;
            _activeOwner = owner;
            return output;
        }
    }

    public static void Release(string owner)
    {
        lock (Sync)
        {
            if (_activeOwner != owner)
            {
                return;
            }

            DisposeActiveOutput();
        }
    }

    public static void ForceReleaseAll()
    {
        lock (Sync)
        {
            DisposeActiveOutput();
        }
    }

    public static bool IsOwnedBy(string owner)
    {
        lock (Sync)
        {
            return _activeOwner == owner;
        }
    }

    private static void DisposeActiveOutput()
    {
        if (_activeOutput is null)
        {
            return;
        }

        var owner = _activeOwner;
        try
        {
            _activeOutput.Stop();
        }
        catch
        {
            // Best-effort immediate silence.
        }

        try
        {
            _activeOutput.Dispose();
        }
        catch
        {
            // Best-effort teardown.
        }

        _activeOutput = null;
        _activeOwner = null;
        App.SipLog.Info($"WinMM: released output device (was '{owner}').");
    }
}
