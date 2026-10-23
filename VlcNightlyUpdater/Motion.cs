using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace VlcNightlyUpdater;

/// <summary>
/// Small, consistent motion language: everything eases out, nothing is slower than half a second.
/// Animations never leave a value "held", so code can keep setting Opacity/Visibility normally afterwards.
/// </summary>
internal static class Motion
{
    private static readonly EasingFunctionBase Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly EasingFunctionBase Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };

    private static CompositeTransform T(FrameworkElement e)
    {
        if (e.RenderTransform is CompositeTransform ct) return ct;
        e.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        return (CompositeTransform)(e.RenderTransform = new CompositeTransform());
    }

    /// <summary>from -> (hold until delay) -> to, as one animation so a delayed start doesn't flash the final value.</summary>
    private static DoubleAnimationUsingKeyFrames Track(DependencyObject target, string path,
        double from, double to, int delayMs, int ms, EasingFunctionBase ease)
    {
        var a = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true, FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = from });
        if (delayMs > 0)
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(delayMs), Value = from });
        a.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = TimeSpan.FromMilliseconds(delayMs + ms), Value = to, EasingFunction = ease
        });
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, path);
        return a;
    }

    /// <summary>Fade + rise into place. Used for the staggered entrance of the cards.</summary>
    public static void Reveal(FrameworkElement e, int delayMs = 0, double fromY = 16, int ms = 460)
    {
        var t = T(e);
        var sb = new Storyboard();
        sb.Children.Add(Track(e, "Opacity", 0, e.Opacity, delayMs, ms, Out));
        sb.Children.Add(Track(t, "TranslateY", fromY, 0, delayMs, ms, Out));
        sb.Begin();
    }

    /// <summary>Quick cross-fade nudge for text that just changed.</summary>
    public static void Swap(FrameworkElement e, double fromY = 6, int ms = 260)
    {
        var t = T(e);
        var sb = new Storyboard();
        sb.Children.Add(Track(e, "Opacity", 0, e.Opacity, 0, ms, Out));
        sb.Children.Add(Track(t, "TranslateY", fromY, 0, 0, ms, Out));
        sb.Begin();
    }

    /// <summary>Springy scale-in for the status badge.</summary>
    public static void Pop(FrameworkElement e, int ms = 460)
    {
        var t = T(e);
        var sb = new Storyboard();
        sb.Children.Add(Track(t, "ScaleX", 0.55, 1, 0, ms, Spring));
        sb.Children.Add(Track(t, "ScaleY", 0.55, 1, 0, ms, Spring));
        sb.Children.Add(Track(e, "Opacity", 0, e.Opacity, 0, ms / 2, Out));
        sb.Begin();
    }

    /// <summary>Draws a stroked path from start to end (needs StrokeDashArray="10,10").</summary>
    public static void Draw(ShapePath p, int delayMs = 140, int ms = 480)
    {
        var sb = new Storyboard();
        sb.Children.Add(Track(p, "StrokeDashOffset", 10, 0, delayMs, ms, Out));
        sb.Begin();
    }
}
