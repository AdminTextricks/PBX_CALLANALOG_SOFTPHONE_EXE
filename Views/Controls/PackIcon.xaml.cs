using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CallAnalog.Softphone.Views.Controls;

public partial class PackIcon : UserControl
{
    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(
            nameof(IconKey),
            typeof(string),
            typeof(PackIcon),
            new PropertyMetadata(string.Empty, OnIconKeyChanged));

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(
            nameof(IconSize),
            typeof(double),
            typeof(PackIcon),
            new PropertyMetadata(18.0));

    public PackIcon()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyIcon();
    }

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    private static void OnIconKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PackIcon)d).ApplyIcon();

    private void ApplyIcon()
    {
        if (string.IsNullOrWhiteSpace(IconKey))
        {
            IconPath.Data = null;
            IconPath.RenderTransform = null;
            return;
        }

        if (Application.Current.TryFindResource(IconKey) is not Geometry geometry)
        {
            IconPath.Data = null;
            IconPath.RenderTransform = null;
            return;
        }

        IconPath.Data = geometry;
        var bounds = geometry.Bounds;

        if (bounds.Height <= 0 && bounds.Width <= 0)
        {
            IconPath.RenderTransform = null;
            return;
        }

        // Material Symbols paths live in a -960..0 coordinate space.
        if (bounds.Top < -50 || bounds.Bottom <= 0)
        {
            IconPath.RenderTransform = new TranslateTransform(0, 960);
            return;
        }

        // Legacy 24x24-style paths (settings gear, etc.).
        const double canvas = 960;
        const double legacyDesignSize = 24;
        var scale = canvas / legacyDesignSize;
        IconPath.RenderTransform = new ScaleTransform(scale, scale);
    }
}
