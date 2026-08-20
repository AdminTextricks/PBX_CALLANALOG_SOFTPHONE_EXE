using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

internal static class NetworkQualityHelper
{
    internal static NetworkQualitySnapshot BuildSnapshot(
        long? optionsRttMs,
        SipRegistrationState registrationState,
        bool isRegistered)
    {
        var registrationOk = registrationState == SipRegistrationState.Registered;

        if (registrationState is SipRegistrationState.Unregistered or SipRegistrationState.Failed)
        {
            return new NetworkQualitySnapshot(0, "Offline", optionsRttMs, false, registrationOk, optionsRttMs);
        }

        if (registrationState is SipRegistrationState.Registering or SipRegistrationState.Reconnecting)
        {
            return new NetworkQualitySnapshot(
                1,
                registrationState == SipRegistrationState.Reconnecting ? "Reconnecting" : "Registering",
                optionsRttMs,
                isRegistered,
                registrationOk,
                optionsRttMs);
        }

        if (optionsRttMs is null)
        {
            return new NetworkQualitySnapshot(
                registrationOk ? 1 : 0,
                registrationOk ? "OPTIONS pending" : "Not registered",
                null,
                isRegistered,
                registrationOk,
                null);
        }

        return optionsRttMs.Value switch
        {
            < 100 => new NetworkQualitySnapshot(4, "Excellent reachability", optionsRttMs, true, true, optionsRttMs),
            < 200 => new NetworkQualitySnapshot(3, "Good reachability", optionsRttMs, true, true, optionsRttMs),
            < 400 => new NetworkQualitySnapshot(2, "Fair reachability", optionsRttMs, true, true, optionsRttMs),
            < 800 => new NetworkQualitySnapshot(1, "Weak reachability", optionsRttMs, true, true, optionsRttMs),
            _ => new NetworkQualitySnapshot(1, "Poor reachability", optionsRttMs, true, true, optionsRttMs)
        };
    }
}
