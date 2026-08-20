namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Pure decisions for mid-call media recovery (unit-testable). SIP re-INVITE is gated by a feature flag.
/// </summary>
public static class MidCallMediaRecoveryHelper
{
    public static readonly TimeSpan DefaultNoRtpThreshold = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(15);

    public static bool ShouldAttemptLocalRecovery(
        bool featureEnabled,
        DateTimeOffset? lastRtpUtc,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastRecoveryUtc,
        TimeSpan? noRtpThreshold = null,
        TimeSpan? cooldown = null)
    {
        if (!featureEnabled)
        {
            return false;
        }

        if (lastRtpUtc is null)
        {
            // Never received RTP — wait a bit after connect before declaring dead media.
            return false;
        }

        var threshold = noRtpThreshold ?? DefaultNoRtpThreshold;
        if (nowUtc - lastRtpUtc.Value < threshold)
        {
            return false;
        }

        var cool = cooldown ?? DefaultCooldown;
        if (lastRecoveryUtc is not null && nowUtc - lastRecoveryUtc.Value < cool)
        {
            return false;
        }

        return true;
    }

    public static bool ShouldAttemptSipReinvite(
        bool featureEnabled,
        bool sipReinviteEnabled,
        int localRecoveryAttemptsSinceRtp,
        int minLocalAttemptsBeforeReinvite = 1)
    {
        if (!featureEnabled || !sipReinviteEnabled)
        {
            return false;
        }

        return localRecoveryAttemptsSinceRtp >= minLocalAttemptsBeforeReinvite;
    }
}
