using System.Windows;
using System.Windows.Media;

namespace CallAnalog.Softphone.Models;

public enum ConnectionStatus
{
    Online,
    Offline,
    Registering,
    Reconnecting,
    Disconnected,
    LoggedOut
}

public static class ConnectionStatusInfo
{
    public static string GetLabel(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Online => "Online",
        ConnectionStatus.Offline => "Offline",
        ConnectionStatus.Registering => "Registering",
        ConnectionStatus.Reconnecting => "Reconnecting",
        ConnectionStatus.Disconnected => "Disconnected",
        ConnectionStatus.LoggedOut => "Logged Out",
        _ => "Offline"
    };

    public static Brush GetBrush(ConnectionStatus status) =>
        FindBrush(GetDotBrushKey(status));

    public static Brush GetChipBackgroundBrush(ConnectionStatus status) =>
        FindBrush(status switch
        {
            ConnectionStatus.Online => "PhoneStatusOnlineBgBrush",
            ConnectionStatus.Registering => "PhoneStatusRegisteringBgBrush",
            ConnectionStatus.Reconnecting => "PhoneStatusRegisteringBgBrush",
            ConnectionStatus.Disconnected => "PhoneStatusDisconnectedBgBrush",
            _ => "PhoneChipBgBrush"
        });

    public static Brush GetChipBorderBrush(ConnectionStatus status) =>
        FindBrush(status switch
        {
            ConnectionStatus.Online => "PhoneStatusOnlineBrush",
            ConnectionStatus.Registering => "PhoneStatusRegisteringBrush",
            ConnectionStatus.Reconnecting => "PhoneStatusReconnectingBrush",
            ConnectionStatus.Disconnected => "PhoneStatusDisconnectedBrush",
            _ => "PhoneCardBorderBrush"
        });

    public static Brush GetChipForegroundBrush(ConnectionStatus status) =>
        FindBrush(status switch
        {
            ConnectionStatus.Online => "PhoneStatusOnlineBrush",
            ConnectionStatus.Registering => "PhoneStatusRegisteringBrush",
            ConnectionStatus.Reconnecting => "PhoneStatusReconnectingBrush",
            ConnectionStatus.Disconnected => "PhoneStatusDisconnectedBrush",
            _ => "PhoneTextPrimaryBrush"
        });

    private static string GetDotBrushKey(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Online => "PhoneStatusOnlineBrush",
        ConnectionStatus.Offline => "PhoneStatusOfflineBrush",
        ConnectionStatus.Registering => "PhoneStatusRegisteringBrush",
        ConnectionStatus.Reconnecting => "PhoneStatusReconnectingBrush",
        ConnectionStatus.Disconnected => "PhoneStatusDisconnectedBrush",
        ConnectionStatus.LoggedOut => "PhoneStatusLoggedOutBrush",
        _ => "PhoneStatusOfflineBrush"
    };

    private static Brush FindBrush(string key)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }
}
