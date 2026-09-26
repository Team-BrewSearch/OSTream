using Avalonia;
using Avalonia.Controls;
using System;
namespace OSTream.Frontend.Controls;

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
    public static bool GetFlex(Control c)
    {
        return c.GetValue(FlexProperty);
    }
    public static void SetFlex(Control c, bool value)
    {
        _ = c.SetValue(FlexProperty, value);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        double fixedWidth = 0, height = 0;
        int visible = 0, flex = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }
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
        double gaps = Spacing * Math.Max(0, visible - 1);
        double remaining = Math.Max(0, (double.IsInfinity(availableSize.Width) ? fixedWidth : availableSize.Width) - fixedWidth - gaps);
        foreach (Control child in Children)
        {
            if (!child.IsVisible || !GetFlex(child))
            {
                continue;
            }
            child.Measure(new Size(flex > 0 ? remaining / flex : 0, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? fixedWidth + gaps : availableSize.Width, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double fixedWidth = 0;
        int visible = 0, flex = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }
            visible++;
            if (GetFlex(child))
            {
                flex++;
            }
            else
            {
                fixedWidth += child.DesiredSize.Width;
            }
        }
        double remaining = Math.Max(0, finalSize.Width - fixedWidth - (Spacing * Math.Max(0, visible - 1)));
        double x = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }
            double width = GetFlex(child) ? remaining / flex : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }
        return finalSize;
    }
}