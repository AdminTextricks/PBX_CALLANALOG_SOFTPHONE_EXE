using System.IO;

namespace CallAnalog.Softphone.Helpers;

internal static class MediaFileStorage
{
    private static string MediaDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog",
            "media");

    public static string CopyRingtoneToAppStorage(string sourcePath) =>
        CopyMediaToAppStorage(sourcePath, "ringtone");

    public static string CopyHoldMusicToAppStorage(string sourcePath) =>
        CopyMediaToAppStorage(sourcePath, "holdmusic");

    /// <summary>
    /// Returns the configured ringtone path when present, otherwise the copied file in app media storage.
    /// </summary>
    public static string? ResolveRingtonePath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        foreach (var extension in new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".ogg" })
        {
            var storedPath = Path.Combine(MediaDirectory, $"ringtone{extension}");
            if (File.Exists(storedPath))
            {
                return storedPath;
            }
        }

        return configuredPath;
    }

    private static string CopyMediaToAppStorage(string sourcePath, string baseName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Media file was not found.", sourcePath);
        }

        Directory.CreateDirectory(MediaDirectory);
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".wav";
        }

        var destinationPath = Path.Combine(MediaDirectory, $"{baseName}{extension.ToLowerInvariant()}");
        File.Copy(sourcePath, destinationPath, overwrite: true);
        return destinationPath;
    }
}
