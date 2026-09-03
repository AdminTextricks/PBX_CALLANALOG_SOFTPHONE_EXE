using System.Windows;
using System.Windows.Controls;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Views.Controls;

public partial class CallRecordRow : UserControl
{
    public event EventHandler<string>? DialRequested;

    public CallRecordRow()
    {
        InitializeComponent();
    }

    private void RowButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CallRecord record)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(record.DialNumber) || record.DialNumber == "—")
        {
            return;
        }

        DialRequested?.Invoke(this, record.DialNumber);
    }
}
