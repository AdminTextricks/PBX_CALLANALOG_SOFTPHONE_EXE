using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CallAnalog.Softphone.Services;

internal sealed record AudioDeviceInfo(string Id, string FriendlyName, int WinMmIndex);

internal static class AudioDeviceHelper
{
    private const string DefaultDeviceLabel = "System Default";

    public static IReadOnlyList<AudioDeviceInfo> EnumerateInputDevices()
    {
        try
        {
            return EnumerateWasapiDevices(DataFlow.Capture);
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Failed to enumerate microphones: {ex.Message}");
            return [];
        }
    }

    public static IReadOnlyList<AudioDeviceInfo> EnumerateOutputDevices()
    {
        try
        {
            return EnumerateWasapiDevices(DataFlow.Render);
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Failed to enumerate speakers: {ex.Message}");
            return [];
        }
    }

    public static IReadOnlyList<string> GetInputDevices() =>
        EnumerateInputDevices().Select(device => device.FriendlyName).ToList();

    public static IReadOnlyList<string> GetOutputDevices() =>
        EnumerateOutputDevices().Select(device => device.FriendlyName).ToList();

    public static string? GetInputDeviceId(string? friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return null;
        }

        return EnumerateInputDevices()
            .FirstOrDefault(device => device.FriendlyName.Equals(friendlyName, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    public static string? GetOutputDeviceId(string? friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return null;
        }

        return EnumerateOutputDevices()
            .FirstOrDefault(device => device.FriendlyName.Equals(friendlyName, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    public static int FindInputDeviceIndexForSip(string? deviceName, string? deviceId = null) =>
        ResolveWinMmIndex(
            EnumerateInputDevices(),
            deviceId,
            deviceName,
            WaveIn.DeviceCount,
            index => WaveIn.GetCapabilities(index).ProductName);

    public static int FindOutputDeviceIndexForSip(string? deviceName, string? deviceId = null) =>
        ResolveWinMmIndex(
            EnumerateOutputDevices(),
            deviceId,
            deviceName,
            WaveOut.DeviceCount,
            index => WaveOut.GetCapabilities(index).ProductName);

    public static int FindInputDeviceIndex(string? deviceName, string? deviceId = null) =>
        FindInputDeviceIndexForSip(deviceName, deviceId);

    public static int FindOutputDeviceIndex(string? deviceName, string? deviceId = null) =>
        FindOutputDeviceIndexForSip(deviceName, deviceId);

    public static MMDevice GetCaptureDevice(string? deviceName, string? deviceId = null) =>
        ResolveWasapiDevice(DataFlow.Capture, EnumerateInputDevices(), deviceId, deviceName);

    public static MMDevice GetRenderDevice(string? deviceName, string? deviceId = null) =>
        ResolveWasapiDevice(DataFlow.Render, EnumerateOutputDevices(), deviceId, deviceName);

    /// <summary>
    /// Prefer the Windows Communications endpoint (headset-friendly VoIP routing).
    /// Falls back to the selected/console device when Communications is unavailable.
    /// </summary>
    public static MMDevice GetCommunicationsCaptureDevice(string? deviceName, string? deviceId = null)
    {
        try
        {
            if (IsDefaultSelection(deviceName) && string.IsNullOrWhiteSpace(deviceId))
            {
                return new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }
        }
        catch (Exception ex)
        {
            App.SipLog.Warn($"WASAPI Communications capture unavailable ({ex.Message}); using selected device.");
        }

        return GetCaptureDevice(deviceName, deviceId);
    }

    public static MMDevice GetCommunicationsRenderDevice(string? deviceName, string? deviceId = null)
    {
        try
        {
            if (IsDefaultSelection(deviceName) && string.IsNullOrWhiteSpace(deviceId))
            {
                return new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
            }
        }
        catch (Exception ex)
        {
            App.SipLog.Warn($"WASAPI Communications render unavailable ({ex.Message}); using selected device.");
        }

        return GetRenderDevice(deviceName, deviceId);
    }

    public static void SetCaptureVolume(string? microphoneDevice, string? microphoneDeviceId, double volumeScalar)
    {
        try
        {
            var device = GetCaptureDevice(microphoneDevice, microphoneDeviceId);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(volumeScalar, 0, 1);
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Failed to set capture volume: {ex.Message}");
        }
    }

    public static bool IsDefaultSelection(string? deviceName) =>
        string.IsNullOrWhiteSpace(deviceName)
        || deviceName.Equals(DefaultDeviceLabel, StringComparison.OrdinalIgnoreCase)
        || deviceName.StartsWith("Default ", StringComparison.OrdinalIgnoreCase);

    public static string? ResolveSavedDeviceName(
        IReadOnlyList<AudioDeviceInfo> devices,
        string? savedDeviceId,
        string? savedDeviceName,
        IReadOnlyList<string> availableDeviceNames)
    {
        if (!string.IsNullOrWhiteSpace(savedDeviceId))
        {
            var byId = devices.FirstOrDefault(device => device.Id.Equals(savedDeviceId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null && availableDeviceNames.Contains(byId.FriendlyName, StringComparer.OrdinalIgnoreCase))
            {
                return byId.FriendlyName;
            }
        }

        if (!string.IsNullOrWhiteSpace(savedDeviceName)
            && availableDeviceNames.Contains(savedDeviceName, StringComparer.OrdinalIgnoreCase))
        {
            return savedDeviceName;
        }

        return null;
    }

    private static IReadOnlyList<AudioDeviceInfo> EnumerateWasapiDevices(DataFlow flow)
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDeviceInfo>();

        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            var winMmIndex = flow == DataFlow.Capture
                ? MapToWinMmInputIndex(device.FriendlyName)
                : MapToWinMmOutputIndex(device.FriendlyName);
            devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, winMmIndex));
        }

        return devices;
    }

    private static int ResolveWinMmIndex(
        IReadOnlyList<AudioDeviceInfo> devices,
        string? deviceId,
        string? deviceName,
        int winMmCount,
        Func<int, string> getProductName)
    {
        if (IsDefaultSelection(deviceName) && string.IsNullOrWhiteSpace(deviceId))
        {
            return -1;
        }

        var match = FindDevice(devices, deviceId, deviceName);
        if (match is not null && match.WinMmIndex >= 0)
        {
            return match.WinMmIndex;
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            for (var i = 0; i < winMmCount; i++)
            {
                if (getProductName(i).Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        LogWinMmFallback(deviceName, "using Windows default audio device (-1)");
        return -1;
    }

    public static string DescribeWinMmFallback(string? deviceName) =>
        IsDefaultSelection(deviceName)
            ? "Using system default audio device."
            : $"WinMM could not map '{deviceName}'. Using system default audio device.";

    private static void LogWinMmFallback(string? deviceName, string reason)
    {
        var label = string.IsNullOrWhiteSpace(deviceName) ? "default device" : $"'{deviceName}'";
        App.SipLog.Warn($"AUDIO DEVICE FALLBACK: {label} -> system default ({reason}).");
    }

    private static AudioDeviceInfo? FindDevice(
        IReadOnlyList<AudioDeviceInfo> devices,
        string? deviceId,
        string? deviceName)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var byId = devices.FirstOrDefault(device => device.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (string.IsNullOrWhiteSpace(deviceName) || IsDefaultSelection(deviceName))
        {
            return null;
        }

        var exact = devices.FirstOrDefault(device =>
            device.FriendlyName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return devices
            .Select(device => (device, score: MatchScore(device.FriendlyName, deviceName)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .Select(pair => pair.device)
            .FirstOrDefault();
    }

    private static MMDevice ResolveWasapiDevice(
        DataFlow flow,
        IReadOnlyList<AudioDeviceInfo> devices,
        string? deviceId,
        string? deviceName)
    {
        var enumerator = new MMDeviceEnumerator();
        var match = FindDevice(devices, deviceId, deviceName);
        if (match is not null)
        {
            try
            {
                return enumerator.GetDevice(match.Id);
            }
            catch (Exception ex)
            {
                App.SipLog.Error($"Failed to open audio device '{match.FriendlyName}': {ex.Message}");
            }
        }

        return enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
    }

    private static int MapToWinMmInputIndex(string friendlyName) =>
        MapToWinMmIndex(friendlyName, WaveIn.DeviceCount, index => WaveIn.GetCapabilities(index).ProductName);

    private static int MapToWinMmOutputIndex(string friendlyName) =>
        MapToWinMmIndex(friendlyName, WaveOut.DeviceCount, index => WaveOut.GetCapabilities(index).ProductName);

    private static int MapToWinMmIndex(string friendlyName, int deviceCount, Func<int, string> getProductName)
    {
        var bestIndex = -1;
        var bestScore = 0;

        for (var index = 0; index < deviceCount; index++)
        {
            var score = MatchScore(friendlyName, getProductName(index));
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestScore > 0 ? bestIndex : -1;
    }

    private static int MatchScore(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (left.Contains(right, StringComparison.OrdinalIgnoreCase)
            || right.Contains(left, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var overlap = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var minimum = Math.Min(leftTokens.Count, rightTokens.Count);
        return overlap >= minimum / 2 ? 60 + overlap : 0;
    }

    private static HashSet<string> Tokenize(string value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in value.Split([' ', '(', ')', '[', ']', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 2)
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }
}
