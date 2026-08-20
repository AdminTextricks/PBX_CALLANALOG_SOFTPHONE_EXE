using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

internal enum CallStateRecoveryAction
{
    None,
    ResetCallState,
    ClearWaitingCallState,
    PromoteToInCall
}

internal readonly record struct CallStateConsistencyInput(
    CallState CallState,
    bool HasPendingIncomingRequest,
    bool HasOutboundCallCompletion,
    bool IsPrimaryCallActive,
    bool HasWaitingIncomingRequest,
    bool HasEstablishedPrimaryCall);

internal static class CallStateConsistencyHelper
{
    internal static CallStateRecoveryAction Evaluate(CallStateConsistencyInput input)
    {
        switch (input.CallState)
        {
            case CallState.Incoming:
                if (!input.HasPendingIncomingRequest)
                {
                    return CallStateRecoveryAction.ResetCallState;
                }

                break;

            case CallState.Outgoing:
                if (input.IsPrimaryCallActive && !input.HasOutboundCallCompletion)
                {
                    return CallStateRecoveryAction.PromoteToInCall;
                }

                if (!input.HasOutboundCallCompletion && !input.IsPrimaryCallActive)
                {
                    return CallStateRecoveryAction.ResetCallState;
                }

                break;

            case CallState.InCall:
            case CallState.OnHold:
                if (!input.IsPrimaryCallActive && !input.HasEstablishedPrimaryCall)
                {
                    return CallStateRecoveryAction.ResetCallState;
                }

                break;

            case CallState.CallWaitingRinging:
                if (!input.IsPrimaryCallActive && !input.HasEstablishedPrimaryCall)
                {
                    return CallStateRecoveryAction.ResetCallState;
                }

                if (!input.HasWaitingIncomingRequest)
                {
                    return CallStateRecoveryAction.ClearWaitingCallState;
                }

                break;
        }

        return CallStateRecoveryAction.None;
    }
}
