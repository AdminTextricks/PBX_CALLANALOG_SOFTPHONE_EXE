using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace CallAnalog.Softphone.Services;

public sealed class AudioDeviceChangeNotifier : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly NotificationClient _client;
    private bool _disposed;

    public AudioDeviceChangeNotifier(Action onDevicesChanged)
    {
        _client = new NotificationClient(onDevicesChanged);
        _enumerator.RegisterEndpointNotificationCallback(_client);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_client);
        }
        catch
        {
            // Best-effort cleanup.
        }

        _enumerator.Dispose();
    }

    private sealed class NotificationClient(Action onDevicesChanged) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) =>
            onDevicesChanged();

        public void OnDeviceAdded(string deviceId) => onDevicesChanged();

        public void OnDeviceRemoved(string deviceId) => onDevicesChanged();

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            onDevicesChanged();

        public void OnPropertyValueChanged(string deviceId, PropertyKey key) =>
            onDevicesChanged();
    }
}
