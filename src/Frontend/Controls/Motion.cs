using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Media.Immutable;
namespace OSTream.Controls;
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
        if (progress >= 1 || !TryColor(from, out var a) || !TryColor(to, out var b)) return to;
        return new ImmutableSolidColorBrush(Blend(a, b, progress));
    }
    static bool TryColor(IBrush? brush, out Color color)
    {
        switch (brush)
        {
            case null:
                color = default;
                return true;
            case ISolidColorBrush solid:
                var c = solid.Color;
                color = Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(solid.Opacity, 0, 1)), c.R, c.G, c.B);
                return true;
            default:
                color = default;
                return false;
        }
    }
    static Color Blend(Color a, Color b, double t)
    {
        double fromAlpha = a.A / 255.0, toAlpha = b.A / 255.0;
        var alpha = fromAlpha + (toAlpha - fromAlpha) * t;
        if (alpha <= 0) return default;
        byte Mix(byte from, byte to) =>
            (byte)Math.Clamp(Math.Round((from * fromAlpha + (to * toAlpha - from * fromAlpha) * t) / alpha), 0, 255);
        return Color.FromArgb((byte)Math.Round(alpha * 255), Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }
}