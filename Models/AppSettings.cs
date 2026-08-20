namespace CallAnalog.Softphone.Models;

public sealed class AppSettings
{
    public bool RememberMe { get; set; }
    public string Extension { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string CompanyName { get; set; } = "CallAnalog";
    public string CarrierHost { get; set; } = "user.callanalog.com";
    public string? CarrierConnectHost { get; set; }
    public string DefaultTransport { get; set; } = "tcp";
    public int SipPort { get; set; } = 5065;
    public int RegistrationExpirySeconds { get; set; } = 3600;
    public int KeepAliveSeconds { get; set; } = 15;

    public bool StartWithWindows { get; set; }
    public string? MicrophoneDevice { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public string? SpeakerDevice { get; set; }
    public string? SpeakerDeviceId { get; set; }
    public string? RingtoneDevice { get; set; }
    public string? RingtoneDeviceId { get; set; }
    public double InputVolume { get; set; } = 1.0;
    public double OutputVolume { get; set; } = 1.0;
    public string? HoldMusicPath { get; set; }
    public string? RingtonePath { get; set; }
    public List<string> EnabledCodecs { get; set; } = ["PCMU", "PCMA"];
    public bool CallRecordingEnabled { get; set; }
    public string CallRecordingFormat { get; set; } = "wav";
    public string? CallRecordingDirectory { get; set; }
    public bool SendCrashReport { get; set; }
    public string CrashReportEmail { get; set; } = "help@callanalog.com";

    public bool DndEnabled { get; set; }
    public bool AutoAnswerEnabled { get; set; }

    public bool DarkModeEnabled { get; set; }
    public bool FollowSystemTheme { get; set; } = true;

    /// <summary>LowLatency | Balanced | StableWifi</summary>
    public string VoiceQualityProfile { get; set; } = "Balanced";
    /// <summary>Off | On | Strong</summary>
    public string VoiceEchoControl { get; set; } = "On";
    /// <summary>Off | Low | High</summary>
    public string VoiceNoiseReduction { get; set; } = "Low";
    public bool VoiceAutoGainEnabled { get; set; } = true;
    /// <summary>Prefer Opus when the PBX offers it (falls back to G.711).</summary>
    public bool VoicePreferOpus { get; set; } = true;

    public string ParkExtension { get; set; } = "*70";
    public string VoicemailDialCode { get; set; } = "*97";
    public string? ConferenceExtension { get; set; }
    public string? CallForwardNumber { get; set; }
    public bool AgentQueueModeEnabled { get; set; }

    public string? CachedPublicIp { get; set; }
    public DateTimeOffset? CachedPublicIpUtc { get; set; }
}
