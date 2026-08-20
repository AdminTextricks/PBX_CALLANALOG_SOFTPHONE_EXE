using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Views.Panels;

public partial class DialStatusPanel : UserControl, IShellPanel
{
    private CancellationTokenSource? _cancellationSource;

    public event EventHandler<ShellPanelResult>? CloseRequested;

    public DialStatusPanel(string number)
    {
        InitializeComponent();
        NumberText.Text = number;
    }

    public static async Task<DialResult> RunDialFlowAsync(
        MainWindow host,
        string number,
        Func<CancellationToken, Task<DialResult>> dialAction)
    {
        using var cts = new CancellationTokenSource();
        var panel = new DialStatusPanel(number);
        panel._cancellationSource = cts;
        panel.TitleText.Text = "Dialing...";
        panel.MessageText.Text = "Connecting your call...";
        panel.ConnectingSpinner.Visibility = Visibility.Visible;
        panel.StatusDot.Visibility = Visibility.Collapsed;
        panel.CancelButton.Visibility = Visibility.Visible;
        host.ShowShellPanel(panel);

        DialResult result;
        try
        {
            result = await dialAction(cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new DialResult
            {
                Success = false,
                Number = number,
                Code = 499,
                Message = "Call cancelled",
                Reason = "The call was cancelled."
            };
        }
        finally
        {
            host.HideShellPanel();
        }

        if (!result.Success)
        {
            await ShowResultAsync(host, result);
        }

        return result;
    }

    public static async Task ShowResultAsync(MainWindow host, DialResult result)
    {
        var panel = new DialStatusPanel(result.Number);

        if (result.Success)
        {
            panel.TitleText.Text = "Call Connected";
            panel.MessageText.Text = result.Message;
            panel.ConnectingSpinner.Visibility = Visibility.Collapsed;
            panel.StatusDot.Visibility = Visibility.Visible;
            panel.StatusDot.Fill = (Brush)panel.FindResource("PhoneCallGreenBrush");
        }
        else
        {
            panel.TitleText.Text = "Call Failed";
            panel.MessageText.Text = result.Message;
            panel.ReasonText.Text = result.Reason;
            panel.ReasonText.Visibility = Visibility.Visible;
            panel.ConnectingSpinner.Visibility = Visibility.Collapsed;
            panel.StatusDot.Visibility = Visibility.Visible;
            panel.StatusDot.Fill = (Brush)panel.FindResource("PhoneHangupRedBrush");
        }

        panel.OkButton.Visibility = Visibility.Visible;
        await host.ShowShellPanelAsync<bool>(panel);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        MessageText.Text = "Cancelling call...";
        CancelButton.IsEnabled = false;
        _cancellationSource?.Cancel();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, new ShellPanelResult());
}
