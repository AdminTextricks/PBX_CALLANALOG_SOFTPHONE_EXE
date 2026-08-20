using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Helpers;

internal static class SipIncomingCallHelper
{
    internal static bool IsEligibleForCallWaiting(CallState callState, bool isPrimaryCallActive) =>
        callState is CallState.InCall
            or CallState.OnHold
            or CallState.CallWaitingRinging
        || (isPrimaryCallActive && callState is CallState.Outgoing);

    /// <summary>
    /// SIPSorcery's SIPUserAgent does not raise OnIncomingCall while a dialog is active.
    /// Concurrent INVITEs must be handled from the SIP transport instead.
    /// </summary>
    internal static bool ShouldHandleConcurrentInviteAtTransport(
        CallState callState,
        bool isPrimaryCallActive,
        string? incomingCallId,
        string? activeCallId,
        string? pendingIncomingCallId)
    {
        if (callState == CallState.Idle && !isPrimaryCallActive)
        {
            return false;
        }

        if (callState is CallState.Incoming or CallState.Outgoing)
        {
            if (SipCallIdHelper.IsRetransmittedInvite(incomingCallId, activeCallId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(pendingIncomingCallId)
                && SipCallIdHelper.IsRetransmittedInvite(incomingCallId, pendingIncomingCallId))
            {
                return false;
            }
        }

        if (callState is CallState.InCall or CallState.OnHold)
        {
            if (SipCallIdHelper.IsRetransmittedInvite(incomingCallId, activeCallId))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsQueueCall(IEnumerable<string>? unknownHeaders)
    {
        if (unknownHeaders is null)
        {
            return false;
        }

        foreach (var header in unknownHeaders)
        {
            if (header.Contains("queue", StringComparison.OrdinalIgnoreCase)
                || header.Contains("acd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
