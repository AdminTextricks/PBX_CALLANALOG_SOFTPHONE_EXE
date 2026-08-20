namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Detects a playback sink that has stopped draining. RTP keeps arriving and the buffer stays pinned
/// at capacity, so frames are discarded on overflow and the agent hears nothing.
/// </summary>
public static class PlaybackStallHelper
{
    /// <summary>Fraction of buffer capacity treated as "not draining".</summary>
    public const double SaturationRatio = 0.95;

    /// <summary>Saturated observations required before restarting the sink.</summary>
    public const int ChecksBeforeRecovery = 2;

    public static bool IsBufferSaturated(int bufferedBytes, int capacityBytes) =>
        capacityBytes > 0 && bufferedBytes >= capacityBytes * SaturationRatio;

    public static int NextSaturatedStreak(int currentStreak, bool saturated) =>
        saturated ? Math.Max(0, currentStreak) + 1 : 0;

    public static bool ShouldRecoverPlayback(int saturatedStreak) =>
        saturatedStreak >= ChecksBeforeRecovery;
}
