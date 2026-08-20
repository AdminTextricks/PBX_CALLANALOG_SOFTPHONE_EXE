using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CallAnalog.Softphone.Views.Controls;

public partial class SearchField : UserControl
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchField), new PropertyMetadata("Search..."));

    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.Register(nameof(Query), typeof(string), typeof(SearchField), new PropertyMetadata(string.Empty));

    public event EventHandler? SearchRequested;

    public SearchField()
    {
        InitializeComponent();
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    public string? NormalizedQuery =>
        string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SearchRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
