using System.Windows;
using System.Windows.Controls;
using CallAnalog.Softphone.Helpers;

namespace CallAnalog.Softphone.Views.Controls;

public partial class HighlightTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata(string.Empty, OnHighlightChanged));

    public static readonly DependencyProperty HighlightQueryProperty =
        DependencyProperty.Register(nameof(HighlightQuery), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata(null, OnHighlightChanged));

    public static readonly DependencyProperty TextStyleProperty =
        DependencyProperty.Register(nameof(TextStyle), typeof(Style), typeof(HighlightTextBlock),
            new PropertyMetadata(null, OnHighlightChanged));

    public HighlightTextBlock()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? HighlightQuery
    {
        get => (string?)GetValue(HighlightQueryProperty);
        set => SetValue(HighlightQueryProperty, value);
    }

    public Style? TextStyle
    {
        get => (Style?)GetValue(TextStyleProperty);
        set => SetValue(TextStyleProperty, value);
    }

    private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HighlightTextBlock control)
        {
            control.RefreshHighlight();
        }
    }

    private void RefreshHighlight()
    {
        if (TextStyle is not null)
        {
            DisplayText.Style = TextStyle;
        }

        SearchHighlightHelper.Apply(DisplayText, Text, HighlightQuery);
    }
}
