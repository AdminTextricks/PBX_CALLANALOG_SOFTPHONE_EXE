using System.Windows;
using System.Windows.Threading;
using CallAnalog.Softphone.Services;
using CallAnalog.Softphone.Helpers;
using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone;

public partial class App : Application
{
    public static IConfiguration Configuration { get; private set; } = null!;
    public static TrayIconService TrayIcon { get; private set; } = null!;
    public static UserSettingsService UserSettings { get; private set; } = null!;
    public static SipLogService SipLog { get; private set; } = null!;

    private CrashReportService? _crashReportService;
    private SingleInstanceService? _singleInstance;
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

        _ = Task.Run(() => _crashReportService.SendPendingReports());

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MediaFoundationLifecycle.ForceShutdown();
        TrayIcon?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    internal void RequestShutdown()
    {
        _isExiting = true;
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
}
