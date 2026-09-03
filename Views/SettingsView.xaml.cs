using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;
using Microsoft.Win32;

namespace CallAnalog.Softphone.Views;

public partial class SettingsView : UserControl
{
    private UserSettingsService? _settingsService;
    private Func<bool>? _isCallActive;
    private SipService? _sipService;
    private AppVersionCheckService? _appVersionCheckService;
    private readonly AudioTestService _audioTestService = new();
    private readonly DiagnosticsExportService? _diagnosticsExport;
    private AudioDeviceChangeNotifier? _deviceChangeNotifier;
    private readonly DispatcherTimer _micLevelDecayTimer;
    private bool _initialized;
    private bool _smtpConfigured;
    private int _previousRegisterSeconds;
    private int _previousKeepAliveSeconds;
    private IReadOnlyList<string> _inputDevices = [];
    private IReadOnlyList<string> _outputDevices = [];

    public event EventHandler<SettingsSavedEventArgs>? SettingsSaved;
    public event EventHandler? SaveAllCompleted;

    public SettingsView()
    {
        InitializeComponent();

        _diagnosticsExport = new DiagnosticsExportService(App.UserSettings, App.SipLog);
        _smtpConfigured = !string.IsNullOrWhiteSpace(App.Configuration["CrashReport:SmtpHost"]);
        VersionInfoText.Text = BuildInfo.FullBuildLabel;

        _micLevelDecayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _micLevelDecayTimer.Tick += (_, _) =>
        {
            if (!_audioTestService.IsMicMonitoring && MicLevelBar.Value > 0)
            {
                MicLevelBar.Value = Math.Max(0, MicLevelBar.Value - 8);
            }
        };

        Unloaded += (_, _) =>
        {
            StopMonitoring();
            _deviceChangeNotifier?.Dispose();
            _deviceChangeNotifier = null;
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                StopMonitoring();
            }
        };
    }

    public void Initialize(
        UserSettingsService settingsService,
        Func<bool>? isCallActive = null,
        SipService? sipService = null,
        AppVersionCheckService? appVersionCheckService = null)
    {
        _settingsService = settingsService;
        _isCallActive = isCallActive;
        _sipService = sipService;
        _appVersionCheckService = appVersionCheckService;

        LoadDeviceLists();
        LoadFields();
        EnsureDeviceChangeNotifier();

        if (!_initialized)
        {
            _micLevelDecayTimer.Start();
            _initialized = true;
        }
    }

    private void StopMonitoring()
    {
        _audioTestService.StopAll();
        MicLevelBar.Value = 0;
    }

    private void EnsureDeviceChangeNotifier()
    {
        _deviceChangeNotifier ??= new AudioDeviceChangeNotifier(() =>
        {
            Dispatcher.Invoke(() =>
            {
                LoadDeviceLists();
                LoadFields();
                SetStatus("Audio devices changed — lists refreshed.", StatusMessageKind.Warning);
            });
        });
    }

    private void LoadDeviceLists()
    {
        if (_settingsService is null)
        {
            return;
        }

        _inputDevices = _settingsService.GetAudioInputDevices();
        _outputDevices = _settingsService.GetAudioOutputDevices();

        MicrophoneCombo.ItemsSource = _inputDevices;
        SpeakerCombo.ItemsSource = _outputDevices;
        RingtoneCombo.ItemsSource = _outputDevices;
    }

    private void LoadFields()
    {
        if (_settingsService is null)
        {
            return;
        }

        var settings = _settingsService.Settings;
        var deviceWarnings = new List<string>();

        if (_inputDevices.Count == 0)
        {
            deviceWarnings.Add("No microphones detected.");
            MicrophoneCombo.SelectedItem = null;
        }
        else
        {
            var microphone = _settingsService.ResolveSavedMicrophone(settings.MicrophoneDeviceId, settings.MicrophoneDevice);
            if (microphone is null
                && (!string.IsNullOrWhiteSpace(settings.MicrophoneDevice) || !string.IsNullOrWhiteSpace(settings.MicrophoneDeviceId)))
            {
                deviceWarnings.Add("Saved microphone not found; using system default.");
            }

            MicrophoneCombo.SelectedItem = microphone;
        }

        if (_outputDevices.Count == 0)
        {
            deviceWarnings.Add("No speakers detected.");
            SpeakerCombo.SelectedItem = null;
            RingtoneCombo.SelectedItem = null;
        }
        else
        {
            var speaker = _settingsService.ResolveSavedSpeaker(settings.SpeakerDeviceId, settings.SpeakerDevice);
            if (speaker is null
                && (!string.IsNullOrWhiteSpace(settings.SpeakerDevice) || !string.IsNullOrWhiteSpace(settings.SpeakerDeviceId)))
            {
                deviceWarnings.Add("Saved speaker not found; using system default.");
            }

            SpeakerCombo.SelectedItem = speaker;

            var ringtone = _settingsService.ResolveSavedSpeaker(settings.RingtoneDeviceId, settings.RingtoneDevice);
            if (ringtone is null
                && (!string.IsNullOrWhiteSpace(settings.RingtoneDevice) || !string.IsNullOrWhiteSpace(settings.RingtoneDeviceId)))
            {
                deviceWarnings.Add("Saved ringtone device not found; using system default.");
            }

            RingtoneCombo.SelectedItem = ringtone;
        }

        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        SelectComboByTag(VoiceProfileCombo, settings.VoiceQualityProfile, "Balanced");
        SelectComboByTag(VoiceEchoCombo, settings.VoiceEchoControl, "On");
        SelectComboByTag(VoiceNoiseCombo, settings.VoiceNoiseReduction, "Low");
        VoiceAutoGainCheckBox.IsChecked = settings.VoiceAutoGainEnabled;
        VoicePreferOpusCheckBox.IsChecked = settings.VoicePreferOpus;
        InputVolumeSlider.Value = settings.InputVolume;
        OutputVolumeSlider.Value = settings.OutputVolume;

        if (deviceWarnings.Count > 0)
        {
            SetStatus(string.Join(" ", deviceWarnings.Distinct()), StatusMessageKind.Warning);
        }

        UpdateAudioDevicePreview();

        CarrierHostText.Text = settings.CarrierHost;
        SipPortText.Text = settings.SipPort.ToString();
        RegisterRequestBox.Text = settings.RegistrationExpirySeconds.ToString();
        KeepAliveBox.Text = settings.KeepAliveSeconds.ToString();
        _previousRegisterSeconds = settings.RegistrationExpirySeconds;
        _previousKeepAliveSeconds = settings.KeepAliveSeconds;
        TransportCombo.SelectedIndex = settings.DefaultTransport.Equals("udp", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        HoldMusicPathText.Text = FormatMediaPath(settings.HoldMusicPath);
        RingtonePathText.Text = FormatMediaPath(settings.RingtonePath);

        var enabledCodecs = CodecConfiguration.NormalizeEnabledCodecs(settings.EnabledCodecs);
        CodecPcmuCheckBox.IsChecked = enabledCodecs.Contains(CodecConfiguration.Pcmu, StringComparer.OrdinalIgnoreCase);
        CodecPcmaCheckBox.IsChecked = enabledCodecs.Contains(CodecConfiguration.Pcma, StringComparer.OrdinalIgnoreCase);

        CallRecordingCheckBox.IsChecked = settings.CallRecordingEnabled;
        RecordingFormatCombo.SelectedIndex = settings.CallRecordingFormat.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        RecordingFolderText.Text = string.IsNullOrWhiteSpace(settings.CallRecordingDirectory)
            ? "No folder selected"
            : settings.CallRecordingDirectory;

        CrashReportCheckBox.IsChecked = settings.SendCrashReport;
        CrashReportNoteText.Text = _smtpConfigured
            ? "Reports are saved locally and emailed when this device is online."
            : "Reports are saved locally. Email delivery will be enabled when IT configures SMTP.";
    }

    private static string FormatMediaPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Not set";
        }

        return File.Exists(path) ? path : $"{path} (file missing)";
    }

    private void AudioDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateAudioDevicePreview();

    private void UpdateAudioDevicePreview()
    {
        MicrophonePreviewText.Text = FormatDevicePreview("Microphone", MicrophoneCombo.SelectedItem as string);
        SpeakerPreviewText.Text = FormatDevicePreview("Speaker", SpeakerCombo.SelectedItem as string);
        RingtonePreviewText.Text = FormatDevicePreview("Ringtone", RingtoneCombo.SelectedItem as string);
    }

    private static string FormatDevicePreview(string label, string? deviceName) =>
        string.IsNullOrWhiteSpace(deviceName)
            ? $"Using: system default {label.ToLowerInvariant()}"
            : $"Using: {deviceName}";

    private static void SelectComboByTag(ComboBox combo, string? tag, string fallbackTag)
    {
        var target = string.IsNullOrWhiteSpace(tag) ? fallbackTag : tag;
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, target, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, fallbackTag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static string ReadComboTag(ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private bool TryBuildPreferencesFromUi(out AppSettings preferences, out string? errorMessage)
    {
        preferences = null!;
        errorMessage = null;

        var enabledCodecs = new List<string>();
        if (CodecPcmuCheckBox.IsChecked == true)
        {
            enabledCodecs.Add(CodecConfiguration.Pcmu);
        }

        if (CodecPcmaCheckBox.IsChecked == true)
        {
            enabledCodecs.Add(CodecConfiguration.Pcma);
        }

        if (enabledCodecs.Count == 0)
        {
            errorMessage = "Select at least one codec before saving.";
            return false;
        }

        if (!TryParsePositiveInt(RegisterRequestBox.Text, 60, out var registerSeconds, out var registerError))
        {
            errorMessage = registerError;
            return false;
        }

        if (!TryParsePositiveInt(KeepAliveBox.Text, 5, out var keepAliveSeconds, out var keepAliveError))
        {
            errorMessage = keepAliveError;
            return false;
        }

        var holdMusicPath = ReadMediaPath(HoldMusicPathText.Text);
        var ringtonePath = ReadMediaPath(RingtonePathText.Text);
        if (holdMusicPath is not null && !File.Exists(holdMusicPath))
        {
            errorMessage = "Hold music file was not found. Choose a valid file or clear the path.";
            return false;
        }

        if (ringtonePath is not null && !File.Exists(ringtonePath))
        {
            errorMessage = "Ringtone file was not found. Choose a valid file or clear the path.";
            return false;
        }

        var formatItem = RecordingFormatCombo.SelectedItem as ComboBoxItem;
        var recordingFormat = formatItem?.Content?.ToString()?.Equals("MP3", StringComparison.OrdinalIgnoreCase) == true
            ? "mp3"
            : "wav";

        var existing = _settingsService!.Settings;
        var microphoneDevice = MicrophoneCombo.SelectedItem as string;
        var speakerDevice = SpeakerCombo.SelectedItem as string;
        var ringtoneDevice = RingtoneCombo.SelectedItem as string;
        preferences = new AppSettings
        {
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            MicrophoneDevice = microphoneDevice,
            MicrophoneDeviceId = _settingsService.GetMicrophoneDeviceId(microphoneDevice),
            SpeakerDevice = speakerDevice,
            SpeakerDeviceId = _settingsService.GetSpeakerDeviceId(speakerDevice),
            RingtoneDevice = ringtoneDevice,
            RingtoneDeviceId = _settingsService.GetSpeakerDeviceId(ringtoneDevice),
            InputVolume = InputVolumeSlider.Value,
            OutputVolume = OutputVolumeSlider.Value,
            HoldMusicPath = holdMusicPath,
            RingtonePath = ringtonePath,
            EnabledCodecs = enabledCodecs,
            CallRecordingEnabled = CallRecordingCheckBox.IsChecked == true,
            CallRecordingFormat = recordingFormat,
            CallRecordingDirectory = RecordingFolderText.Text is "No folder selected" ? null : RecordingFolderText.Text,
            SendCrashReport = CrashReportCheckBox.IsChecked == true,
            CrashReportEmail = "help@callanalog.com",
            DndEnabled = existing.DndEnabled,
            AutoAnswerEnabled = existing.AutoAnswerEnabled,
            DarkModeEnabled = true,
            FollowSystemTheme = false,
            VoiceQualityProfile = ReadComboTag(VoiceProfileCombo, "Balanced"),
            VoiceEchoControl = ReadComboTag(VoiceEchoCombo, "On"),
            VoiceNoiseReduction = ReadComboTag(VoiceNoiseCombo, "Low"),
            VoiceAutoGainEnabled = VoiceAutoGainCheckBox.IsChecked == true,
            VoicePreferOpus = VoicePreferOpusCheckBox.IsChecked == true,
            VoicemailDialCode = existing.VoicemailDialCode,
            ConferenceExtension = existing.ConferenceExtension,
            AgentQueueModeEnabled = existing.AgentQueueModeEnabled,
            RegistrationExpirySeconds = registerSeconds,
            KeepAliveSeconds = keepAliveSeconds
        };

        return true;
    }

    private static bool TryParsePositiveInt(string text, int minimum, out int value, out string? errorMessage)
    {
        errorMessage = null;
        if (!int.TryParse(text.Trim(), out value) || value < minimum)
        {
            value = minimum;
            errorMessage = minimum switch
            {
                60 => "Register request must be a number of at least 60 seconds.",
                5 => "Keep alive must be a number of at least 5 seconds.",
                _ => $"Value must be at least {minimum}."
            };
            return false;
        }

        return true;
    }

    private static string? ReadMediaPath(string displayText)
    {
        if (displayText is "Not set")
        {
            return null;
        }

        var path = displayText;
        const string missingSuffix = " (file missing)";
        if (path.EndsWith(missingSuffix, StringComparison.Ordinal))
        {
            path = path[..^missingSuffix.Length];
        }

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private async Task<bool> SavePreferencesAsync()
    {
        if (_settingsService is null)
        {
            return false;
        }

        if (!TryBuildPreferencesFromUi(out var preferences, out var errorMessage))
        {
            SetStatus(errorMessage ?? "Unable to save settings.", StatusMessageKind.Error);
            return false;
        }

        await _settingsService.SavePreferencesAsync(preferences);
        var timingChanged = _previousRegisterSeconds != preferences.RegistrationExpirySeconds
            || _previousKeepAliveSeconds != preferences.KeepAliveSeconds;
        await _settingsService.SaveSipTimingAsync(
            preferences.RegistrationExpirySeconds,
            preferences.KeepAliveSeconds);
        await _settingsService.SetStartWithWindowsAsync(preferences.StartWithWindows);
        SettingsSaved?.Invoke(this, new SettingsSavedEventArgs(timingChanged));
        return true;
    }

    private void SetStatus(string message, StatusMessageKind kind = StatusMessageKind.Neutral) =>
        StatusMessageHelper.Apply(StatusText, message, kind);

    public void FlashExternalStatus(string message, StatusMessageKind kind = StatusMessageKind.Neutral) =>
        SetStatus(message, kind);

    private async void SaveAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildPreferencesFromUi(out var preferences, out var errorMessage))
        {
            SetStatus(errorMessage ?? "Select at least one codec before saving.", StatusMessageKind.Error);
            return;
        }

        if (!await SavePreferencesAsync())
        {
            return;
        }

        var messages = new List<string> { "All settings saved." };
        var timingChanged = _previousRegisterSeconds != preferences.RegistrationExpirySeconds
            || _previousKeepAliveSeconds != preferences.KeepAliveSeconds;
        if (timingChanged)
        {
            messages.Add("Register request and keep alive changes were applied via re-REGISTER.");
            _previousRegisterSeconds = preferences.RegistrationExpirySeconds;
            _previousKeepAliveSeconds = preferences.KeepAliveSeconds;
        }

        if (_isCallActive?.Invoke() == true)
        {
            if (_sipService is not null)
            {
                await _sipService.ApplyAudioDeviceHotSwapAsync();
                messages.Add("Audio device changes applied to the active call.");
            }
            else
            {
                messages.Add("Audio device changes apply on the next call.");
            }
        }

        AppendWinMmFallbackMessage(messages, MicrophoneCombo.SelectedItem as string, isInput: true, _settingsService!);
        AppendWinMmFallbackMessage(messages, SpeakerCombo.SelectedItem as string, isInput: false, _settingsService!);

        SetStatus(string.Join(" ", messages.Distinct()), StatusMessageKind.Success);
        ThemeManager.ApplyDarkMode();
        SaveAllCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static void AppendWinMmFallbackMessage(
        List<string> messages,
        string? deviceName,
        bool isInput,
        UserSettingsService settings)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return;
        }

        var deviceId = isInput
            ? settings.GetMicrophoneDeviceId(deviceName)
            : settings.GetSpeakerDeviceId(deviceName);
        var index = isInput
            ? AudioDeviceHelper.FindInputDeviceIndex(deviceName, deviceId)
            : AudioDeviceHelper.FindOutputDeviceIndex(deviceName, deviceId);

        if (index < 0)
        {
            messages.Add(AudioDeviceHelper.DescribeWinMmFallback(deviceName));
        }
    }

    private async void SaveTransportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService is null)
        {
            return;
        }

        var transportItem = TransportCombo.SelectedItem as ComboBoxItem;
        var transport = transportItem?.Content?.ToString() ?? "TCP";
        await _settingsService.SaveTransportAsync(transport);
        SetStatus("Transport saved. Sign out and sign in for this change to apply.", StatusMessageKind.Warning);
    }

    private void TestMicButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _audioTestService.StartMicrophoneMonitor(
                MicrophoneCombo.SelectedItem as string,
                InputVolumeSlider.Value,
                level => Dispatcher.Invoke(() => MicLevelBar.Value = level * 100));
            SetStatus("Testing microphone for up to 5 seconds...", StatusMessageKind.Progress);
        }
        catch (Exception ex)
        {
            SetStatus($"Mic test failed — {ex.Message}", StatusMessageKind.Error);
        }
    }

    private void TestSpeakerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var speakerName = SpeakerCombo.SelectedItem as string;
            var outputVolume = OutputVolumeSlider.Value;
            _audioTestService.StartSpeakerTest(
                speakerName,
                outputVolume,
                _settingsService?.GetSpeakerDeviceId(speakerName));
            var volumeNote = outputVolume <= 0
                ? " Output volume was 0 — using 50% for this test."
                : string.Empty;
            SetStatus(
                $"Playing test tone for up to 5 seconds on {speakerName ?? "default speaker"}...{volumeNote}",
                StatusMessageKind.Progress);
        }
        catch (Exception ex)
        {
            SetStatus($"Speaker test failed — {ex.Message}", StatusMessageKind.Error);
        }
    }

    private void StopAudioTestButton_Click(object sender, RoutedEventArgs e)
    {
        StopMonitoring();
        SetStatus("Audio test stopped.", StatusMessageKind.Neutral);
    }

    private async void RemoveHoldMusicButton_Click(object sender, RoutedEventArgs e)
    {
        HoldMusicPathText.Text = "Not set";
        if (await SavePreferencesAsync())
        {
            SetStatus("Hold music removed.", StatusMessageKind.Success);
        }
    }

    private async void RemoveRingtoneButton_Click(object sender, RoutedEventArgs e)
    {
        RingtonePathText.Text = "Not set";
        if (await SavePreferencesAsync())
        {
            SetStatus("Custom ringtone removed. Using default ringtone.", StatusMessageKind.Success);
        }
    }

    private async void UploadHoldMusicButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PickAudioFile("Select hold music");
        if (path is null)
        {
            return;
        }

        try
        {
            var storedPath = MediaFileStorage.CopyHoldMusicToAppStorage(path);
            HoldMusicPathText.Text = storedPath;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusMessageKind.Error);
            return;
        }

        if (await SavePreferencesAsync())
        {
            SetStatus("Hold music file selected.", StatusMessageKind.Success);
        }
    }

    private async void UploadRingtoneButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PickAudioFile("Select ringtone");
        if (path is null)
        {
            return;
        }

        try
        {
            var storedPath = MediaFileStorage.CopyRingtoneToAppStorage(path);
            RingtonePathText.Text = storedPath;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusMessageKind.Error);
            return;
        }

        if (await SavePreferencesAsync())
        {
            SetStatus("Ringtone file selected.", StatusMessageKind.Success);
        }
    }

    private async void ChooseRecordingFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select call recording folder"
        };

        if (dialog.ShowDialog() == true)
        {
            RecordingFolderText.Text = dialog.FolderName;
            if (await SavePreferencesAsync())
            {
                SetStatus("Recording folder updated.", StatusMessageKind.Success);
            }
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_appVersionCheckService is null)
        {
            SetStatus(
                $"{BuildInfo.FullBuildLabel}. Update check is not available.",
                StatusMessageKind.Warning);
            return;
        }

        if (sender is Button button)
        {
            button.IsEnabled = false;
        }

        SetStatus("Checking for updates...", StatusMessageKind.Progress);

        try
        {
            var result = await _appVersionCheckService.CheckAsync();
            SetStatus(
                result.FormatStatusMessage(BuildInfo.FullBuildLabel),
                result.UpdateAvailable ? StatusMessageKind.Warning : StatusMessageKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Update check failed — {ex.Message}", StatusMessageKind.Error);
        }
        finally
        {
            if (sender is Button restoreButton)
            {
                restoreButton.IsEnabled = true;
            }
        }
    }

    private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var zipPath = _diagnosticsExport!.ExportToZip();
            SetStatus($"Diagnostics exported to {zipPath}", StatusMessageKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Diagnostics export failed — {ex.Message}", StatusMessageKind.Error);
        }
    }

    private void OpenSipLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            App.SipLog.EnsureLogFileExists();
            var logPath = App.SipLog.LogFilePath;
            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            });
            App.SipLog.Info(SipLogTag.Settings, "User opened SIP log from Settings.");
            SetStatus("Opened SIP log in your default editor.", StatusMessageKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open SIP log — {ex.Message}", StatusMessageKind.Error);
        }
    }

    private void OpenLogsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logsFolder = _settingsService?.LogsFolderPath ?? App.UserSettings.LogsFolderPath;
            Directory.CreateDirectory(logsFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = logsFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open logs folder — {ex.Message}", StatusMessageKind.Error);
        }
    }

    private void OpenHelpButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.callanalog.com",
            UseShellExecute = true
        });
    }

    private static string? PickAudioFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Audio files (*.mp3;*.wav)|*.mp3;*.wav|All files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
