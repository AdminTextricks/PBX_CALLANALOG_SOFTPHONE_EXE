using System.Windows;
using System.Windows.Controls;

namespace CallAnalog.Softphone.Views.Panels;

public partial class ConfirmPanel : UserControl, IShellPanel
{
    public event EventHandler<ShellPanelResult>? CloseRequested;

    public ConfirmPanel(string title, string message, string confirmText, string cancelText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, new ShellPanelResult { Result = true });

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, new ShellPanelResult { Result = false });
}
