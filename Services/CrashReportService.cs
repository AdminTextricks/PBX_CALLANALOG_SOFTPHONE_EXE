using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone.Services;

public sealed class CrashReportService
{
    private readonly UserSettingsService _settingsService;
    private readonly SipLogService _log;
    private readonly string _crashDirectory;
    private readonly string? _smtpHost;
    private readonly int _smtpPort;
    private readonly string? _smtpUser;
    private readonly string? _smtpPassword;
    private readonly bool _smtpUseSsl;
    private readonly string _smtpFrom;

    public CrashReportService(
        UserSettingsService settingsService,
        SipLogService log,
        IConfiguration configuration)
    {
        _settingsService = settingsService;
        _log = log;
        _crashDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog",
            "crashes");

        _smtpHost = configuration["CrashReport:SmtpHost"];
        _smtpPort = configuration.GetValue("CrashReport:SmtpPort", 587);
        _smtpUser = configuration["CrashReport:SmtpUser"];
        _smtpPassword = configuration["CrashReport:SmtpPassword"];
        _smtpUseSsl = configuration.GetValue("CrashReport:UseSsl", true);
        _smtpFrom = configuration["CrashReport:FromEmail"] ?? "softphone@callanalog.com";
    }

    public void HandleException(Exception exception, string source, bool isTerminating)
    {
        try
        {
            var reportPath = WriteReport(exception, source, isTerminating);
            _log.Error($"Crash captured ({source}, terminating={isTerminating}): {exception.Message}");

            if (_settingsService.Settings.SendCrashReport)
            {
                TrySendReport(reportPath);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to capture crash report: {ex.Message}");
        }
    }

    public void SendPendingReports()
    {
        if (!_settingsService.Settings.SendCrashReport)
        {
            return;
        }

        if (!Directory.Exists(_crashDirectory))
        {
            return;
        }

        foreach (var pending in Directory.GetFiles(_crashDirectory, "*.txt"))
        {
            if (pending.EndsWith(".sent.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TrySendReport(pending);
        }
    }

    private string WriteReport(Exception exception, string source, bool isTerminating)
    {
        Directory.CreateDirectory(_crashDirectory);

        var fileName = $"crash_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt";
        var path = Path.Combine(_crashDirectory, fileName);

        var builder = new StringBuilder();
        builder.AppendLine("CallAnalog Softphone Crash Report");
        builder.AppendLine($"Timestamp: {DateTime.Now:O}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"Terminating: {isTerminating}");
        builder.AppendLine($"Extension: {_settingsService.Settings.Extension}");
        builder.AppendLine($"Version: {App.Configuration["App:Version"] ?? "1.0.0"}");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($"Runtime: {Environment.Version}");
        builder.AppendLine();
        builder.AppendLine(exception.ToString());

        File.WriteAllText(path, builder.ToString());
        return path;
    }

    private void TrySendReport(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(_smtpHost))
        {
            _log.Info("Crash report saved locally. Configure CrashReport:SmtpHost in appsettings.json to enable email delivery.");
            return;
        }

        var recipient = _settingsService.Settings.CrashReportEmail;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        try
        {
            using var message = new MailMessage(_smtpFrom, recipient)
            {
                Subject = "CallAnalog Softphone Crash Report",
                Body = File.ReadAllText(reportPath),
                IsBodyHtml = false
            };

            using var client = new SmtpClient(_smtpHost, _smtpPort)
            {
                EnableSsl = _smtpUseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(_smtpUser))
            {
                client.Credentials = new NetworkCredential(_smtpUser, _smtpPassword);
            }

            client.Send(message);
            File.Move(reportPath, reportPath + ".sent.txt", overwrite: true);
            _log.Info($"Crash report emailed to {recipient}");
        }
        catch (Exception ex)
        {
            _log.Error($"Crash report email failed: {ex.Message}");
        }
    }
}
