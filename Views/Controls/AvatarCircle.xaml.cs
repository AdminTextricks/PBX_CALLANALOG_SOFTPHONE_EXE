using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CallAnalog.Softphone.Helpers;

namespace CallAnalog.Softphone.Views.Controls;

public partial class AvatarCircle : UserControl
{
    public static readonly DependencyProperty DisplayNameProperty =
        DependencyProperty.Register(nameof(DisplayName), typeof(string), typeof(AvatarCircle),
            new PropertyMetadata(string.Empty, OnIdentityChanged));

    public static readonly DependencyProperty NumberProperty =
        DependencyProperty.Register(nameof(Number), typeof(string), typeof(AvatarCircle),
            new PropertyMetadata(string.Empty, OnIdentityChanged));

    public static readonly DependencyProperty InitialsFontSizeProperty =
        DependencyProperty.Register(nameof(InitialsFontSize), typeof(double), typeof(AvatarCircle),
            new PropertyMetadata(16.0));

    public AvatarCircle()
    {
        InitializeComponent();
    }

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public string Number
    {
        get => (string)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public double InitialsFontSize
    {
        get => (double)GetValue(InitialsFontSizeProperty);
        set => SetValue(InitialsFontSizeProperty, value);
    }

    private static void OnIdentityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AvatarCircle avatar)
        {
            avatar.Refresh();
        }
    }

    private void Refresh()
    {
        var seed = !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Number;
        InitialsText.Text = AvatarHelper.GetInitials(DisplayName, Number);
        InitialsText.FontSize = InitialsFontSize;
        BackgroundEllipse.Fill = AvatarHelper.GetAvatarBrush(seed);
    }
}
