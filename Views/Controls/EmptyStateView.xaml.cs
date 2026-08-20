using System.Windows.Controls;

namespace CallAnalog.Softphone.Views.Controls;

public partial class EmptyStateView : UserControl
{
    public EmptyStateView()
    {
        InitializeComponent();
    }

    public void SetContent(string title, string message, string? iconKey = null)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        if (!string.IsNullOrWhiteSpace(iconKey))
        {
            StateIcon.IconKey = iconKey;
        }
    }
}
