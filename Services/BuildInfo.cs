using System.IO;
using System.Reflection;
using System.Text;

namespace CallAnalog.Softphone.Services;

public static class BuildInfo
{
    public static string Version =>
        App.Configuration["App:Version"]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "1.0.0";

    public static string BuildDate
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }
    }

    public static string DisplayVersion => $"{Version} (built {BuildDate})";

    public static string? GitCommit
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "build-info.txt");
            if (!File.Exists(path))
            {
                return null;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("Commit=", StringComparison.OrdinalIgnoreCase))
                {
                    return line["Commit=".Length..].Trim();
                }
            }

            return null;
        }
    }

    public static string FullBuildLabel
    {
        get
        {
            var builder = new StringBuilder(DisplayVersion);
            var commit = GitCommit;
            if (!string.IsNullOrWhiteSpace(commit))
            {
                builder.Append(" · ");
                builder.Append(commit.Length > 8 ? commit[..8] : commit);
            }

            return builder.ToString();
        }
    }
}
