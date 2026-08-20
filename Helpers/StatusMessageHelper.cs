using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Helpers;

public static class StatusMessageHelper
{
    public static Brush GetBrush(StatusMessageKind kind, FrameworkElement element) =>
        (Brush)element.FindResource(kind switch
        {
            StatusMessageKind.Success => "PhoneCallGreenBrush",
            StatusMessageKind.Warning => "PhoneStatusRegisteringBrush",
            StatusMessageKind.Error => "PhoneHangupRedBrush",
            StatusMessageKind.Progress => "PhoneAccentBrush",
            _ => "PhoneTextSecondaryBrush"
        });

    public static void Apply(TextBlock target, string message, StatusMessageKind kind = StatusMessageKind.Neutral)
    {
        target.Text = message;
        target.Foreground = GetBrush(kind, target);
    }
}
