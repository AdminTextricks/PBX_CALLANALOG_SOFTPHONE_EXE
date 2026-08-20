namespace CallAnalog.Softphone.Models;

public sealed class NetworkQualitySnapshot
{
    public NetworkQualitySnapshot(
        int bars,
        string label,
        long? latencyMs,
        bool isRegistered,
        bool? registrationOk = null,
        long? optionsRttMs = null)
    {
        Bars = Math.Clamp(bars, 0, 4);
        Label = label;
        LatencyMs = latencyMs;
        IsRegistered = isRegistered;
        RegistrationOk = registrationOk;
        OptionsRttMs = optionsRttMs;
    }

    public int Bars { get; }

    public string Label { get; }

    public long? LatencyMs { get; }

    public bool IsRegistered { get; }

    public bool? RegistrationOk { get; }

    public long? OptionsRttMs { get; }
}
