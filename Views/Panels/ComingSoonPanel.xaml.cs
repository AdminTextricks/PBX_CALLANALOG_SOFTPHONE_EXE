using System.Windows;
using System.Windows.Controls;

namespace CallAnalog.Softphone.Views.Panels;

public partial class ComingSoonPanel : UserControl, IShellPanel
{
    public event EventHandler<ShellPanelResult>? CloseRequested;

    public ComingSoonPanel(string featureName)
    {
        InitializeComponent();
        MessageText.Text = $"{featureName} is coming in the next version.";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, new ShellPanelResult());
}
