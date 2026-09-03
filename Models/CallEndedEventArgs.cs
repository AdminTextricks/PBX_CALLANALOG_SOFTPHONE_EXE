namespace CallAnalog.Softphone.Models;

public sealed class CallEndedEventArgs : EventArgs
{
    public CallEndedEventArgs(
        string? remoteParty,
        bool isOutbound,
        string? sipCallId,
        bool wasConnected,
        int durationSeconds = 0)
    {
        RemoteParty = remoteParty;
        IsOutbound = isOutbound;
        SipCallId = sipCallId;
        WasConnected = wasConnected;
        DurationSeconds = durationSeconds;
    }

    public string? RemoteParty { get; }
    public bool IsOutbound { get; }
    public string? SipCallId { get; }
    public bool WasConnected { get; }
    public int DurationSeconds { get; }
}
