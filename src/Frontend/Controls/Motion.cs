using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
namespace OSTream.Frontend.Controls;

public static class Motion
{
    public static readonly Easing Standard = new SplineEasing(0.4, 0.2, 0.2, 1);
    public static readonly Easing Out = new SplineEasing(0.16, 1, 0.3, 1);
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(280);
}
public sealed class BrushFadeTransition : InterpolatingTransitionBase<IBrush?>
{
    protected override IBrush? Interpolate(double progress, IBrush? from, IBrush? to)
    {
        return progress >= 1 || !TryColor(from, out Color a) || !TryColor(to, out Color b)
            ? to
            : new ImmutableSolidColorBrush(Blend(a, b, progress));
    }
    private static bool TryColor(IBrush? brush, out Color color)
    {
        switch (brush)
        {
            case null:
                color = default;
                return true;
            case ISolidColorBrush solid:
                Color c = solid.Color;
                color = Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(solid.Opacity, 0, 1)), c.R, c.G, c.B);
                return true;
            default:
                color = default;
                return false;
        }
    }
    private static Color Blend(Color a, Color b, double t)
    {
        double fromAlpha = a.A / 255.0, toAlpha = b.A / 255.0;
        double alpha = fromAlpha + ((toAlpha - fromAlpha) * t);
        if (alpha <= 0)
        {
            return default;
        }

        byte Mix(byte from, byte to)
        {
            return (byte)Math.Clamp(Math.Round(((from * fromAlpha) + (((to * toAlpha) - (from * fromAlpha)) * t)) / alpha), 0, 255);
        }

        return Color.FromArgb((byte)Math.Round(alpha * 255), Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }
}