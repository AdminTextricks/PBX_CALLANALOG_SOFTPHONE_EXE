using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CallAnalog.Softphone.Views.Controls;

public partial class SkeletonShimmer : UserControl
{
    public static readonly DependencyProperty RowCountProperty =
        DependencyProperty.Register(nameof(RowCount), typeof(int), typeof(SkeletonShimmer),
            new PropertyMetadata(4, (_, _) => { }));

    public SkeletonShimmer()
    {
        InitializeComponent();
        Loaded += (_, _) => BuildRows();
    }

    public int RowCount
    {
        get => (int)GetValue(RowCountProperty);
        set => SetValue(RowCountProperty, value);
    }

    private void BuildRows()
    {
        RowsPanel.Children.Clear();
        for (var i = 0; i < RowCount; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10), Height = 62 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avatar = CreateShimmerBlock(46, 46, 23);
            Grid.SetColumn(avatar, 0);
            row.Children.Add(avatar);

            var textStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(CreateShimmerBlock(180, 14, 7));
            textStack.Children.Add(CreateShimmerBlock(120, 12, 6, new Thickness(0, 8, 0, 0)));
            Grid.SetColumn(textStack, 1);
            row.Children.Add(textStack);

            RowsPanel.Children.Add(row);
        }
    }

    private Border CreateShimmerBlock(double width, double height, double radius, Thickness? margin = null)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(radius),
            Background = (Brush)FindResource("PhoneSurfaceElevatedBrush"),
            Margin = margin ?? new Thickness(0),
            ClipToBounds = true
        };

        var shimmer = new Border
        {
            Width = width * 0.4,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var animation = new DoubleAnimation(-width, width * 2, TimeSpan.FromMilliseconds(1200))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        shimmer.RenderTransform = new TranslateTransform();
        shimmer.RenderTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        border.Child = shimmer;
        return border;
    }
}
