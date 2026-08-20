using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using Hardcodet.Wpf.TaskbarNotification;

namespace CallAnalog.Softphone.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly IncomingCallToastService _incomingCallToast = new();
    private readonly ImageSource _baseIcon;
    private MainWindow? _mainWindow;
    private string _statusText = "Offline";
    private ConnectionStatus _connectionStatus = ConnectionStatus.Offline;
    private CallState _callState = CallState.Idle;
    private bool _dndEnabled;
    private MenuItem? _statusItem;
    private MenuItem? _dndItem;

    public TrayIconService()
    {
        _baseIcon = LoadTrayIcon();
        _icon = new TaskbarIcon
        {
            ToolTipText = "CallAnalog Softphone — Offline",
            IconSource = _baseIcon
        };

        _icon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
        _incomingCallToast.Initialize();
    }

    public event EventHandler<IncomingCallNotificationActionEventArgs>? IncomingCallNotificationAction
    {
        add => _incomingCallToast.ActionRequested += value;
        remove => _incomingCallToast.ActionRequested -= value;
    }

    public event EventHandler? OpenDialpadRequested;
    public event EventHandler? DndToggleRequested;
    public event EventHandler? ExitRequested;

    public void AttachMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _icon.ContextMenu = CreateContextMenu();
    }

    public void SetDndEnabled(bool enabled) => _dndEnabled = enabled;

    public void SetStatus(string statusText, ConnectionStatus connectionStatus, CallState callState)
    {
        _statusText = statusText;
        _connectionStatus = connectionStatus;
        _callState = callState;
        _icon.ToolTipText = $"CallAnalog Softphone — {statusText}";
        _icon.IconSource = CreateStatusIcon(connectionStatus, callState);
        if (_statusItem is not null)
        {
            _statusItem.Header = $"Status: {statusText}";
        }

        if (_dndItem is not null)
        {
            _dndItem.Header = TrayStatusHelper.GetDndMenuLabel(_dndEnabled);
        }
    }

    public void HideToTray()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = false;
        _mainWindow.Hide();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ShowIncomingNotification(
        IncomingCallEventArgs callInfo,
        IncomingCallNotificationKind kind = IncomingCallNotificationKind.Incoming)
    {
        _incomingCallToast.ShowIncomingCall(callInfo, kind);
    }

    public void ShowMissedCallNotification(IncomingCallEventArgs callInfo)
    {
        var caller = MissedCallNotificationHelper.FormatCaller(callInfo);
        _icon.ShowBalloonTip("Missed call", $"Call from {caller} while you were on another call.", BalloonIcon.Warning);
    }

    public void DismissIncomingCallNotification() =>
        _incomingCallToast.DismissIncomingCallNotification();

    public void DismissCallWaitingNotification() =>
        _incomingCallToast.DismissCallWaitingNotification();

    public void DismissAllCallNotifications() =>
        _incomingCallToast.DismissAllCallNotifications();

    public void Dispose()
    {
        _incomingCallToast.Dispose();
        _icon.Dispose();
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        var showItem = new MenuItem { Header = "Show CallAnalog" };
        showItem.Click += (_, _) => ShowMainWindow();

        _statusItem = new MenuItem { Header = $"Status: {_statusText}", IsEnabled = false };

        _dndItem = new MenuItem { Header = _dndEnabled ? "Turn DND Off" : "Turn DND On" };
        _dndItem.Click += (_, _) => DndToggleRequested?.Invoke(this, EventArgs.Empty);

        var dialpadItem = new MenuItem { Header = "Open Dialpad" };
        dialpadItem.Click += (_, _) =>
        {
            ShowMainWindow();
            OpenDialpadRequested?.Invoke(this, EventArgs.Empty);
        };

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Opened += (_, _) =>
        {
            if (_statusItem is not null)
            {
                _statusItem.Header = $"Status: {_statusText}";
            }

            if (_dndItem is not null)
            {
                _dndItem.Header = _dndEnabled ? "Turn DND Off" : "Turn DND On";
            }
        };

        menu.Items.Add(showItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_dndItem);
        menu.Items.Add(dialpadItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private static ImageSource LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "favicon.png");
        if (!File.Exists(path))
        {
            return new BitmapImage(new Uri("pack://siteoforigin:,,,/Assets/favicon.png", UriKind.Absolute));
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private ImageSource CreateStatusIcon(ConnectionStatus connectionStatus, CallState callState)
    {
        var overlayColor = TrayStatusHelper.GetOverlayColor(connectionStatus, callState);

        if (_baseIcon is not BitmapSource source)
        {
            return _baseIcon;
        }

        const int size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, size, size));
            dc.DrawEllipse(new SolidColorBrush(overlayColor), null, new Point(size - 7, size - 7), 5, 5);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
