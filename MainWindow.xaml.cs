using System.Windows.Threading;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;
using CallAnalog.Softphone.Views;
using CallAnalog.Softphone.Views.Panels;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone;

public partial class MainWindow : Window
{
    private const double WidthScreenRatio = 0.1725 * 1.30 * (300.0 / 320.0);
    private const double HeightScreenRatio = 0.575 * 1.40 * (300.0 / 320.0);

    private readonly LoginService _loginService;
    private readonly UserSettingsService _userSettings;
    private readonly PbxApiClient _pbxApiClient;
    private readonly ContactsService _contactsService;
    private readonly CallHistoryService _callHistoryService;
    private readonly SipLogService _sipLog;
    private readonly CarrierProvisioningService _carrierProvisioning;
    private readonly SipService _sipService;
    private readonly DialService _dialService;
    private readonly CallNoteService _callNoteService;
    private readonly AppVersionCheckService _appVersionCheckService;
    private readonly NetworkQualityService _networkQuality;
    private readonly RingtoneService _ringtone = new();
    private readonly RingbackService _ringback = new();
    private readonly DispatcherTimer _callDurationTimer;
    private readonly string _unregisterPath;
    private bool _isBusy;
    private string _loggedInExtension = string.Empty;

    private ConnectionStatus _currentStatus = ConnectionStatus.Offline;
    private FrameworkElement? _currentPage;
    private int _currentPageIndex = -1;
    private bool _isNavigating;
    private bool _forceClosing;
    private readonly ContactLookupService _contactLookup = new();
    private GlobalHotkeyService? _globalHotkeys;
    private int _unreadMissedCalls;
    private bool _isSplashVisible;
    private bool _startupVersionCheckStarted;
    private string? _pendingUpdateBannerMessage;
    private double _navSwipeAccumulatedX;
    private double _navSwipeAccumulatedY;
    private CancellationTokenSource? _loginCts;
    private IncomingCallPopupWindow? _incomingCallPopup;

    private const int PageDashboard = 0;
    private const int PageHistory = 1;
    private const int PageContacts = 2;
    private const int PageSettings = 3;
    private const int PageDialpad = 4;

    public MainWindow()
    {
        InitializeComponent();

        var configuration = App.Configuration;
        _loginService = new LoginService(configuration);
        _userSettings = App.UserSettings;
        _pbxApiClient = new PbxApiClient(configuration);
        _contactsService = new ContactsService(_pbxApiClient, configuration);
        _callHistoryService = new CallHistoryService(_pbxApiClient, configuration);
        _sipLog = App.SipLog;
        _carrierProvisioning = new CarrierProvisioningService(_pbxApiClient, _userSettings, _sipLog, configuration);
        _sipService = new SipService(configuration, _userSettings, _sipLog);
        _dialService = new DialService(_sipService, _sipLog);
        _callNoteService = new CallNoteService(_pbxApiClient, configuration);
        _appVersionCheckService = new AppVersionCheckService(_pbxApiClient, configuration);
        _networkQuality = new NetworkQualityService(configuration);
        _networkQuality.ConfigureRegistrationProvider(() => _sipService.RegistrationState);
        _networkQuality.ConfigureOptionsRttProvider(() => _sipService.ProbeOptionsRttAsync());
        _unregisterPath = configuration["PbxApi:UnregisterPath"]
            ?? "/public/api/extensionUnregisterFromOpenSipsOtherApp/";

        _sipService.RegistrationStateChanged += (_, state) =>
        {
            Dispatcher.Invoke(() =>
            {
                ApplyRegistrationStatusFromSip();
            });
        };

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        _sipService.IncomingCall += OnIncomingCall;
        _sipService.IncomingCallWaiting += OnIncomingCallWaiting;
        _sipService.IncomingCallRejectedWhileBusy += OnIncomingCallRejectedWhileBusy;
        App.TrayIcon.IncomingCallNotificationAction += OnIncomingCallNotificationAction;
        _sipService.CallStateChanged += (_, state) =>
            RunOnUiThread(() => UpdateActiveCallUi(state));
        _sipService.RecordingStateChanged += (_, isRecording) =>
            RunOnUiThread(() => CallSessionView.UpdateRecordingState(isRecording));

        _callDurationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _callDurationTimer.Tick += (_, _) => UpdateCallDurationDisplay();

        _sipService.CallEnded += OnCallEnded;

        SourceInitialized += (_, _) => InitializeGlobalHotkeys();
        Activated += (_, _) => _sipService.EnsureCallStateConsistentWithSession();
        StateChanged += MainWindow_StateChanged;

        VersionText.Text = BuildInfo.FullBuildLabel;
        _userSettings.RestoreCachedPublicIp(_sipLog);
        LoadRememberedCredentials();
    }

    private bool CanUseFastAutoLogin(string extension) =>
        RememberMeCheckBox.IsChecked == true
        && SipDomainParser.IsUsableHost(_userSettings.Settings.CarrierHost)
        && !string.IsNullOrWhiteSpace(extension)
        && !string.IsNullOrEmpty(PasswordBox.Password);

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (AppShellPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            if (NetworkInterface.GetIsNetworkAvailable())
            {
                _sipLog.Info(SipLogTag.Network, "Windows reported network availability restored.");
                _sipService.NotifyNetworkRestored();
            }
            else
            {
                _sipLog.Warn(SipLogTag.Network, "Windows reported network unavailable.");
                _sipService.NotifyNetworkLost();
            }
        });
    }

    private void ApplyRegistrationStatusFromSip()
    {
        var loggedIn = AppShellPanel.Visibility == Visibility.Visible;
        SetConnectionStatus(_sipService.RegistrationState switch
        {
            SipRegistrationState.Registered => ConnectionStatus.Online,
            SipRegistrationState.Registering => ConnectionStatus.Registering,
            SipRegistrationState.Reconnecting => ConnectionStatus.Reconnecting,
            SipRegistrationState.Failed when loggedIn => ConnectionStatus.Disconnected,
            SipRegistrationState.Failed => ConnectionStatus.Offline,
            _ => loggedIn ? ConnectionStatus.Disconnected : ConnectionStatus.Offline
        });
    }

    private void OnIncomingCallRejectedWhileBusy(object? sender, IncomingCallEventArgs e) =>
        RunOnUiThread(() =>
        {
            IncrementMissedCallBadge();
            App.TrayIcon.ShowMissedCallNotification(EnrichIncomingCall(e));
        });

    private bool _incomingWasRinging;

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.BeginInvoke(action);
    }

    private void InitializeGlobalHotkeys()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _globalHotkeys?.Dispose();
        _globalHotkeys = new GlobalHotkeyService(handle);
        _globalHotkeys.AnswerRequested += (_, _) => RunOnUiThread(() => _ = HandleGlobalAnswerHotkeyAsync());
        _globalHotkeys.HangupRequested += (_, _) => RunOnUiThread(() => _ = HandleGlobalHangupHotkeyAsync());
        _globalHotkeys.MuteRequested += (_, _) => RunOnUiThread(() => _ = HandleGlobalMuteHotkeyAsync());
        _globalHotkeys.Register();
    }

    private async Task HandleGlobalAnswerHotkeyAsync()
    {
        if (AppShellPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        if (_sipService.CallState == CallState.Incoming)
        {
            await CallSessionView.AnswerIncomingAsync();
        }
        else if (_sipService.CallState == CallState.CallWaitingRinging)
        {
            await _sipService.HoldAndAnswerWaitingCallAsync();
        }
    }

    private async Task HandleGlobalHangupHotkeyAsync()
    {
        if (AppShellPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        if (_sipService.CallState is CallState.Incoming)
        {
            await _sipService.DeclineIncomingAsync();
        }
        else if (_sipService.CallState is CallState.Outgoing
            or CallState.InCall
            or CallState.OnHold
            or CallState.CallWaitingRinging)
        {
            await _sipService.HangupAsync();
        }
    }

    private async Task HandleGlobalMuteHotkeyAsync()
    {
        if (AppShellPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        if (_sipService.CallState is CallState.InCall or CallState.OnHold or CallState.CallWaitingRinging)
        {
            await _sipService.ToggleMuteAsync();
            CallSessionView.UpdateCallState(_sipService.CallState);
        }
    }

    private void NavSwipeHost_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
    {
        if (AppShellPanel.Visibility != Visibility.Visible
            || _currentPageIndex is PageSettings or PageDialpad
            || _isNavigating
            || _sipService.CallState is not CallState.Idle
            || CallSessionView.Visibility == Visibility.Visible)
        {
            e.Cancel();
            return;
        }

        e.ManipulationContainer = NavSwipeHost;
    }

    private void NavSwipeHost_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
    {
        _navSwipeAccumulatedX += e.DeltaManipulation.Translation.X;
        _navSwipeAccumulatedY += e.DeltaManipulation.Translation.Y;
    }

    private void NavSwipeHost_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
    {
        const double threshold = 80;
        var horizontal = Math.Abs(_navSwipeAccumulatedX);
        var vertical = Math.Abs(_navSwipeAccumulatedY);

        if (horizontal < threshold || horizontal <= vertical * 1.2)
        {
            ResetNavSwipeTracking();
            return;
        }

        if (_navSwipeAccumulatedX < 0)
        {
            SwipeToNextNavPage();
        }
        else
        {
            SwipeToPreviousNavPage();
        }

        ResetNavSwipeTracking();
    }

    private void ResetNavSwipeTracking()
    {
        _navSwipeAccumulatedX = 0;
        _navSwipeAccumulatedY = 0;
    }

    private void NavSwipeHost_ManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e) =>
        e.Handled = true;

    private void SwipeToNextNavPage()
    {
        switch (_currentPageIndex)
        {
            case PageDashboard:
                ShowHistory();
                break;
            case PageHistory:
                ShowContacts();
                break;
        }
    }

    private void SwipeToPreviousNavPage()
    {
        switch (_currentPageIndex)
        {
            case PageContacts:
                ShowHistory();
                break;
            case PageHistory:
                ShowDashboard();
                break;
        }
    }

    private void ShowLaunchSplash(string step)
    {
        _isSplashVisible = true;
        SplashStepText.Text = step;
        LaunchSplashOverlay.Visibility = Visibility.Visible;
    }

    private void HideLaunchSplash()
    {
        _isSplashVisible = false;
        LaunchSplashOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateSplashStep(string step)
    {
        if (_isSplashVisible)
        {
            SplashStepText.Text = step;
        }
    }

    private void IncrementMissedCallBadge()
    {
        _unreadMissedCalls++;
        UpdateMissedCallBadgeUi();
    }

    private void ClearMissedCallBadge()
    {
        _unreadMissedCalls = 0;
        UpdateMissedCallBadgeUi();
    }

    private void UpdateMissedCallBadgeUi()
    {
        if (_unreadMissedCalls <= 0)
        {
            HistoryMissedBadge.Visibility = Visibility.Collapsed;
            return;
        }

        HistoryMissedBadgeText.Text = _unreadMissedCalls > 99 ? "99+" : _unreadMissedCalls.ToString();
        HistoryMissedBadge.Visibility = Visibility.Visible;
    }

    private IncomingCallEventArgs EnrichIncomingCall(IncomingCallEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.CallerName))
        {
            return args;
        }

        var resolved = _contactLookup.ResolveName(_loggedInExtension, args.CallerNumber);
        return string.IsNullOrWhiteSpace(resolved)
            ? args
            : new IncomingCallEventArgs(args.CallerNumber, resolved, args.IsQueueCall);
    }

    private void LoadRememberedCredentials()
    {
        var (extension, password, rememberMe) = _userSettings.LoadRememberedLogin();
        if (!rememberMe)
        {
            return;
        }

        ExtensionBox.Text = extension;
        PasswordBox.Password = password;
        RememberMeCheckBox.IsChecked = true;
    }

    private void PhoneScreenBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PhoneScreenBorder.ActualWidth <= 0 || PhoneScreenBorder.ActualHeight <= 0)
        {
            return;
        }

        PhoneScreenBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, PhoneScreenBorder.ActualWidth, PhoneScreenBorder.ActualHeight),
            PhoneScreenBorder.CornerRadius.TopLeft,
            PhoneScreenBorder.CornerRadius.TopLeft);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyScreenRelativeSize();
        UpdateLayout();
        CenterOnWorkArea();

        if (ShouldAutoLogin())
        {
            ShowLaunchSplash("Signing in automatically...");
            _ = AttemptLoginAsync(isAutoLogin: true);
            return;
        }

        ExtensionBox.Focus();
    }

    private bool ShouldAutoLogin() =>
        RememberMeCheckBox.IsChecked == true
        && !string.IsNullOrWhiteSpace(ExtensionBox.Text)
        && !string.IsNullOrEmpty(PasswordBox.Password)
        && AppShellPanel.Visibility != Visibility.Visible;

    private void ApplyScreenRelativeSize()
    {
        var workArea = SystemParameters.WorkArea;
        Width = workArea.Width * WidthScreenRatio;
        Height = workArea.Height * HeightScreenRatio;
        MinWidth = Width * 0.85;
        MinHeight = Height * 0.85;
    }

    private void CenterOnWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + (area.Height - ActualHeight) / 2;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e) =>
        App.TrayIcon.HideToTray();

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppShellPanel.Visibility == Visibility.Visible && !_forceClosing)
        {
            var confirmed = await ConfirmAsync(
                "Exit CallAnalog?",
                "You will be signed out and the application will close.",
                "Exit",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            _forceClosing = true;
            await SignOutAsync();
        }

        if (Application.Current is App app)
        {
            app.RequestShutdown();
        }

        Close();
    }

    public void ForceExit()
    {
        _forceClosing = true;
        CloseIncomingCallPopup(force: true);
        if (Application.Current is App app)
        {
            app.RequestShutdown();
        }

        Close();
    }

    private async void SignOutButton_Click(object sender, RoutedEventArgs e) =>
        await SignOutAsync();

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _globalHotkeys?.Dispose();
        _globalHotkeys = null;

        try
        {
            CloseIncomingCallPopup(force: true);
            _ringtone.Stop();
            if (_sipService.CallState == CallState.Incoming)
            {
                await _sipService.DeclineIncomingAsync();
            }
            else
            {
                await _sipService.HangupAsync();
            }
            await _sipService.UnregisterAsync();
        }
        catch
        {
            // Best-effort cleanup on exit.
        }
    }

    private void OnCallEnded(object? sender, CallEndedEventArgs e)
    {
        if (!e.WasConnected)
        {
            return;
        }

        var callId = SipCallIdHelper.Normalize(e.SipCallId);
        if (string.IsNullOrWhiteSpace(callId) || _dismissedWrapUpCallIds.Contains(callId))
        {
            return;
        }

        _wrapUpCts?.Cancel();
        _wrapUpCts?.Dispose();
        _wrapUpCts = new CancellationTokenSource();
        var wrapUpToken = _wrapUpCts.Token;

        Dispatcher.InvokeAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(_loggedInExtension))
            {
                return;
            }

            if (wrapUpToken.IsCancellationRequested)
            {
                return;
            }

            // Wrap-up runs after the SIP session is idle; skip if a new call already started.
            if (_sipService.CallState != CallState.Idle)
            {
                return;
            }

            await CallWrapUpPanel.ShowAsync(
                this,
                _callNoteService,
                _callHistoryService,
                _loggedInExtension,
                e,
                wrapUpToken);

            _dismissedWrapUpCallIds.Add(callId);
        });
    }

    private void OnIncomingCallWaiting(object? sender, IncomingCallEventArgs e) =>
        RunOnUiThread(() =>
        {
            if (string.IsNullOrWhiteSpace(_loggedInExtension))
            {
                return;
            }

            var callInfo = EnrichIncomingCall(e);
            DismissActiveShellPanel();
            HideIncomingCallPopup();
            CallSessionView.Visibility = Visibility.Visible;
            CallSessionView.ShowCallWaiting(callInfo);
            FlashWindow();

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                ShowInTaskbar = true;
            }

            Activate();

            if (ShouldShowIncomingToast(IncomingCallNotificationKind.CallWaiting))
            {
                App.TrayIcon.ShowIncomingNotification(callInfo, IncomingCallNotificationKind.CallWaiting);
            }
            else if (WindowState == WindowState.Minimized)
            {
                App.TrayIcon.ShowMainWindow();
            }
        });

    private void OnIncomingCall(object? sender, IncomingCallEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (string.IsNullOrWhiteSpace(_loggedInExtension))
            {
                return;
            }

            var callInfo = EnrichIncomingCall(e);
            _wrapUpCts?.Cancel();
            DismissActiveShellPanel();

            CallSessionView.ShowIncoming(callInfo);
            FlashWindow();

            var popupShown = TryShowMinimizedIncomingPopup(callInfo);
            if (popupShown)
            {
                App.TrayIcon.DismissIncomingCallNotification();
            }
            else if (ShouldShowIncomingToast(IncomingCallNotificationKind.Incoming))
            {
                App.TrayIcon.ShowIncomingNotification(callInfo);
            }
            else if (_userSettings?.Settings.AutoAnswerEnabled == true)
            {
                App.TrayIcon.DismissIncomingCallNotification();
            }
            else if (WindowState == WindowState.Minimized)
            {
                App.TrayIcon.ShowMainWindow();
            }
        });
    }

    private void OnIncomingCallNotificationAction(object? sender, IncomingCallNotificationActionEventArgs e) =>
        RunOnUiThread(() => _ = HandleIncomingCallNotificationActionAsync(e));

    private async Task HandleIncomingCallNotificationActionAsync(IncomingCallNotificationActionEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_loggedInExtension))
        {
            App.TrayIcon.DismissAllCallNotifications();
            return;
        }

        switch (e.Action)
        {
            case IncomingCallNotificationAction.Open:
                App.TrayIcon.ShowMainWindow();
                break;

            case IncomingCallNotificationAction.Accept:
                App.TrayIcon.ShowMainWindow();
                try
                {
                    if (e.Kind == IncomingCallNotificationKind.CallWaiting)
                    {
                        await _sipService.HoldAndAnswerWaitingCallAsync();
                    }
                    else
                    {
                        await CallSessionView.AnswerIncomingAsync();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "CallAnalog",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                break;

            case IncomingCallNotificationAction.Decline:
                try
                {
                    if (e.Kind == IncomingCallNotificationKind.CallWaiting)
                    {
                        await _sipService.DeclineWaitingCallAsync();
                    }
                    else
                    {
                        await _sipService.DeclineIncomingAsync();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "CallAnalog",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                break;
        }

        if (e.Kind == IncomingCallNotificationKind.CallWaiting)
        {
            App.TrayIcon.DismissCallWaitingNotification();
        }
        else
        {
            App.TrayIcon.DismissIncomingCallNotification();
        }
    }

    private bool IsAppBackgrounded() =>
        !IsVisible || WindowState == WindowState.Minimized || !ShowInTaskbar;

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            HideIncomingCallPopup();
        }
    }

    private bool TryShowMinimizedIncomingPopup(IncomingCallEventArgs callInfo)
    {
        if (WindowState != WindowState.Minimized)
        {
            return false;
        }

        if (_userSettings?.Settings.AutoAnswerEnabled == true)
        {
            return false;
        }

        try
        {
            var popup = EnsureIncomingCallPopup();
            popup.Present(callInfo, _sipService.ActiveCallId);
            return popup.IsVisible;
        }
        catch
        {
            HideIncomingCallPopup();
            return false;
        }
    }

    private IncomingCallPopupWindow EnsureIncomingCallPopup()
    {
        if (_incomingCallPopup is not null)
        {
            return _incomingCallPopup;
        }

        var popup = new IncomingCallPopupWindow();
        popup.AnswerRequested += (_, _) => _ = HandleIncomingPopupAnswerAsync();
        popup.DeclineRequested += (_, _) => _ = HandleIncomingPopupDeclineAsync();
        _incomingCallPopup = popup;
        return popup;
    }

    private bool IsPopupBoundToCurrentIncoming()
    {
        if (_sipService.CallState != CallState.Incoming)
        {
            return false;
        }

        var boundId = _incomingCallPopup?.BoundCallId;
        var activeId = _sipService.ActiveCallId;
        if (string.IsNullOrWhiteSpace(boundId) || string.IsNullOrWhiteSpace(activeId))
        {
            return false;
        }

        return string.Equals(
            SipCallIdHelper.Normalize(boundId),
            SipCallIdHelper.Normalize(activeId),
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleIncomingPopupAnswerAsync()
    {
        if (!IsPopupBoundToCurrentIncoming())
        {
            HideIncomingCallPopup();
            return;
        }

        try
        {
            await CallSessionView.AnswerIncomingAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "CallAnalog",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        HideIncomingCallPopup();
        App.TrayIcon.ShowMainWindow();
    }

    private async Task HandleIncomingPopupDeclineAsync()
    {
        if (!IsPopupBoundToCurrentIncoming())
        {
            HideIncomingCallPopup();
            return;
        }

        try
        {
            await _sipService.DeclineIncomingAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "CallAnalog",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        HideIncomingCallPopup();
    }

    private void HideIncomingCallPopup()
    {
        _incomingCallPopup?.Dismiss();
    }

    private void CloseIncomingCallPopup(bool force)
    {
        if (_incomingCallPopup is null)
        {
            return;
        }

        if (force)
        {
            _incomingCallPopup.ForceClose();
            _incomingCallPopup = null;
            return;
        }

        HideIncomingCallPopup();
    }

    /// <summary>True when the user may not see the in-app incoming UI (minimized, hidden, or unfocused).</summary>
    private bool ShouldShowIncomingToast(IncomingCallNotificationKind kind)
    {
        if (kind == IncomingCallNotificationKind.Incoming
            && _userSettings?.Settings.AutoAnswerEnabled == true)
        {
            return false;
        }

        return IsAppBackgrounded() || !IsActive;
    }

    private void UpdateActiveCallUi(CallState state)
    {
        if (_incomingWasRinging && state == CallState.Idle)
        {
            if (!_sipService.ConsumeMissedCallBadgeSuppression())
            {
                IncrementMissedCallBadge();
            }
        }

        _incomingWasRinging = state is CallState.Incoming or CallState.CallWaitingRinging;

        if (state is not CallState.Incoming and not CallState.Outgoing and not CallState.CallWaitingRinging)
        {
            _ringtone.Stop();
            _ringback.Stop();
        }

        if (state == CallState.Outgoing)
        {
            _ringback.Start(
                _userSettings.Settings.SpeakerDevice,
                _userSettings.Settings.SpeakerDeviceId);
        }
        else
        {
            _ringback.Stop();
        }

        if (state is not CallState.Incoming)
        {
            App.TrayIcon.DismissIncomingCallNotification();
            HideIncomingCallPopup();
        }

        if (state is not CallState.CallWaitingRinging)
        {
            App.TrayIcon.DismissCallWaitingNotification();
        }

        if (state is CallState.Incoming or CallState.Outgoing or CallState.CallWaitingRinging)
        {
            DismissActiveShellPanel();
        }

        CallSessionView.UpdateCallState(state);
        DialpadView.UpdateCallState(state);
        UpdateCallDurationDisplay(state);
        UpdateHeaderBarForCallState(state);
        UpdateTrayStatus(_currentStatus, state);
    }

    private void UpdateHeaderBarForCallState(CallState state)
    {
        if (state == CallState.OnHold)
        {
            ShellHeaderBar.Background = (Brush)FindResource("PhoneStatusRegisteringBgBrush");
        }
        else
        {
            ShellHeaderBar.Background = (Brush)FindResource("PhoneBgBrush");
        }
    }

    private void UpdateCallDurationDisplay(CallState? state = null)
    {
        state ??= _sipService.CallState;

        if (state is CallState.Idle or CallState.Incoming or CallState.Outgoing)
        {
            _callDurationTimer.Stop();
            CallDurationText.Text = string.Empty;
            CallDurationText.Visibility = Visibility.Collapsed;
            return;
        }

        if (state is CallState.InCall or CallState.OnHold or CallState.CallWaitingRinging
            && _sipService.ConnectedAt is not null)
        {
            var elapsed = state == CallState.OnHold
                ? _sipService.ActiveCallDuration
                : _sipService.ActiveCallDuration;
            var duration = CallSessionView.FormatDuration(elapsed);
            var remote = _sipService.RemoteParty;

            CallDurationText.Text = string.IsNullOrWhiteSpace(remote)
                ? duration
                : $"{remote} · {duration}";
            CallDurationText.Visibility = Visibility.Visible;

            if (!_callDurationTimer.IsEnabled)
            {
                _callDurationTimer.Start();
            }

            return;
        }

        _callDurationTimer.Stop();
        CallDurationText.Text = string.Empty;
        CallDurationText.Visibility = Visibility.Collapsed;
    }

    internal void FlashWindow()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        FlashWindowEx(new FLASHWINFO
        {
            cbSize = Convert.ToUInt32(Marshal.SizeOf<FLASHWINFO>()),
            hwnd = handle,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = 3,
            dwTimeout = 0
        });
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(FLASHWINFO pwfi);

    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e) =>
        await AttemptLoginAsync();

    private void CancelLoginButton_Click(object sender, RoutedEventArgs e)
    {
        _loginCts?.Cancel();
        StatusMessageHelper.Apply(InlineStatusText, "Sign-in cancelled.", StatusMessageKind.Neutral);
        ResetLoginUi();
    }

    private void ResetLoginUi()
    {
        _isBusy = false;
        LoginButton.IsEnabled = true;
        LoginButtonText.Visibility = Visibility.Visible;
        LoginSpinner.Visibility = Visibility.Collapsed;
        CancelLoginButton.Visibility = Visibility.Collapsed;
        LoginStepText.Visibility = Visibility.Collapsed;
        HideLaunchSplash();
    }

    private void SetLoginStep(string step)
    {
        LoginStepText.Text = step;
        LoginStepText.Visibility = Visibility.Visible;
    }

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AttemptLoginAsync();
        }
    }

    private void ExtensionBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private async Task AttemptLoginAsync(bool isAutoLogin = false)
    {
        if (_isBusy)
        {
            return;
        }

        var extension = ExtensionBox.Text.Trim();
        var password = PasswordBox.Password;
        var rememberMe = RememberMeCheckBox.IsChecked == true;

        _isBusy = true;
        LoginButton.IsEnabled = false;
        LoginButtonText.Visibility = Visibility.Collapsed;
        LoginSpinner.Visibility = Visibility.Visible;
        CancelLoginButton.Visibility = Visibility.Visible;
        if (isAutoLogin)
        {
            ShowLaunchSplash("Signing in automatically...");
        }

        SetLoginStep(isAutoLogin ? "Signing in..." : "Signing in...");
        StatusMessageHelper.Apply(
            InlineStatusText,
            isAutoLogin ? "Signing in automatically..." : "Signing in...",
            StatusMessageKind.Progress);

        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = new CancellationTokenSource();
        var loginToken = _loginCts.Token;

        try
        {
            _sipLog.BeginSection(isAutoLogin ? "AUTO LOGIN" : "LOGIN");
            _sipLog.Info(SipLogTag.Login, isAutoLogin
                ? $"Automatic sign-in started for extension {extension}."
                : $"Manual sign-in started for extension {extension}.");

            if (isAutoLogin && CanUseFastAutoLogin(extension))
            {
                _sipLog.Info(SipLogTag.Login, "Using saved carrier credentials — skipping API login.");
                await RegisterWithRetryAsync(
                    extension,
                    password,
                    rememberMe,
                    skipPreRegisterCleanup: false,
                    skipApiLogin: true,
                    loginToken);
                _sipLog.Info(SipLogTag.Login, "Sign-in completed successfully.");
                _sipLog.EndSection(isAutoLogin ? "AUTO LOGIN" : "LOGIN");
                return;
            }

            SetLoginStep("Signing in...");
            UpdateSplashStep("Signing in to your extension...");
            _sipLog.Info(SipLogTag.Login, "Calling extension login API...");
            var loginResult = await _loginService.LoginAsync(extension, password, loginToken);
            if (!loginResult.Success)
            {
                ShowLoginFailure(loginResult);
                _sipLog.EndSection(isAutoLogin ? "AUTO LOGIN" : "LOGIN");
                return;
            }

            _sipLog.Info(SipLogTag.Login, "Extension login API succeeded.");

            SetLoginStep("Resolving carrier...");
            UpdateSplashStep("Resolving SIP carrier...");
            StatusMessageHelper.Apply(InlineStatusText, "Resolving SIP carrier...", StatusMessageKind.Progress);

            var carrier = await _carrierProvisioning.ResolveForRegistrationAsync(
                loginResult.LoginDomainName,
                loginResult.LoginDomainPort);

            await _userSettings.SaveCarrierAsync(
                carrier.DomainName,
                carrier.Transport,
                carrier.DomainPort,
                carrier.DomainIp);
            _sipLog.Info(SipLogTag.Login, $"Carrier resolved from {carrier.Source}: {carrier.Display}");

            await RegisterWithRetryAsync(
                extension,
                password,
                rememberMe,
                skipPreRegisterCleanup: false,
                skipApiLogin: false,
                loginToken);

            _sipLog.Info(SipLogTag.Login, "Sign-in completed successfully.");
            _sipLog.EndSection(isAutoLogin ? "AUTO LOGIN" : "LOGIN");
        }
        catch (OperationCanceledException)
        {
            _sipLog.Info(SipLogTag.Login, "Sign-in cancelled by user.");
            _sipLog.EndSection(isAutoLogin ? "AUTO LOGIN" : "LOGIN");
            StatusMessageHelper.Apply(InlineStatusText, "Sign-in cancelled.", StatusMessageKind.Neutral);
        }
        catch (Exception ex)
        {
            await _sipService.UnregisterAsync();
            StatusMessageHelper.Apply(InlineStatusText, ex.Message, StatusMessageKind.Error);
            if (ex is TimeoutException)
            {
                var port = _userSettings.Settings.SipPort;
                var transport = _userSettings.Settings.DefaultTransport.ToUpperInvariant();
                _sipLog.CustomerError(
                    SipLogTag.Login,
                    "Registration timed out — the softphone could not register with your PBX in time.",
                    $"Check firewall allows outbound {transport} port {port}, verify extension/password, then sign in again.");
            }
            else
            {
                _sipLog.CustomerError(
                    SipLogTag.Login,
                    $"Sign-in failed: {ex.Message}",
                    "Verify extension and password, confirm network connectivity, then try again or contact CallAnalog support.");
            }

            _sipLog.EndSection(isAutoLogin ? "AUTO LOGIN" : "LOGIN");
            if (!rememberMe)
            {
                PasswordBox.Clear();
            }
            else if (string.IsNullOrEmpty(PasswordBox.Password))
            {
                PasswordBox.Password = password;
            }

            if (!isAutoLogin)
            {
                PasswordBox.Focus();
            }
        }
        finally
        {
            ResetLoginUi();
            HideLaunchSplash();
            _loginCts?.Dispose();
            _loginCts = null;
        }
    }

    private void ShowLoginFailure(LoginResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.Explanation)
            ? result.Message
            : result.Explanation;
        _sipLog.CustomerError(
            SipLogTag.Login,
            $"Extension login API failed: {detail} (code {result.Code})",
            "Verify extension and password. If the problem continues, contact your administrator or CallAnalog support.");
        StatusMessageHelper.Apply(
            InlineStatusText,
            $"{detail} (Code {result.Code})",
            StatusMessageKind.Error);
        if (RememberMeCheckBox.IsChecked != true)
        {
            PasswordBox.Clear();
        }

        PasswordBox.Focus();
    }

    private async Task RegisterWithRetryAsync(
        string extension,
        string password,
        bool rememberMe,
        bool skipPreRegisterCleanup,
        bool skipApiLogin,
        CancellationToken loginToken = default)
    {
        var provisionConfig = _userSettings.BuildProvisionConfig(extension, password);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            loginToken.ThrowIfCancellationRequested();
            try
            {
                SetLoginStep("Registering...");
                UpdateSplashStep("Registering on PBX...");
                StatusMessageHelper.Apply(InlineStatusText, "Registering on PBX...", StatusMessageKind.Progress);
                _sipLog.Info(
                    SipLogTag.Register,
                    attempt == 1
                        ? "Starting SIP REGISTER with PBX..."
                        : $"Retrying SIP REGISTER (attempt {attempt})...");

                if (!skipPreRegisterCleanup && attempt == 1)
                {
                    using var unregisterCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _pbxApiClient.TryUnregisterExtensionAsync(extension, _unregisterPath, unregisterCts.Token);
                    _sipLog.Info(SipLogTag.Register, "Requested stale registration cleanup via API.");
                }

                await _sipService.RegisterAsync(
                    provisionConfig,
                    loginToken,
                    registrationWaitTimeout: TimeSpan.FromSeconds(15));

                _sipLog.Info(SipLogTag.Register, $"Registered extension {extension} — entering dashboard.");
                EnterDashboard(extension);

                _userSettings.SaveRememberedLogin(extension, password, rememberMe);
                _userSettings.SaveCachedPublicIp();
                if (!rememberMe)
                {
                    PasswordBox.Clear();
                }

                return;
            }
            catch (Exception ex) when (attempt == 1 && IsRetriableLoginFailure(ex))
            {
                _sipLog.Warn(
                    SipLogTag.Register,
                    $"Registration attempt {attempt} timed out ({ex.Message}) — retrying once.");
                StatusMessageHelper.Apply(
                    InlineStatusText,
                    "Reconnecting to PBX...",
                    StatusMessageKind.Progress);
                await _sipService.UnregisterAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(400));

                if (skipApiLogin && ex is TimeoutException)
                {
                    _userSettings.RestoreCachedPublicIp(_sipLog);
                }
            }
        }

        throw new InvalidOperationException("SIP registration failed after two attempts.");
    }

    private static bool IsRetriableLoginFailure(Exception ex) => ex is TimeoutException;

    private void EnterDashboard(string extension)
    {
        _loggedInExtension = extension;

        LoginPanel.Visibility = Visibility.Collapsed;
        AppShellPanel.Visibility = Visibility.Visible;
        SignOutButton.Visibility = Visibility.Visible;
        TitleBarLogo.Visibility = Visibility.Visible;

        ExtensionLabelText.Text = $"Extension {extension}";
        CallSessionView.Initialize(_sipService, _ringtone, _userSettings, _networkQuality);
        DialpadView.Initialize(_dialService, _sipService);
        DialpadView.OutboundDialFailed += DialpadView_OutboundDialFailed;
        DashboardView.Initialize(_callHistoryService, _userSettings, extension);
        DashboardView.SetExtension(extension);
        DashboardView.SetDndOverlayVisible(_userSettings.Settings.DndEnabled);
        ContactsView.Initialize(_contactsService, extension, ShowContactFormAsync, (title, message) => ConfirmAsync(title, message));
        HistoryView.Initialize(_callHistoryService, extension);
        _ = PrimeContactLookupCacheAsync(extension);
        SettingsView.Initialize(
            _userSettings,
            () => _sipService.CallState is CallState.InCall or CallState.OnHold or CallState.Outgoing or CallState.CallWaitingRinging,
            _sipService,
            _appVersionCheckService);
        ApplyRegistrationStatusFromSip();
        App.TrayIcon.SetDndEnabled(_userSettings.Settings.DndEnabled);
        _ = NavigateToPageAsync(DashboardView, PageDashboard, animate: false);
        _ = DashboardView.RefreshAsync();
        HideLaunchSplash();
        ScheduleStartupVersionCheck();
    }

    /// <summary>
    /// Non-blocking version check after Online. Never delays REGISTER or calls.
    /// Soft banner only — no auto-download / forced quit.
    /// </summary>
    private void ScheduleStartupVersionCheck()
    {
        if (_startupVersionCheckStarted)
        {
            return;
        }

        _startupVersionCheckStarted = true;
        _ = RunStartupVersionCheckAsync();
    }

    private async Task RunStartupVersionCheckAsync()
    {
        try
        {
            await Task.Delay(750);
            var result = await _appVersionCheckService.CheckAsync();
            if (!result.UpdateAvailable)
            {
                _sipLog.Info($"[UPDATES] Startup check: up to date ({result.InstalledVersion} / latest {result.CurrentVersion}).");
                return;
            }

            var message = result.FormatStatusMessage(BuildInfo.FullBuildLabel);
            _sipLog.Info($"[UPDATES] Startup check: {message}");
            await Dispatcher.InvokeAsync(() => ShowUpdateAvailableBanner(message));
        }
        catch (Exception ex)
        {
            // Soft failure — never surface as a blocking error on the dashboard.
            _sipLog.Warn($"[UPDATES] Startup check skipped ({ex.Message}).");
        }
    }

    private void ShowUpdateAvailableBanner(string message)
    {
        _pendingUpdateBannerMessage = message;
        UpdateAvailableBannerText.Text = message;
        UpdateAvailableBanner.Visibility = Visibility.Visible;
    }

    private void UpdateAvailableBanner_Click(object sender, MouseButtonEventArgs e)
    {
        UpdateAvailableBanner.Visibility = Visibility.Collapsed;
        _ = NavigateToPageAsync(SettingsView, PageSettings);
        if (!string.IsNullOrWhiteSpace(_pendingUpdateBannerMessage))
        {
            // Settings status line mirrors the banner if the user opens Check for Updates later.
            SettingsView.FlashExternalStatus(_pendingUpdateBannerMessage, StatusMessageKind.Warning);
        }
    }

    private async Task PrimeContactLookupCacheAsync(string extension)
    {
        try
        {
            var wrapped = await _contactsService.GetContactsAsync(extension, 1);
            if (wrapped.Result.Items.Count > 0)
            {
                _contactLookup.UpdateCache(extension, wrapped.Result.Items);
            }
        }
        catch
        {
            // Offline cache may still be used for inbound caller ID.
        }
    }

    private async Task SignOutAsync()
    {
        if (_isBusy || AppShellPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        if (_sipService.CallState is CallState.Incoming
            or CallState.InCall
            or CallState.OnHold
            or CallState.Outgoing
            or CallState.CallWaitingRinging)
        {
            var isRinging = _sipService.CallState == CallState.Incoming;
            var confirmed = await ConfirmAsync(
                isRinging ? "Decline call and sign out?" : "End call and sign out?",
                isRinging
                    ? "The ringing call will be declined and you will be signed out."
                    : "Your active call will be disconnected and you will be signed out.",
                "Sign out",
                "Cancel");
            if (!confirmed)
            {
                return;
            }
        }

        _isBusy = true;
        try
        {
            _ringtone.Stop();
            if (_sipService.CallState == CallState.Incoming)
            {
                await _sipService.DeclineIncomingAsync();
            }
            else if (_sipService.CallState is not CallState.Idle)
            {
                await _sipService.HangupAllLegsForSignOutAsync();
            }

            if (_sipService.CallState is not CallState.Idle)
            {
                throw new InvalidOperationException("Could not disconnect the active call before sign-out.");
            }

            if (!string.IsNullOrWhiteSpace(_loggedInExtension))
            {
                await _pbxApiClient.TryUnregisterExtensionAsync(_loggedInExtension, _unregisterPath);
            }

            await _sipService.UnregisterAsync();

            var extension = _loggedInExtension;
            AppShellPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
            _startupVersionCheckStarted = false;
            UpdateAvailableBanner.Visibility = Visibility.Collapsed;
            _pendingUpdateBannerMessage = null;
            SignOutButton.Visibility = Visibility.Collapsed;
            TitleBarLogo.Visibility = Visibility.Collapsed;

            ExtensionBox.Text = extension;
            InlineStatusText.Text = string.Empty;
            _loggedInExtension = string.Empty;
            SetConnectionStatus(ConnectionStatus.Offline);
            UpdateTrayStatus(ConnectionStatus.Offline, CallState.Idle);
        }
        catch (Exception ex)
        {
            _sipLog.Error($"Sign out failed: {ex.Message}");
            StatusMessageHelper.Apply(InlineStatusText, ex.Message, StatusMessageKind.Error);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void SetConnectionStatus(ConnectionStatus status)
    {
        _currentStatus = status;
        StatusLabelText.Text = ConnectionStatusInfo.GetLabel(status);
        StatusDot.Fill = ConnectionStatusInfo.GetBrush(status);
        ConnectionStatusChip.Background = ConnectionStatusInfo.GetChipBackgroundBrush(status);
        ConnectionStatusChip.BorderBrush = ConnectionStatusInfo.GetChipBorderBrush(status);
        StatusLabelText.Foreground = ConnectionStatusInfo.GetChipForegroundBrush(status);
        DialpadView.SetRegistrationStatus(ConnectionStatusInfo.GetLabel(status), status);
        UpdateTrayStatus(status, _sipService.CallState);
    }

    private void UpdateTrayStatus(ConnectionStatus connectionStatus, CallState callState)
    {
        var trayStatus = callState switch
        {
            CallState.Incoming => "Ringing",
            CallState.InCall => "On call",
            CallState.OnHold => "On call (hold)",
            CallState.Outgoing => "Calling",
            _ => connectionStatus switch
            {
                ConnectionStatus.Online => "Online",
                ConnectionStatus.Reconnecting => "Reconnecting",
                ConnectionStatus.Registering => "Registering",
                ConnectionStatus.Disconnected => "Disconnected",
                _ => "Offline"
            }
        };

        App.TrayIcon.SetStatus(trayStatus, connectionStatus, callState);
    }

    private void ResetNavButtons()
    {
        NavDashboardButton.Style = (Style)FindResource("BottomNavButton");
        NavHistoryButton.Style = (Style)FindResource("BottomNavButton");
        NavContactsButton.Style = (Style)FindResource("BottomNavButton");
        NavSettingsButton.Style = (Style)FindResource("BottomNavButton");
        NavKeypadButton.Style = (Style)FindResource("BottomNavKeypadButton");
    }

    private void SetActiveNavButton(int pageIndex)
    {
        ResetNavButtons();

        if (pageIndex == PageDialpad)
        {
            NavKeypadButton.Style = (Style)FindResource("BottomNavKeypadButtonActive");
            return;
        }

        var activeButton = pageIndex switch
        {
            PageDashboard => NavDashboardButton,
            PageHistory => NavHistoryButton,
            PageContacts => NavContactsButton,
            PageSettings => NavSettingsButton,
            _ => null
        };

        if (activeButton is not null)
        {
            activeButton.Style = (Style)FindResource("BottomNavButtonActive");
        }
    }

    private async Task NavigateToPageAsync(FrameworkElement page, int pageIndex, bool animate = true)
    {
        if (_isNavigating)
        {
            return;
        }

        if (ReferenceEquals(_currentPage, page) && page.Visibility == Visibility.Visible)
        {
            SetActiveNavButton(pageIndex);
            return;
        }

        _isNavigating = true;
        try
        {
            var direction = _currentPageIndex < 0 || pageIndex == PageDialpad
                ? 0
                : Math.Sign(pageIndex - _currentPageIndex);

            if (animate && _currentPage is not null)
            {
                await PageTransitionHelper.SwitchAsync(_currentPage, page, direction);
            }
            else
            {
                PageTransitionHelper.ShowImmediate(page, _currentPage);
            }

            _currentPage = page;
            _currentPageIndex = pageIndex;
            SetActiveNavButton(pageIndex);
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private async void ShowDashboard()
    {
        await NavigateToPageAsync(DashboardView, PageDashboard);
        await DashboardView.RefreshAsync();
    }

    private async void ShowDialpad(string? number = null)
    {
        if (!string.IsNullOrWhiteSpace(number))
        {
            DialpadView.SetNumber(number);
        }

        await NavigateToPageAsync(DialpadView, PageDialpad);
        DialpadView.SetRegistrationStatus(ConnectionStatusInfo.GetLabel(_currentStatus), _currentStatus);

        if (!string.IsNullOrWhiteSpace(number))
        {
            DialpadView.SetNumber(number);
        }

        DialpadView.ActivateInput();
    }

    private async void ShowContacts()
    {
        await NavigateToPageAsync(ContactsView, PageContacts);
        await ContactsView.EnsureLoadedAsync();
    }

    private async void ShowHistory(CallHistoryFilter filter = CallHistoryFilter.All)
    {
        ClearMissedCallBadge();
        await NavigateToPageAsync(HistoryView, PageHistory);

        if (filter == CallHistoryFilter.All)
        {
            await HistoryView.EnsureLoadedAsync();
        }
        else
        {
            await HistoryView.NavigateWithFilterAsync(filter);
        }
    }

    private async void ShowSettings()
    {
        await NavigateToPageAsync(SettingsView, PageSettings);
        SettingsView.Initialize(
            _userSettings,
            () => _sipService.CallState is CallState.InCall or CallState.OnHold or CallState.Outgoing or CallState.CallWaitingRinging,
            _sipService,
            _appVersionCheckService);
    }

    private async void SettingsView_SettingsSaved(object? sender, SettingsSavedEventArgs e)
    {
        CallSessionView.RefreshRecordingAvailability(_userSettings.Settings.CallRecordingEnabled);
        if (e.RegistrationTimingChanged
            && _sipService.RegistrationState == SipRegistrationState.Registered)
        {
            await _sipService.RefreshRegistrationTimingAsync();
        }
    }

    private void SettingsView_SaveAllCompleted(object? sender, EventArgs e) =>
        ShowDashboard();

    private void CallSessionView_ComingSoonRequested(object? sender, string featureName) =>
        ShowComingSoon(featureName);

    private async void CallSessionView_BlindTransferRequested(object? sender, EventArgs e)
    {
        var request = await ShowShellPanelAsync<TransferRequest>(new TransferPanel());
        if (request is null || string.IsNullOrWhiteSpace(request.Target))
        {
            return;
        }

        try
        {
            await _sipService.BlindTransferAsync(request.Target);
        }
        catch (Exception ex)
        {
            CallSessionView.ShowStatusMessage(ex.Message);
            CallSessionView.UpdateCallState(_sipService.CallState);
        }
    }

    private void DialpadView_OutboundDialFailed(object? sender, string message)
    {
        CallSessionView.ShowStatusMessage(message);
        DialpadView.ShowDialFailure(message);
        ShowDialpad();
    }

    private void DashboardView_OpenDialpadRequested(object? sender, EventArgs e) =>
        ShowDialpad();

    private void DialpadView_BackRequested(object? sender, EventArgs e) =>
        ShowDashboard();

    private void ContactsView_DialRequested(object? sender, string number) =>
        ShowDialpad(number);

    private void ContactsView_MessageRequested(object? sender, string number) =>
        ShowComingSoon($"SMS to {number}");

    private void HistoryView_DialRequested(object? sender, string number) =>
        ShowDialpad(number);

    private void HistoryView_MessageRequested(object? sender, string number) =>
        ShowComingSoon($"SMS to {number}");

    private async void ShowComingSoon(string featureName)
    {
        var panel = new ComingSoonPanel(featureName);
        await ShowShellPanelAsync<bool>(panel);
    }

    internal async Task<ContactFormResult?> ShowContactFormAsync(string title, string name, string number)
    {
        var panel = new ContactFormPanel(title, name, number);
        return await ShowShellPanelAsync<ContactFormResult>(panel);
    }

    private void DashboardView_ComingSoonRequested(object? sender, string featureName) =>
        ShowComingSoon(featureName);

    private void DashboardView_VoicemailDialRequested(object? sender, string code) =>
        ShowDialpad(code);

    private void DashboardView_ViewHistoryFilterRequested(object? sender, CallHistoryFilter filter) =>
        ShowHistory(filter);

    private void DashboardView_DialRecentRequested(object? sender, string number) =>
        ShowDialpad(number);

    private void NavDashboardButton_Click(object sender, RoutedEventArgs e) =>
        ShowDashboard();

    private void NavHistoryButton_Click(object sender, RoutedEventArgs e) =>
        ShowHistory();

    private void NavContactsButton_Click(object sender, RoutedEventArgs e) =>
        ShowContacts();

    private void NavSettingsButton_Click(object sender, RoutedEventArgs e) =>
        ShowSettings();

    private void NavKeypadButton_Click(object sender, RoutedEventArgs e) =>
        ShowDialpad();

    internal void ShowDialpadFromTray() => ShowDialpad();

    internal async void ToggleDndFromTray()
    {
        if (_userSettings is null || AppShellPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        _userSettings.Settings.DndEnabled = !_userSettings.Settings.DndEnabled;
        await _userSettings.SaveDashboardTogglesAsync(
            _userSettings.Settings.DndEnabled,
            _userSettings.Settings.AutoAnswerEnabled);
        DashboardView.SetDndOverlayVisible(_userSettings.Settings.DndEnabled);
        App.TrayIcon.SetDndEnabled(_userSettings.Settings.DndEnabled);
        await DashboardView.RefreshAsync();
    }

    internal async Task ExitFromTrayAsync()
    {
        if (AppShellPanel.Visibility == Visibility.Visible && !_forceClosing)
        {
            var confirmed = await ConfirmAsync(
                "Exit CallAnalog?",
                "You will be signed out and the application will close.",
                "Exit",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            _forceClosing = true;
            await SignOutAsync();
        }

        ForceExit();
    }
}
