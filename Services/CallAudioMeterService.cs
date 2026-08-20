using CallAnalog.Softphone.Helpers;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Voice level meters fed from the live call RTP audio path (no separate WASAPI capture —
/// that conflicts with WinMM mic used during calls).
/// </summary>
public sealed class CallAudioMeterService : IDisposable
{
    private volatile float _incomingLevel;
    private volatile float _outgoingLevel;

    public double IncomingLevel => _incomingLevel;
    public double OutgoingLevel => _outgoingLevel;

    public event EventHandler? LevelsUpdated;

    public void Start(string? microphoneDevice, string? speakerDevice, string? microphoneDeviceId = null, string? speakerDeviceId = null)
    {
        // Levels are driven by SipService playback/capture PCM taps during active calls.
        _ = microphoneDevice;
        _ = speakerDevice;
        _ = microphoneDeviceId;
        _ = speakerDeviceId;
    }

    public void FeedIncomingPcm(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded <= 0)
        {
            return;
        }

        _incomingLevel = Math.Min(1f, AudioPcmHelper.ComputeRms(buffer, bytesRecorded) * 4f);
        LevelsUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void FeedOutgoingPcm(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded <= 0)
        {
            return;
        }

        _outgoingLevel = Math.Min(1f, AudioPcmHelper.ComputeRms(buffer, bytesRecorded) * 4f);
        LevelsUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _incomingLevel = 0;
        _outgoingLevel = 0;
    }

    public static void SetOutputVolume(string? speakerDevice, double volumeScalar, string? speakerDeviceId = null)
    {
        try
        {
            var device = AudioDeviceHelper.GetRenderDevice(speakerDevice, speakerDeviceId);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(volumeScalar, 0, 1);
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Failed to set output volume: {ex.Message}");
        }
    }

    public void Dispose() => Stop();
}
