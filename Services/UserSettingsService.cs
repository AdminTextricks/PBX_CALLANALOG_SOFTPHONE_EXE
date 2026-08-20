using System.IO;
using System.Text.Json;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;

namespace CallAnalog.Softphone.Services;

public sealed class UserSettingsService
{
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "CallAnalogSoftphone";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly string _logsFolderPath;
    private readonly CredentialProtectionService _credentials;
    private AppSettings _settings;

    public UserSettingsService(IConfiguration? configuration = null)
    {
        configuration ??= App.Configuration;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "user-settings.json");
        _logsFolderPath = Path.Combine(directory, "logs");
        Directory.CreateDirectory(_logsFolderPath);
        _credentials = new CredentialProtectionService(directory);

        _settings = CreateDefaultSettings(configuration);
        LoadFromDisk();
        SanitizeCarrierFromDisk(configuration);
    }

    internal UserSettingsService(IConfiguration configuration, string settingsDirectory)
    {
        Directory.CreateDirectory(settingsDirectory);
        _settingsPath = Path.Combine(settingsDirectory, "user-settings.json");
        _logsFolderPath = Path.Combine(settingsDirectory, "logs");
        Directory.CreateDirectory(_logsFolderPath);
        _credentials = new CredentialProtectionService(settingsDirectory);

        _settings = CreateDefaultSettings(configuration);
        LoadFromDisk();
        SanitizeCarrierFromDisk(configuration);
    }

    private static AppSettings CreateDefaultSettings(IConfiguration configuration) =>
        new()
        {
            CompanyName = configuration["App:CompanyName"] ?? "CallAnalog",
            CarrierHost = configuration["Sip:CarrierHost"] ?? "user.callanalog.com",
            DefaultTransport = configuration["Sip:DefaultTransport"] ?? "tcp",
            SipPort = configuration.GetValue("Sip:SipPort", 5065),
            RegistrationExpirySeconds = configuration.GetValue("Sip:RegistrationExpirySeconds", 3600),
            KeepAliveSeconds = configuration.GetValue("Sip:KeepAliveSeconds", 15),
            EnabledCodecs = CodecConfiguration.DefaultEnabledCodecs.ToList()
        };

    public void RestoreCachedPublicIp(SipLogService? log = null) =>
        SipNatHelper.RestoreCachedPublicIp(_settings.CachedPublicIp, _settings.CachedPublicIpUtc, log);

    public void SaveCachedPublicIp()
    {
        var address = SipNatHelper.CachedPublicIp;
        if (address is null)
        {
            return;
        }

        _settings.CachedPublicIp = address.ToString();
        _settings.CachedPublicIpUtc = DateTimeOffset.UtcNow;
        Persist();
    }

    public AppSettings Settings => _settings;
    public string LogsFolderPath => _logsFolderPath;
    public string CarrierHost => _settings.CarrierHost;
    public string Transport => _settings.DefaultTransport;
    public int SipPort => _settings.SipPort;

    public ProvisionConfig BuildProvisionConfig(string extension, string password) =>
        new()
        {
            Extension = extension.Trim(),
            Password = password,
            SipServer = _settings.CarrierHost,
            SipConnectHost = _settings.CarrierConnectHost,
            SipPort = _settings.SipPort,
            Transport = _settings.DefaultTransport,
            DisplayName = extension.Trim()
        };

    public IReadOnlyList<string> GetAudioInputDevices() => AudioDeviceHelper.GetInputDevices();
    public IReadOnlyList<string> GetAudioOutputDevices() => AudioDeviceHelper.GetOutputDevices();

    public string? ResolveSavedMicrophone(string? savedDeviceId, string? savedDeviceName) =>
        AudioDeviceHelper.ResolveSavedDeviceName(
            AudioDeviceHelper.EnumerateInputDevices(),
            savedDeviceId,
            savedDeviceName,
            GetAudioInputDevices());

    public string? ResolveSavedSpeaker(string? savedDeviceId, string? savedDeviceName) =>
        AudioDeviceHelper.ResolveSavedDeviceName(
            AudioDeviceHelper.EnumerateOutputDevices(),
            savedDeviceId,
            savedDeviceName,
            GetAudioOutputDevices());

    public string? GetMicrophoneDeviceId(string? friendlyName) => AudioDeviceHelper.GetInputDeviceId(friendlyName);
    public string? GetSpeakerDeviceId(string? friendlyName) => AudioDeviceHelper.GetOutputDeviceId(friendlyName);

    public (string Extension, string Password, bool RememberMe) LoadRememberedLogin()
    {
        if (!_settings.RememberMe)
        {
            var sessionExtension = _credentials.LoadExtension();
            return (sessionExtension, string.Empty, false);
        }

        var extension = !string.IsNullOrWhiteSpace(_settings.Extension)
            ? _settings.Extension
            : _credentials.LoadExtension();
        return (extension, _credentials.LoadPassword(), true);
    }

    public void SaveRememberedLogin(string extension, string password, bool rememberMe)
    {
        _settings.Extension = rememberMe ? extension.Trim() : string.Empty;
        _settings.Password = rememberMe ? password : string.Empty;
        _settings.RememberMe = rememberMe;

        if (rememberMe)
        {
            _credentials.SavePassword(password);
            _credentials.SaveExtension(extension.Trim());
        }
        else
        {
            _credentials.SavePassword(null);
            _credentials.SaveExtension(extension.Trim());
        }

        Persist();
    }

    public Task SaveCarrierAsync(string carrierHost, string transport, int sipPort, string? connectHost = null)
    {
        var host = carrierHost.Contains(':') ? carrierHost.Split(':')[0] : carrierHost.Trim();
        _settings.CarrierHost = host;
        _settings.CarrierConnectHost = string.IsNullOrWhiteSpace(connectHost) ? null : connectHost.Trim();
        _settings.DefaultTransport = transport.Trim().ToLowerInvariant();
        _settings.SipPort = sipPort;
        Persist();
        return Task.CompletedTask;
    }

    public Task SavePreferencesAsync(AppSettings preferences)
    {
        _settings.StartWithWindows = preferences.StartWithWindows;
        _settings.MicrophoneDevice = preferences.MicrophoneDevice;
        _settings.MicrophoneDeviceId = preferences.MicrophoneDeviceId;
        _settings.SpeakerDevice = preferences.SpeakerDevice;
        _settings.SpeakerDeviceId = preferences.SpeakerDeviceId;
        _settings.RingtoneDevice = preferences.RingtoneDevice;
        _settings.RingtoneDeviceId = preferences.RingtoneDeviceId;
        _settings.InputVolume = preferences.InputVolume;
        _settings.OutputVolume = preferences.OutputVolume;
        _settings.HoldMusicPath = preferences.HoldMusicPath;
        _settings.RingtonePath = preferences.RingtonePath;
        _settings.EnabledCodecs = CodecConfiguration.NormalizeEnabledCodecs(preferences.EnabledCodecs).ToList();
        _settings.CallRecordingEnabled = preferences.CallRecordingEnabled;
        _settings.CallRecordingFormat = NormalizeRecordingFormat(preferences.CallRecordingFormat);
        _settings.CallRecordingDirectory = preferences.CallRecordingDirectory;
        _settings.SendCrashReport = preferences.SendCrashReport;
        _settings.CrashReportEmail = "help@callanalog.com";
        _settings.DndEnabled = preferences.DndEnabled;
        _settings.AutoAnswerEnabled = preferences.AutoAnswerEnabled;
        _settings.DarkModeEnabled = preferences.DarkModeEnabled;
        _settings.FollowSystemTheme = preferences.FollowSystemTheme;
        _settings.VoiceQualityProfile = VoiceQualitySettingsHelper.ProfileToStorage(
            VoiceQualitySettingsHelper.ParseProfile(preferences.VoiceQualityProfile));
        _settings.VoiceEchoControl = VoiceQualitySettingsHelper.EchoToStorage(
            VoiceQualitySettingsHelper.ParseEcho(preferences.VoiceEchoControl));
        _settings.VoiceNoiseReduction = VoiceQualitySettingsHelper.NoiseToStorage(
            VoiceQualitySettingsHelper.ParseNoise(preferences.VoiceNoiseReduction));
        _settings.VoiceAutoGainEnabled = preferences.VoiceAutoGainEnabled;
        _settings.VoicePreferOpus = preferences.VoicePreferOpus;
        _settings.ParkExtension = string.IsNullOrWhiteSpace(preferences.ParkExtension) ? "*70" : preferences.ParkExtension;
        _settings.VoicemailDialCode = string.IsNullOrWhiteSpace(preferences.VoicemailDialCode) ? "*97" : preferences.VoicemailDialCode;
        _settings.ConferenceExtension = preferences.ConferenceExtension;
        _settings.CallForwardNumber = preferences.CallForwardNumber;
        _settings.AgentQueueModeEnabled = preferences.AgentQueueModeEnabled;
        _settings.RegistrationExpirySeconds = preferences.RegistrationExpirySeconds;
        _settings.KeepAliveSeconds = preferences.KeepAliveSeconds;
        Persist();
        return Task.CompletedTask;
    }

    public Task SaveSipTimingAsync(int registrationExpirySeconds, int keepAliveSeconds)
    {
        _settings.RegistrationExpirySeconds = RegistrationTimingHelper.ClampRegistrationExpiry(registrationExpirySeconds);
        _settings.KeepAliveSeconds = RegistrationTimingHelper.ClampKeepAliveSeconds(keepAliveSeconds);
        Persist();
        return Task.CompletedTask;
    }

    public Task SaveTransportAsync(string transport)
    {
        _settings.DefaultTransport = transport.Trim().ToLowerInvariant();
        Persist();
        return Task.CompletedTask;
    }

    public Task SaveDashboardTogglesAsync(bool dndEnabled, bool autoAnswerEnabled)
    {
        _settings.DndEnabled = dndEnabled;
        _settings.AutoAnswerEnabled = autoAnswerEnabled;
        Persist();
        return Task.CompletedTask;
    }

    public static string NormalizeRecordingFormat(string? format) =>
        format?.Trim().Equals("mp3", StringComparison.OrdinalIgnoreCase) == true ? "mp3" : "wav";

    private void SanitizeCarrierFromDisk(IConfiguration configuration)
    {
        if (SipDomainParser.IsUsableHost(_settings.CarrierHost))
        {
            return;
        }

        _settings.CarrierHost = configuration["Sip:CarrierHost"] ?? "user.callanalog.com";
        _settings.DefaultTransport = configuration["Sip:DefaultTransport"] ?? "tcp";
        _settings.SipPort = configuration.GetValue("Sip:SipPort", SipDomainParser.DesktopTcpPort);
        Persist();
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions);
            if (saved is null)
            {
                return;
            }

            _settings.RememberMe = saved.RememberMe;
            _settings.Extension = saved.Extension;
            _credentials.MigratePlaintextPassword(saved.Password);
            _credentials.MigratePlaintextExtension(saved.Extension);
            _settings.Password = string.Empty;
            _settings.CarrierHost = saved.CarrierHost;
            _settings.CarrierConnectHost = saved.CarrierConnectHost;
            _settings.DefaultTransport = saved.DefaultTransport;
            _settings.SipPort = saved.SipPort;
            _settings.RegistrationExpirySeconds = saved.RegistrationExpirySeconds > 0
                ? saved.RegistrationExpirySeconds
                : 3600;
            _settings.KeepAliveSeconds = saved.KeepAliveSeconds > 0 ? saved.KeepAliveSeconds : 15;
            _settings.StartWithWindows = saved.StartWithWindows;
            _settings.MicrophoneDevice = saved.MicrophoneDevice;
            _settings.MicrophoneDeviceId = saved.MicrophoneDeviceId;
            _settings.SpeakerDevice = saved.SpeakerDevice;
            _settings.SpeakerDeviceId = saved.SpeakerDeviceId;
            _settings.RingtoneDevice = saved.RingtoneDevice;
            _settings.RingtoneDeviceId = saved.RingtoneDeviceId;
            _settings.InputVolume = saved.InputVolume;
            _settings.OutputVolume = saved.OutputVolume;
            _settings.HoldMusicPath = saved.HoldMusicPath;
            _settings.RingtonePath = saved.RingtonePath;
            _settings.EnabledCodecs = CodecConfiguration.NormalizeEnabledCodecs(saved.EnabledCodecs).ToList();
            _settings.CallRecordingEnabled = saved.CallRecordingEnabled;
            _settings.CallRecordingFormat = NormalizeRecordingFormat(saved.CallRecordingFormat);
            _settings.CallRecordingDirectory = saved.CallRecordingDirectory;
            _settings.SendCrashReport = saved.SendCrashReport;
            _settings.CrashReportEmail = string.IsNullOrWhiteSpace(saved.CrashReportEmail)
                ? "help@callanalog.com"
                : saved.CrashReportEmail;
            _settings.DndEnabled = saved.DndEnabled;
            _settings.AutoAnswerEnabled = saved.AutoAnswerEnabled;
            _settings.DarkModeEnabled = saved.DarkModeEnabled;
            _settings.FollowSystemTheme = saved.FollowSystemTheme;
            _settings.VoiceQualityProfile = VoiceQualitySettingsHelper.ProfileToStorage(
                VoiceQualitySettingsHelper.ParseProfile(saved.VoiceQualityProfile));
            _settings.VoiceEchoControl = VoiceQualitySettingsHelper.EchoToStorage(
                VoiceQualitySettingsHelper.ParseEcho(saved.VoiceEchoControl));
            _settings.VoiceNoiseReduction = VoiceQualitySettingsHelper.NoiseToStorage(
                VoiceQualitySettingsHelper.ParseNoise(saved.VoiceNoiseReduction));
            _settings.VoiceAutoGainEnabled = saved.VoiceAutoGainEnabled;
            _settings.VoicePreferOpus = saved.VoicePreferOpus;
            _settings.ParkExtension = string.IsNullOrWhiteSpace(saved.ParkExtension) ? "*70" : saved.ParkExtension;
            _settings.VoicemailDialCode = string.IsNullOrWhiteSpace(saved.VoicemailDialCode) ? "*97" : saved.VoicemailDialCode;
            _settings.ConferenceExtension = saved.ConferenceExtension;
            _settings.CallForwardNumber = saved.CallForwardNumber;
            _settings.AgentQueueModeEnabled = saved.AgentQueueModeEnabled;

            if (!string.IsNullOrEmpty(saved.Password) || (!saved.RememberMe && !string.IsNullOrEmpty(saved.Extension)))
            {
                Persist();
            }
        }
        catch
        {
            // Keep defaults if file is corrupt or legacy format.
        }
    }

    private void Persist()
    {
        var passwordInMemory = _settings.Password;
        _settings.Password = string.Empty;
        try
        {
            SettingsPersistenceHelper.WriteJsonAtomically(_settingsPath, _settings, JsonOptions);
            if (!string.IsNullOrEmpty(passwordInMemory))
            {
                _credentials.SavePassword(passwordInMemory);
            }
        }
        finally
        {
            _settings.Password = passwordInMemory;
        }
    }

    private static void ApplyStartupRegistration(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(StartupValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort startup registration.
        }
    }

    public Task SetStartWithWindowsAsync(bool enabled)
    {
        _settings.StartWithWindows = enabled;
        ApplyStartupRegistration(enabled);
        Persist();
        return Task.CompletedTask;
    }

    public void ApplySavedStartupRegistration() =>
        ApplyStartupRegistration(_settings.StartWithWindows);
}
