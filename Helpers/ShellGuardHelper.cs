using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

internal static class ShellGuardHelper
{
    internal static bool SignOutRequiresConfirmation(CallState callState) =>
        callState is CallState.Incoming
            or CallState.InCall
            or CallState.OnHold
            or CallState.Outgoing
            or CallState.CallWaitingRinging;

    internal static bool CanGlobalHotkeyAnswer(CallState callState, bool appShellVisible) =>
        appShellVisible
        && callState is CallState.Incoming or CallState.CallWaitingRinging;

    internal static bool ShouldShowIncomingToast(
        bool appBackgrounded,
        CallState callState,
        bool autoAnswerEnabled = false) =>
        appBackgrounded
        && callState is CallState.Incoming or CallState.CallWaitingRinging
        && !(autoAnswerEnabled && callState == CallState.Incoming);

    internal static bool ShouldDismissCallNotifications(CallState callState) =>
        callState == CallState.Idle;
}
