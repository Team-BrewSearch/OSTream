using System;
using Avalonia;
using Avalonia.Controls;
namespace OSTream.Controls;
public sealed class BarLayout : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<BarLayout, double>(nameof(Spacing), 20);
    public static readonly AttachedProperty<bool> FlexProperty = AvaloniaProperty.RegisterAttached<BarLayout, Control, bool>("Flex");
    static BarLayout()
    {
        AffectsMeasure<BarLayout>(SpacingProperty);
        AffectsParentMeasure<BarLayout>(FlexProperty);
    }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public static bool GetFlex(Control c) => c.GetValue(FlexProperty);
    public static void SetFlex(Control c, bool value) => c.SetValue(FlexProperty, value);
    protected override Size MeasureOverride(Size availableSize)
    {
        double fixedWidth = 0, height = 0;
        int visible = 0, flex = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            visible++;
            if (GetFlex(child))
            {
                flex++;
                continue;
            }
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            fixedWidth += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var gaps = Spacing * Math.Max(0, visible - 1);
        var remaining = Math.Max(0, (double.IsInfinity(availableSize.Width) ? fixedWidth : availableSize.Width) - fixedWidth - gaps);
        foreach (var child in Children)
        {
            if (!child.IsVisible || !GetFlex(child)) continue;
            child.Measure(new Size(flex > 0 ? remaining / flex : 0, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? fixedWidth + gaps : availableSize.Width, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double fixedWidth = 0;
        int visible = 0, flex = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            visible++;
            if (GetFlex(child)) flex++;
            else fixedWidth += child.DesiredSize.Width;
        }
        var remaining = Math.Max(0, finalSize.Width - fixedWidth - Spacing * Math.Max(0, visible - 1));
        double x = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var width = GetFlex(child) ? remaining / flex : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }
        return finalSize;
    }
}