namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Simple packet-loss concealment: when the playback buffer is starving after the first frame,
/// briefly repeat the last good PCM frame.
/// </summary>
internal static class PlaybackConcealmentHelper
{
    internal static bool ShouldRepeatLastFrame(int receivedFrameCount, int bufferedBytes, int frameBytes) =>
        receivedFrameCount > 0
        && frameBytes > 0
        && bufferedBytes < frameBytes / 2;
}
