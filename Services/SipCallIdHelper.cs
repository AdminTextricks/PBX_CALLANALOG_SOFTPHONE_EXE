using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

public static class SipCallIdHelper
{
    /// <summary>
    /// Returns the SIP Call-ID token without @host:port (e.g. for call notes API).
    /// </summary>
    public static string? Normalize(string? callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return null;
        }

        var trimmed = callId.Trim();
        var at = trimmed.IndexOf('@');
        return at > 0 ? trimmed[..at] : trimmed;
    }

    /// <summary>
    /// True when BYE/hangup signaling belongs to a prior session (e.g. warm-transfer leg after complete).
    /// </summary>
    public static bool IsStaleCallSignaling(
        string? signalingCallId,
        string? activeCallId,
        string? pendingCallId,
        CallState callState)
    {
        if (callState is CallState.Outgoing)
        {
            return false;
        }

        var signaling = Normalize(signalingCallId);
        if (string.IsNullOrWhiteSpace(signaling))
        {
            return callState == CallState.Idle;
        }

        var active = Normalize(activeCallId);
        if (!string.IsNullOrWhiteSpace(active))
        {
            return !string.Equals(signaling, active, StringComparison.Ordinal);
        }

        var pending = Normalize(pendingCallId);
        if (!string.IsNullOrWhiteSpace(pending))
        {
            return !string.Equals(signaling, pending, StringComparison.Ordinal);
        }

        return callState == CallState.Idle;
    }

    /// <summary>
    /// True when a new INVITE is a retransmission of an invite already being handled.
    /// </summary>
    public static bool IsRetransmittedInvite(string? incomingCallId, string? trackedCallId) =>
        !string.IsNullOrWhiteSpace(incomingCallId)
        && !string.IsNullOrWhiteSpace(trackedCallId)
        && string.Equals(Normalize(incomingCallId), Normalize(trackedCallId), StringComparison.Ordinal);
}
