using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace StarryDisplay.Views;

/// <summary>
/// Small, reusable transitions. Every animation is short and ends; nothing loops, so the
/// display costs ~nothing between changes.
/// </summary>
public static class Anim
{
    public static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseInOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });

    public static void To(IAnimatable target, DependencyProperty property, double to, int ms, IEasingFunction? ease = null, Action? completed = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? EaseOut };
        if (completed is not null) a.Completed += (_, _) => completed();
        target.BeginAnimation(property, a, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Set(IAnimatable target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, null);
        ((DependencyObject)target).SetValue(property, value);
    }

    /// <summary>Fade out, apply the change, fade back in while rising a few pixels. Used for title/artist swaps.</summary>
    public static void SwapContent(FrameworkElement element, Action apply, double rise = 8, int outMs = 130, int inMs = 320)
    {
        var translate = element.RenderTransform as TranslateTransform;
        if (translate is null)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        if (!element.IsVisible)
        {
            apply();
            return;
        }

        To(element, UIElement.OpacityProperty, 0, outMs, EaseInOut, () =>
        {
            apply();
            Set(translate, TranslateTransform.YProperty, rise);
            To(translate, TranslateTransform.YProperty, 0, inMs);
            To(element, UIElement.OpacityProperty, 1, inMs);
        });
    }

    /// <summary>
    /// Crossfades a pair of stacked Images: the hidden one takes the new source and fades in on top.
    /// Returns the image that is now in front.
    /// </summary>
    public static Image Crossfade(Image front, Image back, ImageSource? source, int ms)
    {
        if (ReferenceEquals(front.Source, source)) return front;
        back.Source = source;
        Panel.SetZIndex(back, 1);
        Panel.SetZIndex(front, 0);
        Set(back, UIElement.OpacityProperty, 0);
        To(back, UIElement.OpacityProperty, source is null ? 0 : 1, ms, EaseInOut);
        To(front, UIElement.OpacityProperty, 0, ms, EaseInOut, () =>
        {
            // Release the old bitmap, unless another crossfade has since brought this image forward.
            if (Panel.GetZIndex(front) == 0 && front.Opacity < 0.01) front.Source = null;
        });
        return back;
    }

    public static void ColorTo(SolidColorBrush brush, Color to, int ms)
    {
        if (brush.IsFrozen) return;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EaseInOut }, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>A static, tileable star field. Drawn once; no animation; ties the PC display to the Mac's Starry Night theme.</summary>
    public static Brush CreateStarField(int seed, double tile = 220, int stars = 26)
    {
        var rng = new Random(seed);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, tile, tile));
            for (var i = 0; i < stars; i++)
            {
                var r = rng.NextDouble() < 0.12 ? 1.25 : 0.55 + rng.NextDouble() * 0.45;
                var a = (byte)(70 + rng.Next(150));
                var brush = new SolidColorBrush(Color.FromArgb(a, 235, 240, 255));
                brush.Freeze();
                dc.DrawEllipse(brush, null, new Point(rng.NextDouble() * tile, rng.NextDouble() * tile), r, r);
            }
        }
        group.Freeze();
        var db = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        db.Freeze();
        return db;
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
