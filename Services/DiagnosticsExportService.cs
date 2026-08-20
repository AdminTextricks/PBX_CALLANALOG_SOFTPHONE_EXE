using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

public sealed class DiagnosticsExportService
{
    private readonly UserSettingsService _settings;
    private readonly SipLogService _log;

    public DiagnosticsExportService(UserSettingsService settings, SipLogService log)
    {
        _settings = settings;
        _log = log;
    }

    public string ExportToZip()
    {
        var exportsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog",
            "exports");
        Directory.CreateDirectory(exportsDir);

        var zipPath = Path.Combine(exportsDir, $"diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddTextEntry(archive, "version.txt", BuildVersionText());
            AddTextEntry(archive, "settings-redacted.json", RedactSettingsJson());
            AddRecentLogEntries(archive, "sip.log");
        }

        _log.Info(SipLogTag.Diagnostics, $"Diagnostics exported to {zipPath}");
        return zipPath;
    }

    private static string BuildVersionText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Version: {BuildInfo.Version}");
        builder.AppendLine($"Build date: {BuildInfo.BuildDate}");
        builder.AppendLine($"Display: {BuildInfo.FullBuildLabel}");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($"Runtime: {Environment.Version}");
        builder.AppendLine($"Machine: {Environment.MachineName}");
        return builder.ToString();
    }

    private string RedactSettingsJson()
    {
        var settings = _settings.Settings;
        var redacted = new
        {
            settings.CompanyName,
            settings.CarrierHost,
            HasCarrierConnectHost = !string.IsNullOrWhiteSpace(settings.CarrierConnectHost),
            settings.DefaultTransport,
            settings.SipPort,
            settings.RegistrationExpirySeconds,
            settings.KeepAliveSeconds,
            settings.StartWithWindows,
            settings.MicrophoneDevice,
            settings.SpeakerDevice,
            settings.RingtoneDevice,
            settings.InputVolume,
            settings.OutputVolume,
            settings.EnabledCodecs,
            settings.CallRecordingEnabled,
            settings.CallRecordingFormat,
            HasRecordingDirectory = !string.IsNullOrWhiteSpace(settings.CallRecordingDirectory),
            settings.SendCrashReport,
            settings.DndEnabled,
            settings.AutoAnswerEnabled,
            settings.VoicemailDialCode,
            settings.AgentQueueModeEnabled,
            RememberMe = settings.RememberMe,
            HasSavedExtension = !string.IsNullOrWhiteSpace(settings.Extension)
        };

        return JsonSerializer.Serialize(redacted, new JsonSerializerOptions { WriteIndented = true });
    }

    private void AddRecentLogEntries(ZipArchive archive, string entryName)
    {
        var logPath = _log.LogFilePath;
        if (!File.Exists(logPath))
        {
            AddTextEntry(archive, entryName, "(no sip.log found — open Settings → Open SIP Log after using the app)");
            return;
        }

        var lines = File.ReadAllLines(logPath);
        var tail = lines.Length <= 1000 ? lines : lines[^1000..];
        var content = string.Join(Environment.NewLine, tail.Select(SipLogRedaction.Redact));
        AddTextEntry(archive, entryName, content);
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
