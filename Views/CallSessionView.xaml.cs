using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Views;

public partial class CallSessionView : UserControl
{
    private SipService? _sipService;
    private RingtoneService? _ringtone;
    private UserSettingsService? _settings;
    private NetworkQualityService? _networkQuality;
    private readonly CallAudioMeterService _audioMeters = new();
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _durationTimer;
    private readonly DispatcherTimer _ringVisualizerTimer;
    private readonly ObservableCollection<double> _ringBarHeights = [8, 16, 22, 14, 20, 10];
    private readonly double[] _ringBarTargets = new double[6];
    private bool _pulseStarted;
    private bool _waitingCallUiActive;
    private int _dtmfSentCount;
    private bool _dtmfHandledFromKeyDown;

    public event EventHandler<string>? ComingSoonRequested;
    public event EventHandler? BlindTransferRequested;

    public CallSessionView()
    {
        InitializeComponent();

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _meterTimer.Tick += (_, _) => UpdateMeterBars();

        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _durationTimer.Tick += (_, _) => UpdateConnectedDuration();

        _ringVisualizerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _ringVisualizerTimer.Tick += (_, _) => UpdateRingVisualizerBars();

        RingingWavePanel.ItemsSource = _ringBarHeights;
        SizeChanged += (_, _) => PositionDtmfKeypadIfOpen();
        DtmfOverlayHost.SizeChanged += (_, _) => PositionDtmfKeypadIfOpen();
    }

    public void Initialize(
        SipService sipService,
        RingtoneService ringtone,
        UserSettingsService settings,
        NetworkQualityService networkQuality)
    {
        _sipService = sipService;
        _ringtone = ringtone;
        _settings = settings;
        _networkQuality = networkQuality;
        _networkQuality.QualityUpdated += OnNetworkQualityUpdated;
        _sipService.CallQuality.QualityUpdated += OnMediaQualityUpdated;

        _ringtone.LevelChanged -= OnRingtoneLevelChanged;
        _ringtone.LevelChanged += OnRingtoneLevelChanged;
        _sipService.IncomingPlaybackPcm -= OnIncomingPlaybackPcm;
        _sipService.IncomingPlaybackPcm += OnIncomingPlaybackPcm;
        _sipService.OutgoingCapturePcm -= OnOutgoingCapturePcm;
        _sipService.OutgoingCapturePcm += OnOutgoingCapturePcm;

        VolumeSlider.Value = settings.Settings.OutputVolume;
        VolumeSlider.ValueChanged += (_, _) =>
        {
            _sipService?.SetCallOutputVolume(VolumeSlider.Value);
        };

        RefreshRecordingAvailability(settings.Settings.CallRecordingEnabled);
        UpdateCallState(sipService.CallState);
    }

    public void ShowCallWaiting(IncomingCallEventArgs callInfo)
    {
        var displayName = string.IsNullOrWhiteSpace(callInfo.CallerName)
            ? callInfo.CallerNumber
            : callInfo.CallerName;
        _waitingCallUiActive = true;
        WaitingCallStripText.Text = displayName;
        CallWaitingInlineCallerText.Text = displayName;
        CallWaitingInlineActiveText.Text = BuildActiveCallSummary();
        UpdateActiveCallStrip();
        DualCallBannerPanel.Visibility = Visibility.Visible;
        ActiveCallStrip.Visibility = Visibility.Visible;
        WaitingCallStrip.Visibility = Visibility.Visible;
        CallWaitingInlinePanel.Visibility = Visibility.Collapsed;
        UpdateWaitingCallActionButtons();
        HeaderText.Text = "Call Waiting";
        StatusText.BeginAnimation(UIElement.OpacityProperty, null);
        StatusText.Opacity = 1;
        StatusText.Text = $"Waiting: {displayName}";
        Visibility = Visibility.Visible;
    }

    public void RefreshRecordingAvailability(bool enabled)
    {
        var inCall = _sipService?.CallState is CallState.InCall or CallState.OnHold or CallState.CallWaitingRinging;
        RecordButton.IsEnabled = inCall && enabled;
        RecordButton.Opacity = enabled ? 1.0 : 0.45;
    }

    public void UpdateRecordingState(bool isRecording) =>
        SetRecordingButtonActive(isRecording);

    public void ShowIncoming(IncomingCallEventArgs callInfo)
    {
        var displayName = string.IsNullOrWhiteSpace(callInfo.CallerName)
            ? callInfo.CallerNumber
            : callInfo.CallerName;

        CallerNameText.Text = displayName;
        CallerNumberText.Text = callInfo.CallerNumber;
        CallerAvatar.DisplayName = displayName;
        CallerAvatar.Number = callInfo.CallerNumber;
        HeaderText.Text = callInfo.IsQueueCall ? "Queue Call" : "Incoming Call";
        CallerNameText.Visibility = string.Equals(displayName, callInfo.CallerNumber, StringComparison.Ordinal)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ShowIncomingState();
        Visibility = Visibility.Visible;

        if (_settings?.Settings.AutoAnswerEnabled == true)
        {
            _ = AnswerCallAsync();
        }
    }

    private void StartIncomingRingtoneIfNeeded()
    {
        if (_settings?.Settings.AutoAnswerEnabled == true)
        {
            return;
        }

        _ringtone?.Start(
            _settings!.Settings.RingtonePath,
            _settings.Settings.RingtoneDevice,
            _settings.Settings.RingtoneDeviceId);
    }

    public void UpdateCallState(CallState state)
    {
        switch (state)
        {
            case CallState.Incoming:
                ApplyRemotePartyDisplay(_sipService?.RemoteParty);
                HeaderText.Text = "Incoming Call";
                ShowIncomingState();
                Visibility = Visibility.Visible;
                StartIncomingRingtoneIfNeeded();
                break;

            case CallState.Outgoing:
                ApplyRemotePartyDisplay(_sipService?.RemoteParty);
                HeaderText.Text = "Calling...";
                StatusText.Text = "Dialing...";
                ShowOutgoingState();
                Visibility = Visibility.Visible;
                break;

            case CallState.CallWaitingRinging:
                ShowConnectedState(CallState.InCall, preserveCallWaiting: true);
                ShowCallWaiting(BuildWaitingCallInfo());
                StartIncomingRingtoneIfNeeded();
                break;

            case CallState.InCall:
            case CallState.OnHold:
                ApplyRemotePartyDisplay(_sipService?.RemoteParty);
                ShowConnectedState(state, preserveCallWaiting: IsCallWaitingUiActive());
                if (IsCallWaitingUiActive() && _sipService?.HasWaitingCall == true)
                {
                    ShowCallWaiting(BuildWaitingCallInfo());
                }

                Visibility = Visibility.Visible;
                break;

            default:
                HideSession();
                break;
        }
    }

    public async Task AnswerIncomingAsync() => await AnswerCallAsync();

    private void ApplyRemotePartyDisplay(string? remoteParty)
    {
        if (string.IsNullOrWhiteSpace(remoteParty))
        {
            return;
        }

        CallerNumberText.Text = remoteParty;
        CallerAvatar.DisplayName = CallerNameText.Text;
        CallerAvatar.Number = remoteParty;
        if (CallerNameText.Text == remoteParty || string.IsNullOrWhiteSpace(CallerNameText.Text))
        {
            CallerNameText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowIncomingState()
    {
        ResetCallControls();
        PulseRing.Visibility = Visibility.Visible;
        PulseRingOuter.Visibility = Visibility.Visible;
        RingingWavePanel.Visibility = Visibility.Visible;
        CallerAvatar.Visibility = Visibility.Visible;
        OutgoingSpinnerPanel.Visibility = Visibility.Collapsed;
        IncomingActionsPanel.Visibility = Visibility.Visible;
        CancelOutgoingButton.Visibility = Visibility.Collapsed;
        ConnectedActionsPanel.Visibility = Visibility.Collapsed;
        ConnectedSecondaryPanel.Visibility = Visibility.Collapsed;
        CloseDtmfKeypad();
        AudioMetersPanel.Visibility = Visibility.Collapsed;
        EndCallButton.Visibility = Visibility.Collapsed;
        NetworkQualityPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "Ringing...";
        StatusText.BeginAnimation(UIElement.OpacityProperty, null);
        StatusText.Opacity = 1;

        if (!_pulseStarted)
        {
            StartPulseAnimation();
            _pulseStarted = true;
        }

        if (!_ringVisualizerTimer.IsEnabled)
        {
            _ringVisualizerTimer.Start();
        }
    }

    private void ShowOutgoingState()
    {
        ResetCallControls();
        CallerNameText.Text = string.Empty;
        CallerNameText.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(_sipService?.RemoteParty))
        {
            CallerNumberText.Text = _sipService.RemoteParty;
        }

        PulseRing.Visibility = Visibility.Collapsed;
        PulseRingOuter.Visibility = Visibility.Collapsed;
        RingingWavePanel.Visibility = Visibility.Collapsed;
        OutgoingSpinnerPanel.Visibility = Visibility.Visible;
        CallerAvatar.Visibility = Visibility.Collapsed;
        IncomingActionsPanel.Visibility = Visibility.Collapsed;
        CancelOutgoingButton.Visibility = Visibility.Visible;
        ConnectedActionsPanel.Visibility = Visibility.Collapsed;
        ConnectedSecondaryPanel.Visibility = Visibility.Collapsed;
        CloseDtmfKeypad();
        AudioMetersPanel.Visibility = Visibility.Collapsed;
        EndCallButton.Visibility = Visibility.Collapsed;
        NetworkQualityPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowConnectedState(CallState state, bool preserveCallWaiting = false)
    {
        ResetCallControls();
        _ringtone?.Stop();
        _ringVisualizerTimer.Stop();
        ResetRingVisualizerBars();
        PulseRing.Visibility = Visibility.Collapsed;
        PulseRingOuter.Visibility = Visibility.Collapsed;
        RingingWavePanel.Visibility = Visibility.Collapsed;
        OutgoingSpinnerPanel.Visibility = Visibility.Collapsed;
        CallerAvatar.Visibility = Visibility.Visible;
        IncomingActionsPanel.Visibility = Visibility.Collapsed;
        CancelOutgoingButton.Visibility = Visibility.Collapsed;
        ConnectedActionsPanel.Visibility = Visibility.Visible;
        ConnectedSecondaryPanel.Visibility = Visibility.Visible;
        AudioMetersPanel.Visibility = Visibility.Visible;
        EndCallButton.Visibility = Visibility.Visible;

        var callWaitingActive = preserveCallWaiting
            || _waitingCallUiActive
            || _sipService?.CallState == CallState.CallWaitingRinging
            || _sipService?.HasWaitingCall == true;
        if (!callWaitingActive)
        {
            HideWaitingCallStrip();
        }
        else
        {
            CallWaitingInlinePanel.Visibility = Visibility.Collapsed;
        }

        UpdateActiveCallStrip();
        UpdateWaitingCallActionButtons();

        HeaderText.Text = state switch
        {
            CallState.OnHold => "On Hold",
            _ => "On Call"
        };
        StatusText.BeginAnimation(UIElement.OpacityProperty, null);
        StatusText.Opacity = 1;
        StatusText.Text = state switch
        {
            CallState.OnHold => "On hold",
            _ => FormatDurationLabel()
        };
        PulseRing.Stroke = (Brush)FindResource("PhoneAccentBrush");

        TransferButton.Visibility = Visibility.Visible;
        HoldButton.IsEnabled = true;

        if (_sipService is not null)
        {
            SetIconButtonActive(HoldButton, state == CallState.OnHold);
            HoldIcon.IconKey = state == CallState.OnHold ? "IconHoldActiveGeometry" : "IconHoldGeometry";
            SetIconButtonActive(MuteButton, _sipService.IsMuted);
            MuteIcon.IconKey = _sipService.IsMuted ? "IconMuteOffGeometry" : "IconMuteGeometry";
            MuteButton.ToolTip = _sipService.IsMuted ? "Unmute microphone" : "Mute microphone";
            SetIconButtonActive(SpeakerMuteButton, _sipService.IsSpeakerMuted);
            SpeakerMuteIcon.IconKey = _sipService.IsSpeakerMuted ? "IconSpeakerMuteGeometry" : "IconSpeakerGeometry";
            SpeakerMuteButton.ToolTip = _sipService.IsSpeakerMuted ? "Unmute speaker" : "Mute speaker";
            RecordButton.IsEnabled = _sipService.CanRecordLocally;
            RecordButton.Opacity = _sipService.CanRecordLocally ? 1.0 : 0.45;
            SetRecordingButtonActive(_sipService.IsRecording);
        }

        if (_settings is not null && !_meterTimer.IsEnabled)
        {
            _audioMeters.Start(
                _settings.Settings.MicrophoneDevice,
                _settings.Settings.SpeakerDevice,
                _settings.Settings.MicrophoneDeviceId,
                _settings.Settings.SpeakerDeviceId);
            _meterTimer.Start();
        }

        if (!_durationTimer.IsEnabled)
        {
            _durationTimer.Start();
        }

        NetworkQualityPanel.Visibility = Visibility.Collapsed;
        MediaQualityText.Visibility = Visibility.Collapsed;
        _networkQuality?.StartMonitoring();
        UpdateNetworkQuality(_networkQuality?.Current);
        UpdateMediaQuality(_sipService?.CallQuality.Current);
    }

    private void OnNetworkQualityUpdated(object? sender, NetworkQualitySnapshot snapshot) =>
        Dispatcher.Invoke(() => UpdateNetworkQuality(snapshot));

    private void OnMediaQualityUpdated(object? sender, CallMediaQualitySnapshot snapshot) =>
        Dispatcher.Invoke(() => UpdateMediaQuality(snapshot));

    private void UpdateMediaQuality(CallMediaQualitySnapshot? snapshot)
    {
        MediaQualityText.Visibility = Visibility.Collapsed;
        if (snapshot is null || snapshot.FramesReceived <= 0)
        {
            MediaQualityText.Text = "Call audio: waiting for RTP…";
            return;
        }

        MediaQualityText.Text =
            $"Call audio: {snapshot.Label} · loss~{snapshot.PacketLossPct:0.0}% · jitter~{snapshot.JitterMs:0.0} ms";
    }

    private void UpdateNetworkQuality(NetworkQualitySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        NetworkQualityLabelText.Text = snapshot.Label;
        NetworkQualityLabelText.Foreground = snapshot.Bars switch
        {
            >= 3 => (Brush)FindResource("PhoneCallGreenBrush"),
            2 => (Brush)FindResource("PhoneAccentBrush"),
            1 => (Brush)FindResource("PhoneAccentBrush"),
            _ => (Brush)FindResource("PhoneHangupRedBrush")
        };

        NetworkLatencyText.Text = snapshot.OptionsRttMs is long optionsMs
            ? $"OPTIONS {optionsMs} ms · Registered {(snapshot.RegistrationOk == true ? "yes" : "no")}"
            : snapshot.LatencyMs is long ms
                ? $"{ms} ms to server"
                : snapshot.IsRegistered
                    ? "Measuring OPTIONS..."
                    : "Not registered";

        var activeBrush = NetworkQualityLabelText.Foreground;
        var inactiveBrush = (Brush)FindResource("PhoneSurfaceElevatedBrush");
        NetworkBar1.Background = snapshot.Bars >= 1 ? activeBrush : inactiveBrush;
        NetworkBar2.Background = snapshot.Bars >= 2 ? activeBrush : inactiveBrush;
        NetworkBar3.Background = snapshot.Bars >= 3 ? activeBrush : inactiveBrush;
        NetworkBar4.Background = snapshot.Bars >= 4 ? activeBrush : inactiveBrush;
    }

    private void HideSession()
    {
        ResetCallControls();
        Visibility = Visibility.Collapsed;
        _ringtone?.Stop();
        _meterTimer.Stop();
        _durationTimer.Stop();
        _audioMeters.Stop();
        _networkQuality?.StopMonitoring();
        NetworkQualityPanel.Visibility = Visibility.Collapsed;
        HideDualCallBanner();
        CloseDtmfKeypad();
        _pulseStarted = false;
        _ringVisualizerTimer.Stop();
        ResetRingVisualizerBars();
    }

    private void UpdateActiveCallStrip()
    {
        if (_sipService is null)
        {
            return;
        }

        var showActiveStrip = _sipService.CallState is CallState.InCall
            or CallState.OnHold
            or CallState.CallWaitingRinging;

        if (!showActiveStrip)
        {
            HideDualCallBanner();
            return;
        }

        var showBanner = IsCallWaitingUiActive() || _sipService.HasHeldCall;
        if (!showBanner)
        {
            HideDualCallBanner();
            return;
        }

        var remoteParty = _sipService.RemoteParty;
        ActiveCallStripText.Text = string.IsNullOrWhiteSpace(remoteParty) ? "Connected" : remoteParty;
        ActiveCallStripDuration.Text = FormatDuration(_sipService.ActiveCallDuration);
        ActiveCallStrip.Visibility = Visibility.Visible;
        DualCallBannerPanel.Visibility = WaitingCallStrip.Visibility == Visibility.Visible
            || ActiveCallStrip.Visibility == Visibility.Visible
            || HeldCallStrip.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HideWaitingCallStrip()
    {
        _waitingCallUiActive = false;
        WaitingCallStrip.Visibility = Visibility.Collapsed;
        CallWaitingInlinePanel.Visibility = Visibility.Collapsed;
        if (ActiveCallStrip.Visibility != Visibility.Visible && HeldCallStrip.Visibility != Visibility.Visible)
        {
            HideDualCallBanner();
        }
    }

    private void HideDualCallBanner()
    {
        DualCallBannerPanel.Visibility = Visibility.Collapsed;
        ActiveCallStrip.Visibility = Visibility.Collapsed;
        WaitingCallStrip.Visibility = Visibility.Collapsed;
        HeldCallStrip.Visibility = Visibility.Collapsed;
        CallWaitingInlinePanel.Visibility = Visibility.Collapsed;
        SwitchCallsSecondaryButton.Visibility = Visibility.Collapsed;
        SwitchCallsSecondaryButton.IsEnabled = false;
        _waitingCallUiActive = false;
    }

    private void UpdateHeldCallUi()
    {
        if (_sipService?.HasHeldCall != true)
        {
            HeldCallStrip.Visibility = Visibility.Collapsed;
            SwitchCallsSecondaryButton.Visibility = Visibility.Collapsed;
            SwitchCallsSecondaryButton.IsEnabled = false;
            SwitchCallsButton.Visibility = Visibility.Collapsed;
            SwitchCallsButton.IsEnabled = false;
            SwitchCallsInlineButton.Visibility = Visibility.Collapsed;
            SwitchCallsInlineButton.IsEnabled = false;
            HeldCallSwitchButton.IsEnabled = false;
            return;
        }

        var heldParty = string.IsNullOrWhiteSpace(_sipService.HeldRemoteParty)
            ? "Other party"
            : _sipService.HeldRemoteParty;
        HeldCallStripText.Text = heldParty;
        HeldCallStrip.Visibility = Visibility.Visible;
        DualCallBannerPanel.Visibility = Visibility.Visible;
        ActiveCallStrip.Visibility = Visibility.Visible;

        var switchLabel = _sipService.IsWaitingCallLegActive
            ? $"Switch to {heldParty}"
            : "Switch calls";
        SwitchCallsSecondaryButton.Content = switchLabel;
        SwitchCallsSecondaryButton.Visibility = Visibility.Visible;
        SwitchCallsSecondaryButton.IsEnabled = true;
        HeldCallSwitchButton.IsEnabled = true;

        StatusText.Text = $"On hold: {heldParty} · Active: {_sipService.RemoteParty}";
    }

    private bool IsCallWaitingUiActive() =>
        (_waitingCallUiActive
         || _sipService?.CallState == CallState.CallWaitingRinging
         || _sipService?.HasWaitingCall == true)
        && _sipService?.HasWaitingCall == true;

    private IncomingCallEventArgs BuildWaitingCallInfo() =>
        new(
            _sipService?.WaitingCallerNumber ?? "Unknown",
            _sipService?.WaitingCallerName);

    private string BuildActiveCallSummary()
    {
        var remoteParty = _sipService?.RemoteParty;
        return string.IsNullOrWhiteSpace(remoteParty)
            ? "Active call in progress"
            : $"Active call: {remoteParty}";
    }

    private void UpdateWaitingCallActionButtons()
    {
        var canSwitch = _sipService?.HasHeldCall == true;
        SwitchCallsButton.Visibility = canSwitch ? Visibility.Visible : Visibility.Collapsed;
        SwitchCallsButton.IsEnabled = canSwitch;
        SwitchCallsInlineButton.Visibility = canSwitch ? Visibility.Visible : Visibility.Collapsed;
        SwitchCallsInlineButton.IsEnabled = canSwitch;
        UpdateHeldCallUi();
    }

    private void OnIncomingPlaybackPcm(byte[] pcm, int length) =>
        _audioMeters.FeedIncomingPcm(pcm, length);

    private void OnOutgoingCapturePcm(byte[] pcm, int length) =>
        _audioMeters.FeedOutgoingPcm(pcm, length);

    private void OnRingtoneLevelChanged(object? sender, double level)
    {
        Dispatcher.Invoke(() =>
        {
            for (var i = 0; i < _ringBarTargets.Length; i++)
            {
                var spread = 0.55 + (i * 0.12);
                var jitter = 0.85 + ((i % 3) * 0.08);
                _ringBarTargets[i] = Math.Max(6, 6 + (level * 18 * spread * jitter));
            }
        });
    }

    private void UpdateRingVisualizerBars()
    {
        for (var i = 0; i < _ringBarHeights.Count; i++)
        {
            var current = _ringBarHeights[i];
            var target = _ringBarTargets[i];
            _ringBarHeights[i] = current + ((target - current) * 0.35);
        }
    }

    private void ResetRingVisualizerBars()
    {
        for (var i = 0; i < _ringBarHeights.Count; i++)
        {
            _ringBarHeights[i] = 8;
            _ringBarTargets[i] = 8;
        }
    }

    public void ShowStatusMessage(string message) => StatusText.Text = message;

    private void StartPulseAnimation()
    {
        var animation = new DoubleAnimation(1.0, 1.12, TimeSpan.FromSeconds(0.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        foreach (var ring in new[] { PulseRing, PulseRingOuter })
        {
            var scale = new ScaleTransform(1, 1);
            ring.RenderTransform = scale;
            ring.RenderTransformOrigin = new Point(0.5, 0.5);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }

    private async void AnswerButton_Click(object sender, RoutedEventArgs e) =>
        await AnswerCallAsync();

    private async Task AnswerCallAsync()
    {
        if (_sipService is null)
        {
            return;
        }

        _ringtone?.StopForAnswer();
        AnswerButton.IsEnabled = false;
        DeclineButton.IsEnabled = false;
        StatusText.Text = "Connecting...";

        try
        {
            await _sipService.AnswerAsync();
            if (_sipService.CallState is CallState.InCall or CallState.OnHold)
            {
                ShowConnectedState(_sipService.CallState);
                SetRecordingButtonActive(_sipService.IsRecording);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void DeclineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService is null)
        {
            return;
        }

        DeclineButton.IsEnabled = false;
        _ringtone?.StopForAnswer();

        try
        {
            await _sipService.DeclineIncomingAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            DeclineButton.IsEnabled = true;
            AnswerButton.IsEnabled = true;
        }
    }

    private async void CancelOutgoingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService is null)
        {
            return;
        }

        CancelOutgoingButton.IsEnabled = false;
        try
        {
            await _sipService.HangupAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            CancelOutgoingButton.IsEnabled = true;
        }
    }

    private async void EndCallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService is null)
        {
            return;
        }

        EndCallButton.IsEnabled = false;
        try
        {
            await _sipService.HangupAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            EndCallButton.IsEnabled = true;
        }
    }

    private async void HoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService is null)
        {
            return;
        }

        try
        {
            await _sipService.ToggleHoldAsync();
            UpdateCallState(_sipService.CallState);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService is null)
        {
            return;
        }

        try
        {
            await _sipService.ToggleMuteAsync();
            SetIconButtonActive(MuteButton, _sipService.IsMuted);
            MuteIcon.IconKey = _sipService.IsMuted ? "IconMuteOffGeometry" : "IconMuteGeometry";
            MuteButton.ToolTip = _sipService.IsMuted ? "Unmute microphone" : "Mute microphone";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void SpeakerMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService is null)
        {
            return;
        }

        try
        {
            await _sipService.ToggleSpeakerMuteAsync();
            SetIconButtonActive(SpeakerMuteButton, _sipService.IsSpeakerMuted);
            SpeakerMuteIcon.IconKey = _sipService.IsSpeakerMuted ? "IconSpeakerMuteGeometry" : "IconSpeakerGeometry";
            SpeakerMuteButton.ToolTip = _sipService.IsSpeakerMuted ? "Unmute speaker" : "Mute speaker";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void AnswerWaitingButton_Click(object sender, RoutedEventArgs e) =>
        await RunWaitingCallAction(() => _sipService!.EndAndAnswerWaitingCallAsync());

    private async void DeclineWaitingButton_Click(object sender, RoutedEventArgs e) =>
        await RunWaitingCallAction(() => _sipService!.DeclineWaitingCallAsync());

    private async void HoldAnswerWaitingButton_Click(object sender, RoutedEventArgs e) =>
        await RunWaitingCallAction(() => _sipService!.HoldAndAnswerWaitingCallAsync());

    private async void SwitchCallsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService is null)
        {
            return;
        }

        try
        {
            await _sipService.SwitchCallsAsync();
            UpdateCallState(_sipService.CallState);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task RunWaitingCallAction(Func<Task> action)
    {
        if (_sipService is null)
        {
            return;
        }

        try
        {
            _ringtone?.StopForAnswer();
            HideWaitingCallStrip();
            await action();
            UpdateCallState(_sipService.CallState);
            UpdateHeldCallUi();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService is null)
        {
            return;
        }

        if (!_sipService.CanRecordLocally)
        {
            StatusText.Text = "Enable local call recording in Settings before recording.";
            return;
        }

        try
        {
            await _sipService.ToggleRecordingAsync();
            SetRecordingButtonActive(_sipService.IsRecording);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void TransferButton_Click(object sender, RoutedEventArgs e) =>
        BlindTransferRequested?.Invoke(this, EventArgs.Empty);

    private void KeypadToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (DtmfOverlayHost.Visibility == Visibility.Visible)
        {
            CloseDtmfKeypad();
            return;
        }

        OpenDtmfKeypad();
    }

    private void OpenDtmfKeypad()
    {
        KeypadToggleButton.Content = "Hide Keypad";
        DtmfOverlayHost.Visibility = Visibility.Visible;
        DtmfOverlayHost.UpdateLayout();
        if (!TryPositionDtmfKeypad())
        {
            Dispatcher.BeginInvoke(() => TryPositionDtmfKeypad(), DispatcherPriority.Loaded);
        }

        Dispatcher.BeginInvoke(() =>
        {
            TryPositionDtmfKeypad();
            DtmfInput.Focus();
            DtmfInput.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void PositionDtmfKeypadIfOpen()
    {
        if (DtmfOverlayHost.Visibility == Visibility.Visible)
        {
            TryPositionDtmfKeypad();
        }
    }

    private void CloseDtmfKeypad()
    {
        DtmfOverlayHost.Visibility = Visibility.Collapsed;
        InCallKeypadPanel.Opacity = 0;
        KeypadToggleButton.Content = "Keypad";
    }

    private void DtmfOverlayHost_MouseDown(object sender, MouseButtonEventArgs e)
    {
        CloseDtmfKeypad();
        e.Handled = true;
    }

    private void InCallKeypadPanel_MouseDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;

    private void CallSessionView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DtmfOverlayHost.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            CloseDtmfKeypad();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = SendUnsentDtmfAsync();
            e.Handled = true;
            return;
        }

        if (TryGetDtmfChar(e) is char tone)
        {
            _ = SendImmediateDtmfAsync(tone, replaceSelection: true);
            _dtmfHandledFromKeyDown = true;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Space or Key.PageUp or Key.PageDown)
        {
            e.Handled = true;
        }
    }

    private void DtmfInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_dtmfHandledFromKeyDown)
        {
            _dtmfHandledFromKeyDown = false;
            e.Handled = true;
            return;
        }

        if (string.IsNullOrEmpty(e.Text) || e.Text.Any(ch => !IsDtmfChar(ch)))
        {
            e.Handled = true;
            return;
        }

        foreach (var ch in e.Text)
        {
            _ = SendImmediateDtmfAsync(ch, replaceSelection: true);
        }

        e.Handled = true;
    }

    private bool TryPositionDtmfKeypad()
    {
        if (DtmfOverlayHost.Visibility != Visibility.Visible
            || !DtmfAnchor.IsVisible
            || !DtmfAnchor.IsArrangeValid
            || DtmfAnchor.ActualWidth < 1
            || DtmfAnchor.ActualHeight < 1
            || DtmfOverlayHost.ActualWidth < 1
            || DtmfOverlayHost.ActualHeight < 1)
        {
            return false;
        }

        InCallKeypadPanel.Measure(new Size(DtmfOverlayHost.ActualWidth, DtmfOverlayHost.ActualHeight));
        InCallKeypadPanel.UpdateLayout();
        var popupWidth = InCallKeypadPanel.ActualWidth > 1
            ? InCallKeypadPanel.ActualWidth
            : InCallKeypadPanel.DesiredSize.Width;
        var popupHeight = InCallKeypadPanel.ActualHeight > 1
            ? InCallKeypadPanel.ActualHeight
            : InCallKeypadPanel.DesiredSize.Height;
        if (popupWidth < 1 || popupHeight < 1)
        {
            return false;
        }

        var origin = DtmfAnchor.TranslatePoint(new Point(0, 0), DtmfOverlayHost);
        if (origin.X == 0
            && origin.Y == 0
            && DtmfOverlayHost.ActualHeight > DtmfAnchor.ActualHeight * 2)
        {
            return false;
        }

        var desiredLeft = origin.X + ((DtmfAnchor.ActualWidth - popupWidth) / 2);
        var desiredTop = origin.Y - popupHeight - 6;

        var pad = 8.0;
        var maxLeft = Math.Max(pad, DtmfOverlayHost.ActualWidth - popupWidth - pad);
        var maxTop = Math.Max(pad, DtmfOverlayHost.ActualHeight - popupHeight - pad);
        var left = Math.Clamp(desiredLeft, pad, maxLeft);
        var top = Math.Clamp(desiredTop, pad, maxTop);
        InCallKeypadPanel.Margin = new Thickness(left, top, 0, 0);
        InCallKeypadPanel.Opacity = 1;
        return true;
    }

    private async void DtmfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tone } || tone.Length != 1)
        {
            return;
        }

        await SendImmediateDtmfAsync(tone[0], replaceSelection: true);
        RestoreDtmfInputFocus(selectAll: false);
    }

    private async Task SendImmediateDtmfAsync(char tone, bool replaceSelection)
    {
        if (_sipService is null || !IsDtmfChar(tone))
        {
            return;
        }

        ApplyDtmfDigitToInput(tone, replaceSelection);
        try
        {
            await _sipService.SendDtmfAsync(tone);
            _dtmfSentCount = DtmfInput.Text.Length;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task SendUnsentDtmfAsync()
    {
        if (_sipService is null)
        {
            return;
        }

        var text = new string((DtmfInput.Text ?? string.Empty).Where(IsDtmfChar).ToArray());
        if (DtmfInput.Text != text)
        {
            DtmfInput.Text = text;
        }

        _dtmfSentCount = Math.Clamp(_dtmfSentCount, 0, text.Length);
        try
        {
            for (var i = _dtmfSentCount; i < text.Length; i++)
            {
                await _sipService.SendDtmfAsync(text[i]);
                _dtmfSentCount = i + 1;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void ApplyDtmfDigitToInput(char tone, bool replaceSelection)
    {
        var text = DtmfInput.Text ?? string.Empty;
        var start = DtmfInput.SelectionStart;
        var length = DtmfInput.SelectionLength;
        if (replaceSelection && length > 0)
        {
            text = text.Remove(start, length).Insert(start, tone.ToString());
            if (length == (DtmfInput.Text ?? string.Empty).Length)
            {
                _dtmfSentCount = 0;
            }
            else
            {
                _dtmfSentCount = Math.Min(_dtmfSentCount, start);
            }

            DtmfInput.Text = text;
            DtmfInput.CaretIndex = start + 1;
            return;
        }

        var insertAt = Math.Clamp(DtmfInput.CaretIndex, 0, text.Length);
        DtmfInput.Text = text.Insert(insertAt, tone.ToString());
        DtmfInput.CaretIndex = insertAt + 1;
    }

    private void RestoreDtmfInputFocus(bool selectAll)
    {
        Dispatcher.BeginInvoke(() =>
        {
            DtmfInput.Focus();
            if (selectAll)
            {
                DtmfInput.SelectAll();
            }
            else
            {
                DtmfInput.CaretIndex = DtmfInput.Text?.Length ?? 0;
            }
        }, DispatcherPriority.Input);
    }

    private static bool IsDtmfChar(char tone) =>
        tone is >= '0' and <= '9' or '*' or '#';

    private static char? TryGetDtmfChar(KeyEventArgs e)
    {
        if (e.Key is >= Key.D0 and <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None)
        {
            return (char)('0' + (e.Key - Key.D0));
        }

        if (e.Key is >= Key.NumPad0 and <= Key.NumPad9 && Keyboard.Modifiers == ModifierKeys.None)
        {
            return (char)('0' + (e.Key - Key.NumPad0));
        }

        if (e.Key == Key.Multiply || (e.Key == Key.D8 && Keyboard.Modifiers == ModifierKeys.Shift))
        {
            return '*';
        }

        if (e.Key == Key.D3 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            return '#';
        }

        return null;
    }

    private void ClearDtmfDisplay()
    {
        _dtmfSentCount = 0;
        if (DtmfInput is not null)
        {
            DtmfInput.Text = string.Empty;
        }
    }

    private void UpdateMeterBars()
    {
        IncomingLevelBar.Value = _audioMeters.IncomingLevel * 100;
        OutgoingLevelBar.Value = _audioMeters.OutgoingLevel * 100;
    }

    private void UpdateConnectedDuration()
    {
        if (_sipService?.CallState is not (CallState.InCall or CallState.OnHold or CallState.CallWaitingRinging))
        {
            _durationTimer.Stop();
            return;
        }

        var duration = FormatDuration(_sipService.ActiveCallDuration);
        ActiveCallStripDuration.Text = duration;
        if (_waitingCallUiActive)
        {
            CallWaitingInlineActiveText.Text = BuildActiveCallSummary();
        }

        StatusText.Text = _sipService.CallState switch
        {
            CallState.OnHold => $"On hold · {duration}",
            _ => FormatDurationLabel()
        };
    }

    private string FormatDurationLabel()
    {
        if (_sipService is null)
        {
            return "Connected";
        }

        return FormatDuration(_sipService.ActiveCallDuration);
    }

    internal static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }

        return $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
    }

    private void SetIconButtonActive(Button button, bool active)
    {
        button.Style = (Style)FindResource(active ? "InCallIconButtonActive" : "InCallIconButton");
    }

    private void SetRecordingButtonActive(bool active)
    {
        RecordButton.Style = (Style)FindResource(active ? "InCallIconButtonRecording" : "InCallIconButton");
    }

    private void ResetCallControls()
    {
        EndCallButton.IsEnabled = true;
        CancelOutgoingButton.IsEnabled = true;
        AnswerButton.IsEnabled = true;
        DeclineButton.IsEnabled = true;
        ClearDtmfDisplay();
    }
}
