using System.IO;

namespace CallAnalog.Softphone.Services;

public sealed class SipLogService
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int MaxRotatedFiles = 3;

    private readonly string _logFilePath;
    private readonly object _sync = new();

    public SipLogService(UserSettingsService settingsService)
    {
        Directory.CreateDirectory(settingsService.LogsFolderPath);
        _logFilePath = Path.Combine(settingsService.LogsFolderPath, "sip.log");
    }

    public string LogFilePath => _logFilePath;

    public void Debug(string message) => Write("DEBUG", null, message);

    public void Debug(SipLogTag tag, string message) => Write("DEBUG", tag, message);

    public void Info(string message) => Write("INFO", null, message);

    public void Info(SipLogTag tag, string message) => Write("INFO", tag, message);

    public void Warn(string message) => Write("WARN", null, message);

    public void Warn(SipLogTag tag, string message) => Write("WARN", tag, message);

    public void Error(string message) => Write("ERROR", null, message);

    public void Error(SipLogTag tag, string message) => Write("ERROR", tag, message);

    public void CustomerError(SipLogTag tag, string whatHappened, string whatToTry)
    {
        Write("ERROR", tag, whatHappened);
        Write("INFO", tag, $"What to try: {whatToTry}");
    }

    public void BeginSection(string title) => WriteLine($"========== {title.ToUpperInvariant()} ==========");

    public void EndSection(string title) => WriteLine($"========== {title.ToUpperInvariant()} END ==========");

    public void Comment(string message) => WriteLine($"// {message}");

    public void WriteStartupBanner(string? extension = null)
    {
        BeginSection("APPLICATION START");
        Info(SipLogTag.Startup, $"CallAnalog Softphone {BuildInfo.FullBuildLabel}");
        Info(SipLogTag.Startup, $"OS: {Environment.OSVersion} · Machine: {Environment.MachineName}");
        Info(SipLogTag.Startup, $".NET runtime: {Environment.Version}");
        Info(SipLogTag.Startup, $"Log file: {_logFilePath}");
        if (!string.IsNullOrWhiteSpace(extension))
        {
            Info(SipLogTag.Startup, $"Remembered extension: {extension}");
        }

        Comment("Structured log for sign-in, SIP registration, calls, and network events.");
        Comment("Passwords and SIP Authorization headers are never written to this file.");
        EndSection("APPLICATION START");
    }

    public void EnsureLogFileExists()
    {
        lock (_sync)
        {
            if (File.Exists(_logFilePath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
            File.WriteAllText(
                _logFilePath,
                $"// CallAnalog SIP log created {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
        }
    }

    public void LogWireOut(string statusLine, string body)
    {
        Info(SipLogTag.Wire, $"SIP OUT {statusLine}");
        AppendWireBody(body);
    }

    public void LogWireIn(string shortDescription, string body)
    {
        Info(SipLogTag.Wire, $"SIP IN {shortDescription}");
        AppendWireBody(body);
    }

    private void AppendWireBody(string body)
    {
        foreach (var line in SipLogRedaction.Redact(body).Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                Info(SipLogTag.Wire, trimmed);
            }
        }
    }

    private void Write(string level, SipLogTag? tag, string message)
    {
        var tagPrefix = tag is { } value && value != SipLogTag.General
            ? $"[{value.ToString().ToUpperInvariant()}] "
            : string.Empty;
        WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {tagPrefix}{message}");
    }

    private void WriteLine(string line)
    {
        lock (_sync)
        {
            RotateIfNeeded();
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logFilePath))
        {
            return;
        }

        var info = new FileInfo(_logFilePath);
        if (info.Length < MaxLogBytes)
        {
            return;
        }

        for (var index = MaxRotatedFiles - 1; index >= 1; index--)
        {
            var source = $"{_logFilePath}.{index}";
            var target = $"{_logFilePath}.{index + 1}";
            if (File.Exists(source))
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(source, target);
            }
        }

        var firstArchive = $"{_logFilePath}.1";
        if (File.Exists(firstArchive))
        {
            File.Delete(firstArchive);
        }

        File.Move(_logFilePath, firstArchive);
    }
}
