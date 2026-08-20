using NAudio.Wave;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// MicroSIP-style playback through WinMM WaveOut (Windows default audio device when index is -1).
/// </summary>
internal static class WinMmPlaybackHelper
{
    public static WaveOutEvent CreateWaveOutOutput(
        string owner,
        IWaveProvider provider,
        string? deviceName,
        string? deviceId = null,
        int desiredLatency = 100)
    {
        var deviceIndex = AudioDeviceHelper.FindOutputDeviceIndexForSip(deviceName, deviceId);
        return WinMmAudioOutputManager.CreateWaveOut(owner, provider, deviceIndex, desiredLatency);
    }
}
