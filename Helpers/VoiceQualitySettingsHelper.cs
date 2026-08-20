using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

internal static class VoiceQualitySettingsHelper
{
    public static VoiceQualityProfile ParseProfile(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "lowlatency" or "low latency" or "low_latency" => VoiceQualityProfile.LowLatency,
            "stablewifi" or "stable wifi" or "stable_wifi" or "stable" => VoiceQualityProfile.StableWifi,
            _ => VoiceQualityProfile.Balanced
        };

    public static VoiceEchoControl ParseEcho(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "off" => VoiceEchoControl.Off,
            "strong" => VoiceEchoControl.Strong,
            _ => VoiceEchoControl.On
        };

    public static VoiceNoiseReduction ParseNoise(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "off" => VoiceNoiseReduction.Off,
            "high" => VoiceNoiseReduction.High,
            _ => VoiceNoiseReduction.Low
        };

    public static string ProfileToStorage(VoiceQualityProfile profile) =>
        profile switch
        {
            VoiceQualityProfile.LowLatency => "LowLatency",
            VoiceQualityProfile.StableWifi => "StableWifi",
            _ => "Balanced"
        };

    public static string EchoToStorage(VoiceEchoControl echo) =>
        echo switch
        {
            VoiceEchoControl.Off => "Off",
            VoiceEchoControl.Strong => "Strong",
            _ => "On"
        };

    public static string NoiseToStorage(VoiceNoiseReduction noise) =>
        noise switch
        {
            VoiceNoiseReduction.Off => "Off",
            VoiceNoiseReduction.High => "High",
            _ => "Low"
        };
}
