using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

public enum IncomingCallNotificationKind
{
    Incoming,
    CallWaiting
}

public enum IncomingCallNotificationAction
{
    Open,
    Accept,
    Decline
}

public sealed class IncomingCallNotificationActionEventArgs : EventArgs
{
    public IncomingCallNotificationActionEventArgs(
        IncomingCallNotificationAction action,
        IncomingCallNotificationKind kind)
    {
        Action = action;
        Kind = kind;
    }

    public IncomingCallNotificationAction Action { get; }
    public IncomingCallNotificationKind Kind { get; }
}

public sealed class IncomingCallToastService : IDisposable
{
    private bool _initialized;
    private bool _disposed;
    private bool _balloonVisible;
    private IncomingCallNotificationKind _activeKind = IncomingCallNotificationKind.Incoming;

    public event EventHandler<IncomingCallNotificationActionEventArgs>? ActionRequested;

    public event Action<string, string>? BalloonRequested;

    public event Action? BalloonDismissRequested;

    public void Initialize()
    {
        _initialized = true;
    }

    public void ShowIncomingCall(IncomingCallEventArgs callInfo, IncomingCallNotificationKind kind)
    {
        EnsureInitialized();

        var caller = FormatCaller(callInfo);
        var title = kind switch
        {
            IncomingCallNotificationKind.CallWaiting => "Call Waiting",
            IncomingCallNotificationKind.Incoming when callInfo.IsQueueCall => "Queue Call",
            _ => "Incoming Call"
        };

        _activeKind = kind;
        _balloonVisible = true;
        BalloonRequested?.Invoke(title, $"Call from {caller}");

        App.SipLog.Info(
            SipLogTag.Toast,
            $"Showing {title.ToLowerInvariant()} tray notification for {caller}.");
    }

    public void DismissIncomingCallNotification() => DismissIf(IncomingCallNotificationKind.Incoming);

    public void DismissCallWaitingNotification() => DismissIf(IncomingCallNotificationKind.CallWaiting);

    public void DismissAllCallNotifications()
    {
        if (!_balloonVisible)
        {
            return;
        }

        _balloonVisible = false;
        BalloonDismissRequested?.Invoke();
    }

    public void NotifyBalloonClicked()
    {
        if (!_balloonVisible)
        {
            return;
        }

        var kind = _activeKind;
        _balloonVisible = false;
        App.SipLog.Info(SipLogTag.Toast, $"Tray notification opened ({kind}).");
        ActionRequested?.Invoke(
            this,
            new IncomingCallNotificationActionEventArgs(IncomingCallNotificationAction.Open, kind));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _balloonVisible = false;
        _disposed = true;
    }

    private void DismissIf(IncomingCallNotificationKind kind)
    {
        if (!_balloonVisible || _activeKind != kind)
        {
            return;
        }

        _balloonVisible = false;
        BalloonDismissRequested?.Invoke();
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }

    private static string FormatCaller(IncomingCallEventArgs callInfo) =>
        string.IsNullOrWhiteSpace(callInfo.CallerName)
            ? callInfo.CallerNumber
            : callInfo.CallerName;
}
