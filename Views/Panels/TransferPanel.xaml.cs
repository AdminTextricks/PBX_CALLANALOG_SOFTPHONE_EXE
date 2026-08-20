using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Views.Panels;

public partial class TransferPanel : UserControl, IShellPanel
{
    private static readonly Regex ValidTargetRegex = new("^[0-9+*#]+$");

    public event EventHandler<ShellPanelResult>? CloseRequested;

    public TransferPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => TargetBox.Focus();
    }

    private void TransferButton_Click(object sender, RoutedEventArgs e)
    {
        var target = TargetBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            ShowError("Enter an extension or number.");
            return;
        }

        if (!ValidTargetRegex.IsMatch(target))
        {
            ShowError("Only digits, * and # are allowed.");
            return;
        }

        CloseRequested?.Invoke(this, new ShellPanelResult
        {
            Result = new TransferRequest
            {
                Target = target
            }
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, new ShellPanelResult());

    private void TargetBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !ValidTargetRegex.IsMatch(e.Text);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
