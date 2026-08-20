using System.Windows;
using CallAnalog.Softphone.Models;
using CommunityToolkit.WinUI.Notifications;

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
    private const string IncomingToastTag = "callanalog-incoming";
    private const string WaitingToastTag = "callanalog-waiting";
    private const string ToastGroup = "callanalog-calls";

    private bool _initialized;
    private bool _disposed;

    public event EventHandler<IncomingCallNotificationActionEventArgs>? ActionRequested;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
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
        var tag = kind == IncomingCallNotificationKind.CallWaiting ? WaitingToastTag : IncomingToastTag;
        var kindArg = kind == IncomingCallNotificationKind.CallWaiting ? "waiting" : "incoming";

        new ToastContentBuilder()
            .AddArgument("action", "open")
            .AddArgument("kind", kindArg)
            .AddText(title)
            .AddText($"Call from {caller}")
            .AddButton(new ToastButton("Accept", $"action=accept;kind={kindArg}"))
            .AddButton(new ToastButton("Decline", $"action=decline;kind={kindArg}"))
            .SetToastScenario(ToastScenario.IncomingCall)
            .Show(toast =>
            {
                toast.Tag = tag;
                toast.Group = ToastGroup;
            });

        App.SipLog.Info(
            SipLogTag.Toast,
            $"Showing {title.ToLowerInvariant()} toast for {caller} (Accept / Decline available).");
    }

    public void DismissIncomingCallNotification()
    {
        if (!_initialized)
        {
            return;
        }

        ToastNotificationManagerCompat.History.Remove(IncomingToastTag, ToastGroup);
    }

    public void DismissCallWaitingNotification()
    {
        if (!_initialized)
        {
            return;
        }

        ToastNotificationManagerCompat.History.Remove(WaitingToastTag, ToastGroup);
    }

    public void DismissAllCallNotifications()
    {
        DismissIncomingCallNotification();
        DismissCallWaitingNotification();
    }

    public static bool TryParseToastActivation(
        string? argument,
        out IncomingCallNotificationAction action,
        out IncomingCallNotificationKind kind)
    {
        action = IncomingCallNotificationAction.Open;
        kind = IncomingCallNotificationKind.Incoming;

        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var args = ToastArguments.Parse(argument);
        if (!args.TryGetValue("action", out var actionValue))
        {
            return false;
        }

        action = actionValue switch
        {
            "accept" => IncomingCallNotificationAction.Accept,
            "decline" => IncomingCallNotificationAction.Decline,
            _ => IncomingCallNotificationAction.Open
        };

        if (args.TryGetValue("kind", out var kindValue)
            && string.Equals(kindValue, "waiting", StringComparison.OrdinalIgnoreCase))
        {
            kind = IncomingCallNotificationKind.CallWaiting;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
        _disposed = true;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        if (!TryParseToastActivation(e.Argument, out var action, out var kind))
        {
            return;
        }

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.Dispatcher.BeginInvoke(() =>
        {
            App.SipLog.Info(
                SipLogTag.Toast,
                $"Toast action: {action} ({kind})");
            ActionRequested?.Invoke(this, new IncomingCallNotificationActionEventArgs(action, kind));
        });
    }

    private static string FormatCaller(IncomingCallEventArgs callInfo) =>
        string.IsNullOrWhiteSpace(callInfo.CallerName)
            ? callInfo.CallerNumber
            : callInfo.CallerName;
}
