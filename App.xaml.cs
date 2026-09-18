using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CallAnalog.Softphone.Services;
using CallAnalog.Softphone.Helpers;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone;

public partial class App : Application
{
    private const string WatchdogExeName = "CallAnalog.Watchdog.exe";
    private static readonly TimeSpan WatchdogHeartbeatInterval = TimeSpan.FromSeconds(5);

    public static IConfiguration Configuration { get; private set; } = null!;
    public static TrayIconService TrayIcon { get; private set; } = null!;
    public static UserSettingsService UserSettings { get; private set; } = null!;
    public static SipLogService SipLog { get; private set; } = null!;

    private CrashReportService? _crashReportService;
    private SingleInstanceService? _singleInstance;
    private Process? _watchdogProcess;
    private Timer? _watchdogHeartbeat;
    private EventWaitHandle? _watchdogHeartbeatEvent;
    private EventWaitHandle? _watchdogStoppingEvent;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        var basePath = AppContext.BaseDirectory;
        Configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var userSettings = new UserSettingsService(Configuration);
        userSettings.ApplySavedStartupRegistration();
        var sipLog = new SipLogService(userSettings);
        UserSettings = userSettings;
        SipLog = sipLog;

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimaryInstance)
        {
            sipLog.Info(SipLogTag.Startup, "Second app instance detected — focusing existing window and exiting.");
            _singleInstance.FocusExistingInstance();
            Shutdown();
            return;
        }

        sipLog.WriteStartupBanner(userSettings.Settings.Extension);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _crashReportService = new CrashReportService(userSettings, sipLog, Configuration);

        ThemeManager.ApplyDarkMode();

        TrayIcon = new TrayIconService();

        var mainWindow = new MainWindow();
        TrayIcon.OpenDialpadRequested += (_, _) =>
        {
            if (mainWindow.AppShellPanel.Visibility == Visibility.Visible)
            {
                mainWindow.ShowDialpadFromTray();
            }
        };
        TrayIcon.DndToggleRequested += (_, _) => mainWindow.ToggleDndFromTray();
        TrayIcon.ExitRequested += async (_, _) => await mainWindow.ExitFromTrayAsync();
        TrayIcon.AttachMainWindow(mainWindow);
        MainWindow = mainWindow;
        mainWindow.Show();

        StartWatchdog();
        _ = Task.Run(() => _crashReportService.SendPendingReports());

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SignalWatchdogStopping();
        StopWatchdogHeartbeat();
        WaitForWatchdogExit();
        StopWatchdogProcess(killIfRunning: false);
        DisposeWatchdogEvents();
        MediaFoundationLifecycle.ForceShutdown();
        TrayIcon?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    internal void RequestShutdown()
    {
        _isExiting = true;
        SignalWatchdogStopping();
        StopWatchdogHeartbeat();
        WaitForWatchdogExit();
        Shutdown();
    }

    internal bool IsExiting => _isExiting;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _crashReportService?.HandleException(e.Exception, "UI thread", isTerminating: false);
        e.Handled = true;

        if (IsNonFatalAudioTeardown(e.Exception))
        {
            SipLog.Warn($"Non-fatal audio teardown suppressed (UI): {e.Exception.GetType().Name}: {e.Exception.Message}");
            return;
        }

        MessageBox.Show(
            "An unexpected error occurred. Details were saved to:\n%LOCALAPPDATA%\\CallAnalog\\crashes\\",
            "CallAnalog",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _crashReportService?.HandleException(ex, "AppDomain", e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _crashReportService?.HandleException(e.Exception, "Task", isTerminating: false);
        e.SetObserved();
    }

    private static bool IsNonFatalAudioTeardown(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ObjectDisposedException or InvalidOperationException)
            {
                return true;
            }

            var message = current.Message ?? string.Empty;
            if (message.Contains("WaveOut", StringComparison.OrdinalIgnoreCase)
                || message.Contains("MmException", StringComparison.OrdinalIgnoreCase)
                || message.Contains("MediaFoundation", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ACM", StringComparison.OrdinalIgnoreCase)
                || message.Contains("disposed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current.GetType().Name.Contains("MmException", StringComparison.OrdinalIgnoreCase)
                || current.GetType().Name.Contains("COMException", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void StartWatchdog()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, WatchdogExeName);
        if (!File.Exists(exePath))
        {
            SipLog.Warn(SipLogTag.Startup, $"Watchdog executable not found: {exePath}");
            return;
        }

        var pid = Environment.ProcessId;
        try
        {
            _watchdogHeartbeatEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                $"Local\\CallAnalog.Softphone.Heartbeat.{pid}");
            _watchdogStoppingEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.ManualReset,
                $"Local\\CallAnalog.Softphone.Stopping.{pid}");
        }
        catch (Exception ex)
        {
            SipLog.Warn(SipLogTag.Startup, $"Watchdog event create failed: {ex.GetType().Name}: {ex.Message}");
            DisposeWatchdogEvents();
            return;
        }

        try
        {
            _watchdogProcess = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--pid {pid}",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            SipLog.Warn(SipLogTag.Startup, $"Watchdog start failed: {ex.GetType().Name}: {ex.Message}");
            DisposeWatchdogEvents();
            return;
        }

        _watchdogHeartbeat = new Timer(
            OnWatchdogHeartbeat,
            state: null,
            dueTime: WatchdogHeartbeatInterval,
            period: WatchdogHeartbeatInterval);
    }

    private void OnWatchdogHeartbeat(object? state)
    {
        if (_isExiting)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_isExiting)
            {
                return;
            }

            try
            {
                _watchdogHeartbeatEvent?.Set();
            }
            catch
            {
                // Best-effort pulse.
            }
        });
    }

    private void SignalWatchdogStopping()
    {
        try
        {
            _watchdogStoppingEvent?.Set();
        }
        catch
        {
            // Best-effort clean-exit signal.
        }
    }

    private void StopWatchdogHeartbeat()
    {
        var timer = _watchdogHeartbeat;
        _watchdogHeartbeat = null;
        timer?.Dispose();
    }

    private void DisposeWatchdogEvents()
    {
        try
        {
            _watchdogHeartbeatEvent?.Dispose();
        }
        catch
        {
        }

        try
        {
            _watchdogStoppingEvent?.Dispose();
        }
        catch
        {
        }

        _watchdogHeartbeatEvent = null;
        _watchdogStoppingEvent = null;
    }

    private void WaitForWatchdogExit()
    {
        try
        {
            _watchdogProcess?.WaitForExit();
        }
        catch
        {
            // Watchdog may already have exited.
        }
    }

    private void StopWatchdogProcess(bool killIfRunning)
    {
        var process = _watchdogProcess;
        _watchdogProcess = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (killIfRunning && !process.HasExited)
            {
                process.Kill(entireProcessTree: false);
            }
        }
        catch
        {
            // Best-effort; watchdog may already have exited.
        }
        finally
        {
            process.Dispose();
        }
    }
}
