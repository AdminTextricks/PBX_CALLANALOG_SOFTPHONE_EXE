using System.Runtime.CompilerServices;
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
    private static readonly object NativeSync = new();
    private static readonly ConditionalWeakTable<WaveOutEvent, DisposeState> _disposeStates = new();
    private static WaveOutEvent? _activeOutput;
    private static string? _activeOwner;

    public static WaveOutEvent CreateWaveOut(
        string owner,
        IWaveProvider provider,
        int deviceIndex,
        int desiredLatency = 100,
        int numberOfBuffers = 3)
    {
        WaveOutEvent? preempted = null;
        string? preemptedOwner = null;

        try
        {
            WaveOutEvent output;
            lock (Sync)
            {
                if (_activeOutput is not null)
                {
                    App.SipLog.Info($"WinMM: releasing '{_activeOwner}' so '{owner}' can use the output device.");
                    AudioLifecycleLog.Write(
                        "WinMM_Preempt",
                        detail: $"fromOwner={_activeOwner} toOwner={owner} waveOut={FormatWaveOut(_activeOutput)}");
                    preempted = DetachActiveUnlocked(out preemptedOwner);
                }

                output = new WaveOutEvent
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
            }

            if (owner == OwnerCallPlayback && preempted is not null)
            {
                DisposeInstance(preempted, preemptedOwner ?? owner);
                preempted = null;
                preemptedOwner = null;
            }

            lock (NativeSync)
            {
                output.Init(provider);
            }

            lock (Sync)
            {
                _activeOutput = output;
                _activeOwner = owner;
                AudioLifecycleLog.Write(
                    "WinMM_Create",
                    detail: $"owner={owner} waveOut={FormatWaveOut(output)} deviceIndex={deviceIndex} latency={desiredLatency} buffers={numberOfBuffers}");
            }

            return output;
        }
        finally
        {
            QueueDisposeCaptured(preempted, preemptedOwner);
        }
    }

    public static void Release(string owner)
    {
        WaveOutEvent? captured;
        string? capturedOwner;
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
            captured = DetachActiveUnlocked(out capturedOwner);
        }

        QueueDisposeCaptured(captured, capturedOwner);
    }

    public static void ForceReleaseAll()
    {
        WaveOutEvent? captured;
        string? capturedOwner;
        lock (Sync)
        {
            AudioLifecycleLog.Write("WinMM_ForceReleaseAll", detail: $"activeOwner={_activeOwner ?? "none"}");
            captured = DetachActiveUnlocked(out capturedOwner);
        }

        QueueDisposeCaptured(captured, capturedOwner);
    }

    /// <summary>
    /// Stops and disposes <paramref name="output"/> on the calling thread.
    /// Clears the active slot only when it still points at this instance.
    /// </summary>
    internal static void DisposeInstance(WaveOutEvent output, string owner, Action? beforeDispose = null)
    {
        lock (Sync)
        {
            if (ReferenceEquals(_activeOutput, output))
            {
                _activeOutput = null;
                _activeOwner = null;
            }
        }

        if (!TryClaimDispose(output, out var state))
        {
            state.Completed.Wait();
            return;
        }

        RunDispose(output, owner, state, beforeDispose);
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

    private static WaveOutEvent? DetachActiveUnlocked(out string? owner)
    {
        owner = _activeOwner;
        var output = _activeOutput;
        _activeOutput = null;
        _activeOwner = null;
        return output;
    }

    private static void QueueDisposeCaptured(WaveOutEvent? output, string? owner)
    {
        if (output is null)
        {
            return;
        }

        if (!TryClaimDispose(output, out var state))
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => RunDispose(output, owner, state, null));
    }

    private static bool TryClaimDispose(WaveOutEvent output, out DisposeState state)
    {
        state = _disposeStates.GetOrCreateValue(output);
        lock (state)
        {
            if (state.Started)
            {
                return false;
            }

            state.Started = true;
            return true;
        }
    }

    private static void RunDispose(WaveOutEvent output, string? owner, DisposeState state, Action? beforeDispose)
    {
        lock (NativeSync)
        {
            try
            {
                beforeDispose?.Invoke();
            }
            finally
            {
                try
                {
                    DisposeCaptured(output, owner);
                }
                finally
                {
                    state.Completed.Set();
                }
            }
        }
    }

    private static void DisposeCaptured(WaveOutEvent output, string? owner)
    {
        var waveOut = FormatWaveOut(output);
        AudioLifecycleLog.Write("WinMM_StopEnter", detail: $"owner={owner} waveOut={waveOut}");
        try
        {
            output.Stop();
            AudioLifecycleLog.Write("WinMM_StopExit", detail: $"owner={owner} waveOut={waveOut}");
        }
        catch (Exception ex)
        {
            AudioLifecycleLog.Write("WinMM_StopException", detail: $"owner={owner} waveOut={waveOut} ex={ex.GetType().Name}: {ex.Message}");
        }

        AudioLifecycleLog.Write("WinMM_DisposeEnter", detail: $"owner={owner} waveOut={waveOut}");
        try
        {
            output.Dispose();
            AudioLifecycleLog.Write("WinMM_DisposeExit", detail: $"owner={owner} waveOut={waveOut}");
        }
        catch (Exception ex)
        {
            AudioLifecycleLog.Write("WinMM_DisposeException", detail: $"owner={owner} waveOut={waveOut} ex={ex.GetType().Name}: {ex.Message}");
        }

        App.SipLog.Info($"WinMM: released output device (was '{owner}').");
    }

    private static string FormatWaveOut(WaveOutEvent? output) =>
        output is null ? "none" : $"0x{output.GetHashCode():x8}";

    private sealed class DisposeState
    {
        public bool Started;
        public readonly ManualResetEventSlim Completed = new(false);
    }
}
