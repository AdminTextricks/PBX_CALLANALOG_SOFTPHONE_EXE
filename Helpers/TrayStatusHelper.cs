using CallAnalog.Softphone.Models;
using System.Windows.Media;

namespace CallAnalog.Softphone.Helpers;

internal static class TrayStatusHelper
{
    internal static Color GetOverlayColor(ConnectionStatus connectionStatus, CallState callState) =>
        callState switch
        {
            CallState.InCall or CallState.OnHold or CallState.Outgoing => Colors.LimeGreen,
            CallState.Incoming or CallState.CallWaitingRinging => Colors.MediumSeaGreen,
            _ => connectionStatus switch
            {
                ConnectionStatus.Online => Colors.LimeGreen,
                ConnectionStatus.Registering or ConnectionStatus.Reconnecting => Colors.Orange,
                ConnectionStatus.Disconnected => Colors.OrangeRed,
                _ => Colors.Gray
            }
        };

    internal static string GetDndMenuLabel(bool dndEnabled) =>
        dndEnabled ? "Turn DND Off" : "Turn DND On";
}
