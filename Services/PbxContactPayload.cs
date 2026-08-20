namespace CallAnalog.Softphone.Services;

internal static class PbxContactPayload
{
    public static string Build(string extension, string name, string number) =>
        $"{{\"contact_name\":\"{Escape(name)}\",\"contact_number\":\"{Escape(number)}\",\"extension_name\":\"{Escape(extension.Trim())}\"}}";

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
