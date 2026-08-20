namespace CallAnalog.Softphone.Models;

public enum VoiceQualityProfile
{
    LowLatency = 0,
    Balanced = 1,
    StableWifi = 2
}

public enum VoiceEchoControl
{
    Off = 0,
    On = 1,
    Strong = 2
}

public enum VoiceNoiseReduction
{
    Off = 0,
    Low = 1,
    High = 2
}

public sealed class CallMediaQualitySnapshot
{
    public CallMediaQualitySnapshot(
        int bars,
        string label,
        double? packetLossPct,
        double? jitterMs,
        int framesReceived)
    {
        Bars = bars;
        Label = label;
        PacketLossPct = packetLossPct;
        JitterMs = jitterMs;
        FramesReceived = framesReceived;
    }

    public int Bars { get; }
    public string Label { get; }
    public double? PacketLossPct { get; }
    public double? JitterMs { get; }
    public int FramesReceived { get; }
}
