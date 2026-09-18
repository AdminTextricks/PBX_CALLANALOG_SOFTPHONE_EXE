using CallAnalog.Softphone.Helpers;
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
                AudioLifecycleLog.Write(
                    "WinMM_Preempt",
                    detail: $"fromOwner={_activeOwner} toOwner={owner} waveOut={FormatWaveOut(_activeOutput)}");
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
            AudioLifecycleLog.Write(
                "WinMM_Create",
                detail: $"owner={owner} waveOut={FormatWaveOut(output)} deviceIndex={deviceIndex} latency={desiredLatency} buffers={numberOfBuffers}");
            return output;
        }
    }

    public static void Release(string owner)
    {
        lock (Sync)
        {
            if (_activeOwner != owner)
            {
                AudioLifecycleLog.Write(
                    "WinMM_ReleaseSkipped",
                    detail: $"requestedOwner={owner} activeOwner={_activeOwner ?? "none"}");
                return;
            }

            AudioLifecycleLog.Write("WinMM_Release", detail: $"owner={owner}");
            DisposeActiveOutput();
        }
    }

    public static void ForceReleaseAll()
    {
        lock (Sync)
        {
            AudioLifecycleLog.Write("WinMM_ForceReleaseAll", detail: $"activeOwner={_activeOwner ?? "none"}");
            DisposeActiveOutput();
        }
    }

    internal static string DescribeForLog()
    {
        lock (Sync)
        {
            var count = _activeOutput is null ? 0 : 1;
            return $"winmmOwner={_activeOwner ?? "none"} waveOut={FormatWaveOut(_activeOutput)} winmmCount={count}";
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
        var waveOut = FormatWaveOut(_activeOutput);
        AudioLifecycleLog.Write("WinMM_StopEnter", detail: $"owner={owner} waveOut={waveOut}");
        try
        {
            _activeOutput.Stop();
            AudioLifecycleLog.Write("WinMM_StopExit", detail: $"owner={owner} waveOut={waveOut}");
        }
        catch (Exception ex)
        {
            AudioLifecycleLog.Write("WinMM_StopException", detail: $"owner={owner} waveOut={waveOut} ex={ex.GetType().Name}: {ex.Message}");
        }

        AudioLifecycleLog.Write("WinMM_DisposeEnter", detail: $"owner={owner} waveOut={waveOut}");
        try
        {
            _activeOutput.Dispose();
            AudioLifecycleLog.Write("WinMM_DisposeExit", detail: $"owner={owner} waveOut={waveOut}");
        }
        catch (Exception ex)
        {
            AudioLifecycleLog.Write("WinMM_DisposeException", detail: $"owner={owner} waveOut={waveOut} ex={ex.GetType().Name}: {ex.Message}");
        }

        _activeOutput = null;
        _activeOwner = null;
        App.SipLog.Info($"WinMM: released output device (was '{owner}').");
    }

    private static string FormatWaveOut(WaveOutEvent? output) =>
        output is null ? "none" : $"0x{output.GetHashCode():x8}";
}
