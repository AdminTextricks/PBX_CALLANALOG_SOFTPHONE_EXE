using System.ComponentModel;
using System.Windows;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Views;

public partial class IncomingCallPopupWindow : Window
{
    private bool _allowClose;

    public IncomingCallPopupWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? AnswerRequested;
    public event EventHandler? DeclineRequested;

    public string? BoundCallId { get; private set; }

    public void Present(IncomingCallEventArgs callInfo, string? callId)
    {
        BoundCallId = callId;
        HeaderText.Text = callInfo.IsQueueCall ? "Queue Call" : "Incoming Call";

        var displayName = string.IsNullOrWhiteSpace(callInfo.CallerName)
            ? callInfo.CallerNumber
            : callInfo.CallerName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "Unknown";
        }

        CallerNameText.Text = displayName;
        var showNumber = !string.IsNullOrWhiteSpace(callInfo.CallerNumber)
            && !string.Equals(displayName, callInfo.CallerNumber, StringComparison.Ordinal);
        CallerNumberText.Text = callInfo.CallerNumber ?? string.Empty;
        CallerNumberText.Visibility = showNumber ? Visibility.Visible : Visibility.Collapsed;

        PositionOnWorkArea();
        if (!IsVisible)
        {
            Show();
        }

        Activate();
    }

    public void Dismiss()
    {
        BoundCallId = null;
        if (IsVisible)
        {
            Hide();
        }
    }

    public void ForceClose()
    {
        BoundCallId = null;
        _allowClose = true;
        Close();
    }

    public void ClearBinding() => BoundCallId = null;

    private void PositionOnWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 20;
    }

    private void AnswerButton_Click(object sender, RoutedEventArgs e) =>
        AnswerRequested?.Invoke(this, EventArgs.Empty);

    private void DeclineButton_Click(object sender, RoutedEventArgs e) =>
        DeclineRequested?.Invoke(this, EventArgs.Empty);

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
