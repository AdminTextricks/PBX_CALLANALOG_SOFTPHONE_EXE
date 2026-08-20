using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

internal static class NetworkLossHelper
{
    internal static bool ShouldHangupOnNetworkLoss(CallState state) =>
        state is CallState.InCall
            or CallState.OnHold
            or CallState.Outgoing
            or CallState.Incoming;

    internal static bool ShouldProcessNetworkLoss(SipRegistrationState registrationState, bool hasConfig) =>
        hasConfig && registrationState is not SipRegistrationState.Unregistered;
}
