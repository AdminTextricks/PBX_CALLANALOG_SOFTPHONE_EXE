using System.IO;
using System.Text.Json;

namespace CallAnalog.Softphone.Services;

internal static class SettingsPersistenceHelper
{
    public static void WriteJsonAtomically<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(value, options);
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
