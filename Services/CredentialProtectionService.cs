using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CallAnalog.Softphone.Services;

public sealed class CredentialProtectionService
{
    private const string PasswordFileName = "auth.password.enc";
    private const string ExtensionFileName = "auth.extension.enc";

    private readonly string _storageDirectory;

    public CredentialProtectionService(string storageDirectory)
    {
        _storageDirectory = storageDirectory;
        Directory.CreateDirectory(_storageDirectory);
        DeleteLegacyPlaintextPasswordFile();
    }

    public void SavePassword(string? password)
    {
        var path = GetPasswordPath();
        if (string.IsNullOrEmpty(password))
        {
            DeleteIfExists(path);
            return;
        }

        WriteProtected(path, password);
    }

    public string LoadPassword()
    {
        var path = GetPasswordPath();
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return ReadProtected(path);
        }
        catch
        {
            DeleteIfExists(path);
            return string.Empty;
        }
    }

    public void MigratePlaintextPassword(string? plaintextPassword)
    {
        if (string.IsNullOrEmpty(plaintextPassword))
        {
            return;
        }

        if (File.Exists(GetPasswordPath()))
        {
            return;
        }

        SavePassword(plaintextPassword);
    }

    public void SaveExtension(string? extension)
    {
        var path = GetExtensionPath();
        if (string.IsNullOrWhiteSpace(extension))
        {
            DeleteIfExists(path);
            return;
        }

        WriteProtected(path, extension.Trim());
    }

    public string LoadExtension()
    {
        var path = GetExtensionPath();
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return ReadProtected(path);
        }
        catch
        {
            DeleteIfExists(path);
            return string.Empty;
        }
    }

    public void MigratePlaintextExtension(string? plaintextExtension)
    {
        if (string.IsNullOrWhiteSpace(plaintextExtension))
        {
            return;
        }

        if (File.Exists(GetExtensionPath()))
        {
            return;
        }

        SaveExtension(plaintextExtension);
    }

    private string GetPasswordPath() => Path.Combine(_storageDirectory, PasswordFileName);

    private string GetExtensionPath() => Path.Combine(_storageDirectory, ExtensionFileName);

    private void WriteProtected(string path, string value)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBytes);
    }

    private static string ReadProtected(string path)
    {
        var protectedBytes = File.ReadAllBytes(path);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void DeleteLegacyPlaintextPasswordFile()
    {
        DeleteIfExists(Path.Combine(_storageDirectory, "auth.password"));
    }
}
