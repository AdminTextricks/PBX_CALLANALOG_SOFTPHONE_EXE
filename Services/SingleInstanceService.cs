using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CallAnalog.Softphone.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "Global\\CallAnalog.Softphone.SingleInstance";
    private readonly Mutex? _mutex;
    private readonly bool _isPrimary;

    public SingleInstanceService()
    {
        _isPrimary = TryAcquireMutex(out _mutex);
    }

    public bool IsPrimaryInstance => _isPrimary;

    public void FocusExistingInstance()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var existing = System.Diagnostics.Process.GetProcessesByName(process.ProcessName)
            .FirstOrDefault(p => p.Id != process.Id);

        if (existing is null)
        {
            return;
        }

        var handle = existing.MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(handle))
        {
            ShowWindow(handle, SwRestore);
        }

        SetForegroundWindow(handle);
    }

    public void Dispose()
    {
        if (_isPrimary)
        {
            _mutex?.ReleaseMutex();
        }

        _mutex?.Dispose();
    }

    private static bool TryAcquireMutex(out Mutex? mutex)
    {
        mutex = null;
        try
        {
            mutex = new Mutex(true, MutexName, out var createdNew);
            return createdNew;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SwRestore = 9;
}
