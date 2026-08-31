using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CallAnalog.Softphone.Views.Panels;

public sealed class ContactFormResult
{
    public required string Name { get; init; }
    public required string Number { get; init; }
}

public partial class ContactFormPanel : UserControl, IShellPanel
{
    // Name: letters, numbers and spaces.
    private static readonly Regex NameInputRegex =
        new(@"^[a-zA-Z0-9 ]$");

    // Phone: numbers only.
    private static readonly Regex PhoneInputRegex =
        new(@"^[0-9]$");

    private static readonly Brush ValidBorderBrush =
        Brushes.Transparent;

    private readonly Brush _invalidBorderBrush;

    public event EventHandler<ShellPanelResult>? CloseRequested;

    public ContactFormPanel(string title, string name, string number)
    {
        InitializeComponent();

        _invalidBorderBrush =
            (Brush)FindResource("PhoneHangupRedBrush");

        TitleText.Text = title;
        NameBox.Text = name;
        NumberBox.Text = number;

        NameBox.TextChanged += NameBox_TextChanged;
        NumberBox.TextChanged += NumberBox_TextChanged;

        Loaded += ContactFormPanel_Loaded;
    }

    private void ContactFormPanel_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        NameBox.Focus();

        // Initial state:
        // Do not show validation error when Add Contact opens.
        ErrorText.Visibility = Visibility.Hidden;
        ErrorText.Text = string.Empty;

        NameBox.BorderBrush = ValidBorderBrush;
        NumberBox.BorderBrush = ValidBorderBrush;

        SaveButton.IsEnabled =
            !string.IsNullOrWhiteSpace(NameBox.Text) &&
            !string.IsNullOrWhiteSpace(NumberBox.Text);
    }

    private void NameBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ValidateForm();
    }

    private void NumberBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ValidateForm();
    }

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!ValidateForm())
        {
            return;
        }

        CloseRequested?.Invoke(this, new ShellPanelResult
        {
            Result = new ContactFormResult
            {
                Name = NameBox.Text.Trim(),
                Number = NumberBox.Text.Trim()
            }
        });
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, new ShellPanelResult());
    }

    private void NameBox_PreviewTextInput(
        object sender,
        TextCompositionEventArgs e)
    {
        e.Handled = !NameInputRegex.IsMatch(e.Text);
    }

    private void NameBox_Pasting(
        object sender,
        DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }

        var pasted =
            e.DataObject.GetData(typeof(string)) as string
            ?? string.Empty;

        if (pasted.Any(ch =>
                !NameInputRegex.IsMatch(ch.ToString())))
        {
            e.CancelCommand();
        }
    }

    private void NumberBox_PreviewTextInput(
        object sender,
        TextCompositionEventArgs e)
    {
        e.Handled = !PhoneInputRegex.IsMatch(e.Text);
    }

    private void NumberBox_Pasting(
        object sender,
        DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }

        var pasted =
            e.DataObject.GetData(typeof(string)) as string
            ?? string.Empty;

        if (pasted.Any(ch =>
                !PhoneInputRegex.IsMatch(ch.ToString())))
        {
            e.CancelCommand();
        }
    }

    private bool ValidateForm()
    {
        var nameValid =
            !string.IsNullOrWhiteSpace(NameBox.Text);

        var numberValid =
            !string.IsNullOrWhiteSpace(NumberBox.Text);

        NameBox.BorderBrush =
            nameValid
                ? ValidBorderBrush
                : _invalidBorderBrush;

        NumberBox.BorderBrush =
            numberValid
                ? ValidBorderBrush
                : _invalidBorderBrush;

        var isValid = nameValid && numberValid;

        SaveButton.IsEnabled = isValid;

        if (isValid)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = string.Empty;
        }
        else
        {
            ErrorText.Text =
                "Name and number are required.";

            ErrorText.Visibility =
                Visibility.Visible;
        }

        return isValid;
    }
}