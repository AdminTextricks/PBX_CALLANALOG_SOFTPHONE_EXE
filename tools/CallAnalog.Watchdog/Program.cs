using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CallAnalog.Watchdog;

internal static class Program
{
    private const int HeartbeatTimeoutMs = 15_000;
    private const int MiniDumpWithHandleData = 0x00000004;
    private const int MiniDumpWithUnloadedModules = 0x00000020;
    private const int MiniDumpWithThreadInfo = 0x00001000;
    private const int HangDumpType = MiniDumpWithHandleData | MiniDumpWithUnloadedModules | MiniDumpWithThreadInfo;

    private static int Main(string[] args)
    {
        if (!TryParsePid(args, out var pid))
        {
            return 1;
        }

        EventWaitHandle? heartbeat = null;
        EventWaitHandle? stopping = null;
        Process? parent = null;
        ProcessExitWaitHandle? parentWait = null;
        try
        {
            heartbeat = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                $"Local\\CallAnalog.Softphone.Heartbeat.{pid}");
            stopping = new EventWaitHandle(
                initialState: false,
                EventResetMode.ManualReset,
                $"Local\\CallAnalog.Softphone.Stopping.{pid}");

            try
            {
                parent = Process.GetProcessById(pid);
            }
            catch
            {
                WriteReasonText(pid, "parent_missing", "Parent process was not found.");
                return 1;
            }

            if (parent.HasExited)
            {
                WriteReasonText(pid, "parent_already_exited", "Parent process had already exited.");
                return 0;
            }

            parentWait = new ProcessExitWaitHandle(parent);
            var handles = new WaitHandle[] { heartbeat, stopping, parentWait };

            while (true)
            {
                int signaled;
                try
                {
                    signaled = WaitHandle.WaitAny(handles, HeartbeatTimeoutMs);
                }
                catch
                {
                    return 1;
                }

                if (signaled == 0)
                {
                    continue;
                }

                if (signaled == 1)
                {
                    return 0;
                }

                if (signaled == 2)
                {
                    WriteReasonText(
                        pid,
                        "parent_exited",
                        "Parent process exited without the Stopping signal.");
                    return 0;
                }

                if (signaled == WaitHandle.WaitTimeout)
                {
                    if (stopping.WaitOne(0))
                    {
                        return 0;
                    }

                    try
                    {
                        if (parent.HasExited)
                        {
                            WriteReasonText(
                                pid,
                                "parent_exited",
                                "Parent process exited without the Stopping signal.");
                            return 0;
                        }
                    }
                    catch
                    {
                        WriteReasonText(
                            pid,
                            "parent_exited",
                            "Parent process exited without the Stopping signal.");
                        return 0;
                    }

                    WriteHangDump(pid);
                    return 0;
                }

                return 1;
            }
        }
        catch
        {
            return 1;
        }
        finally
        {
            parentWait?.Dispose();
            parent?.Dispose();
            heartbeat?.Dispose();
            stopping?.Dispose();
        }
    }

    private static bool TryParsePid(string[] args, out int pid)
    {
        pid = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--pid", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out pid)
                && pid > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteHangDump(int pid)
    {
        var crashes = GetCrashDirectory();
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var dumpPath = Path.Combine(crashes, $"hang_{pid}_{stamp}.dmp");
        var textPath = Path.Combine(crashes, $"hang_{pid}_{stamp}.txt");
        WriteText(
            textPath,
            pid,
            "ui_hang",
            $"No Dispatcher heartbeat for {HeartbeatTimeoutMs / 1000} seconds. Dump: {dumpPath}");

        try
        {
            using var file = File.Create(dumpPath);
            var processHandle = OpenProcess(
                ProcessQueryInformation | ProcessVmRead | ProcessDupHandle,
                false,
                pid);
            if (processHandle == IntPtr.Zero)
            {
                var openError = Marshal.GetLastWin32Error();
                File.AppendAllText(textPath, Environment.NewLine + $"OpenProcess failed. Win32={openError}");
                return;
            }

            try
            {
                if (!MiniDumpWriteDump(
                        processHandle,
                        (uint)pid,
                        file.SafeFileHandle,
                        HangDumpType,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    File.AppendAllText(textPath, Environment.NewLine + $"MiniDumpWriteDump failed. Win32={error}");
                }
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(textPath, Environment.NewLine + $"Dump failed: {ex.GetType().Name}: {ex.Message}");
            }
            catch
            {
            }
        }
    }

    private static void WriteReasonText(int pid, string reason, string detail)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(GetCrashDirectory(), $"{reason}_{pid}_{stamp}.txt");
        WriteText(path, pid, reason, detail);
    }

    private static void WriteText(string path, int pid, string reason, string detail)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                $"CallAnalog watchdog{Environment.NewLine}" +
                $"Timestamp: {DateTime.Now:O}{Environment.NewLine}" +
                $"PID: {pid}{Environment.NewLine}" +
                $"Reason: {reason}{Environment.NewLine}" +
                $"{detail}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static string GetCrashDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog",
            "crashes");

    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessDupHandle = 0x0040;
    private const uint ProcessQueryInformation = 0x0400;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        SafeFileHandle hFile,
        int dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    private sealed class ProcessExitWaitHandle : WaitHandle
    {
        public ProcessExitWaitHandle(Process process)
        {
            SafeWaitHandle = new SafeWaitHandle(process.Handle, ownsHandle: false);
        }
    }
}
