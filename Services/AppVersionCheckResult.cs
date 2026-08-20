namespace CallAnalog.Softphone.Services;

public sealed class AppVersionCheckResult
{
    public required bool UpdateAvailable { get; init; }
    public required string InstalledVersion { get; init; }
    public required string CurrentVersion { get; init; }
    public string? ApplicationName { get; init; }
    public string? ReleaseStatus { get; init; }
    public DateTimeOffset? ReleaseDate { get; init; }
    public string? Message { get; init; }

    public string FormatStatusMessage(string localBuildLabel)
    {
        if (UpdateAvailable)
        {
            return $"Update available: {CurrentVersion} (you have {InstalledVersion}). Contact CallAnalog support or your IT admin for the installer.";
        }

        return $"You're up to date — {localBuildLabel}. Latest released: {CurrentVersion}.";
    }
}
