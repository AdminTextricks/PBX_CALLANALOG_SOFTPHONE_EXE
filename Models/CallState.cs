namespace CallAnalog.Softphone.Models;

public enum CallState
{
    Idle,
    Outgoing,
    Incoming,
    InCall,
    OnHold,
    CallWaitingRinging
}

public sealed class IncomingCallEventArgs : EventArgs
{
    public IncomingCallEventArgs(string callerNumber, string? callerName = null, bool isQueueCall = false)
    {
        CallerNumber = callerNumber;
        CallerName = callerName;
        IsQueueCall = isQueueCall;
    }

    public string CallerNumber { get; }
    public string? CallerName { get; }
    public bool IsQueueCall { get; }
}
