namespace CallAnalog.Softphone.Helpers;

internal static class UiStateHelper
{
    internal static bool ShouldShowWrapUp(string? callId, IReadOnlySet<string> dismissedCallIds) =>
        !string.IsNullOrWhiteSpace(callId) && !dismissedCallIds.Contains(callId);

    internal static bool ShouldIncrementMissedBadge(bool wasRinging, bool nowIdle, bool suppressBadge) =>
        wasRinging && nowIdle && !suppressBadge;

    internal static string FormatMissedBadgeCount(int count) =>
        count > 99 ? "99+" : count.ToString();
}
