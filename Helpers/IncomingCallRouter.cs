using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Helpers;

internal enum IncomingCallRouteAction
{
    AcceptPrimary,
    AcceptWaiting,
    RejectBusy,
    RejectDnd,
    Forward,
    IgnoreRetransmitted,
    RejectSecondWaiting
}

internal readonly record struct IncomingCallRouteInput(
    CallState CallState,
    bool DndEnabled,
    string? CallForwardTarget,
    bool IsPrimaryCallActive,
    bool HasWaitingCall,
    string? IncomingCallId,
    string? ActiveCallId,
    string? PendingCallId,
    string? WaitingCallId);

internal static class IncomingCallRouter
{
    internal static IncomingCallRouteAction Evaluate(IncomingCallRouteInput input)
    {
        if (SipIncomingCallHelper.IsEligibleForCallWaiting(input.CallState, input.IsPrimaryCallActive))
        {
            if (input.HasWaitingCall)
            {
                if (SipCallIdHelper.IsRetransmittedInvite(input.IncomingCallId, input.WaitingCallId))
                {
                    return IncomingCallRouteAction.IgnoreRetransmitted;
                }

                return IncomingCallRouteAction.RejectSecondWaiting;
            }

            return IncomingCallRouteAction.AcceptWaiting;
        }

        if (input.CallState == CallState.Incoming
            && (SipCallIdHelper.IsRetransmittedInvite(input.IncomingCallId, input.ActiveCallId)
                || (!string.IsNullOrWhiteSpace(input.PendingCallId)
                    && SipCallIdHelper.IsRetransmittedInvite(input.IncomingCallId, input.PendingCallId))))
        {
            return IncomingCallRouteAction.IgnoreRetransmitted;
        }

        if (input.CallState != CallState.Idle)
        {
            return IncomingCallRouteAction.RejectBusy;
        }

        if (input.DndEnabled)
        {
            return IncomingCallRouteAction.RejectDnd;
        }

        if (!string.IsNullOrWhiteSpace(input.CallForwardTarget))
        {
            return IncomingCallRouteAction.Forward;
        }

        return IncomingCallRouteAction.AcceptPrimary;
    }
}
