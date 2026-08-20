using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CallAnalog.Softphone.Helpers;

public static class PageTransitionHelper
{
    public const int DurationMs = 220;
    private const double SlideDistance = 22;

    public static void ShowImmediate(FrameworkElement show, FrameworkElement? hide)
    {
        if (hide is not null)
        {
            hide.BeginAnimation(UIElement.OpacityProperty, null);
            hide.RenderTransform = null;
            hide.Visibility = Visibility.Collapsed;
            hide.Opacity = 1;
        }

        show.BeginAnimation(UIElement.OpacityProperty, null);
        show.RenderTransform = null;
        show.Visibility = Visibility.Visible;
        show.Opacity = 1;
    }

    public static Task SwitchAsync(FrameworkElement? from, FrameworkElement to, int direction)
    {
        var tcs = new TaskCompletionSource();

        to.Visibility = Visibility.Visible;
        to.Opacity = 0;
        to.RenderTransform = new TranslateTransform();

        var duration = TimeSpan.FromMilliseconds(DurationMs);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var slideOffset = direction == 0 ? 0 : SlideDistance * Math.Sign(direction);

        var toOpacity = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
        var toSlide = new DoubleAnimation(slideOffset, 0, duration) { EasingFunction = easing };

        if (from is not null && from.Visibility == Visibility.Visible && !ReferenceEquals(from, to))
        {
            from.RenderTransform ??= new TranslateTransform();

            var fromOpacity = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
            var fromSlide = new DoubleAnimation(0, -slideOffset, duration) { EasingFunction = easing };

            fromOpacity.Completed += (_, _) =>
            {
                from.BeginAnimation(UIElement.OpacityProperty, null);
                from.RenderTransform = null;
                from.Visibility = Visibility.Collapsed;
                from.Opacity = 1;
            };

            toOpacity.Completed += (_, _) => tcs.TrySetResult();

            from.BeginAnimation(UIElement.OpacityProperty, fromOpacity);
            if (from.RenderTransform is TranslateTransform fromTransform)
            {
                fromTransform.BeginAnimation(TranslateTransform.XProperty, fromSlide);
            }
        }
        else
        {
            toOpacity.Completed += (_, _) => tcs.TrySetResult();
        }

        to.BeginAnimation(UIElement.OpacityProperty, toOpacity);
        if (to.RenderTransform is TranslateTransform toTransform)
        {
            toTransform.BeginAnimation(TranslateTransform.XProperty, toSlide);
        }

        return tcs.Task;
    }
}
