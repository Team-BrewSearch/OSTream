using Avalonia;
using Avalonia.Controls;
using System;
using System.Linq;
namespace OSTream.Frontend.Controls;

public sealed class SectionHead : Panel
{
    public static readonly StyledProperty<double> ColumnGapProperty = AvaloniaProperty.Register<SectionHead, double>(nameof(ColumnGap), 14);
    public static readonly StyledProperty<double> RowGapProperty = AvaloniaProperty.Register<SectionHead, double>(nameof(RowGap), 10);
    public static readonly StyledProperty<bool> AlignBottomProperty = AvaloniaProperty.Register<SectionHead, bool>(nameof(AlignBottom));
    public static readonly StyledProperty<bool> GrowProperty = AvaloniaProperty.Register<SectionHead, bool>(nameof(Grow));
    static SectionHead()
    {
        AffectsMeasure<SectionHead>(ColumnGapProperty, RowGapProperty, AlignBottomProperty, GrowProperty);
    }

    public double ColumnGap { get => GetValue(ColumnGapProperty); set => SetValue(ColumnGapProperty, value); }
    public double RowGap { get => GetValue(RowGapProperty); set => SetValue(RowGapProperty, value); }
    public bool AlignBottom { get => GetValue(AlignBottomProperty); set => SetValue(AlignBottomProperty, value); }
    public bool Grow { get => GetValue(GrowProperty); set => SetValue(GrowProperty, value); }
    private Control[] Visible => Children.Where(c => c.IsVisible).ToArray();
    private double RestWidth(Control[] rest)
    {
        return rest.Sum(c => c.DesiredSize.Width) + (ColumnGap * Math.Max(0, rest.Length - 1));
    }

    private (bool OneLine, double FirstWidth) Row(Control first, Control[] rest, double width)
    {
        double restWidth = rest.Length > 0 ? RestWidth(rest) + ColumnGap : 0;
        double basis = Grow ? first.MinWidth : first.DesiredSize.Width;
        bool oneLine = double.IsInfinity(width) || basis + restWidth <= width;
        return (oneLine, Grow ? width - restWidth : Math.Min(first.DesiredSize.Width, width - restWidth));
    }
    private Size Layout(double width, bool arrange)
    {
        Control[] items = Visible;
        if (items.Length == 0)
        {
            return default;
        }

        Control first = items[0];
        Control[] rest = items.Skip(1).ToArray();
        double restWidth = RestWidth(rest);
        (bool oneLine, double firstWidth) = Row(first, rest, width);
        if (oneLine)
        {
            double height = items.Max(c => c.DesiredSize.Height);
            if (arrange)
            {
                Place(first, 0, 0, height, firstWidth);
                double x = width - restWidth;
                foreach (Control? c in rest)
                {
                    Place(c, x, 0, height, c.DesiredSize.Width);
                    x += c.DesiredSize.Width + ColumnGap;
                }
            }
            return new Size(double.IsInfinity(width) ? first.DesiredSize.Width + (rest.Length > 0 ? ColumnGap + restWidth : 0) : width, height);
        }
        double y = first.DesiredSize.Height;
        if (arrange)
        {
            Place(first, 0, 0, first.DesiredSize.Height, Grow ? width : Math.Min(first.DesiredSize.Width, width));
        }

        double lineX = 0, lineHeight = 0;
        y += RowGap;
        int lineStart = 0;
        for (int i = 0; i < rest.Length; i++)
        {
            Control c = rest[i];
            if (lineX > 0 && lineX + c.DesiredSize.Width > width)
            {
                if (arrange)
                {
                    PlaceLine(rest, lineStart, i, y, lineHeight);
                }

                y += lineHeight + RowGap;
                lineX = 0;
                lineHeight = 0;
                lineStart = i;
            }
            lineX += c.DesiredSize.Width + ColumnGap;
            lineHeight = Math.Max(lineHeight, c.DesiredSize.Height);
        }
        if (arrange)
        {
            PlaceLine(rest, lineStart, rest.Length, y, lineHeight);
        }

        return new Size(width, y + lineHeight);
    }
    private void PlaceLine(Control[] items, int from, int to, double y, double height)
    {
        double x = 0;
        for (int i = from; i < to; i++)
        {
            Place(items[i], x, y, height, items[i].DesiredSize.Width);
            x += items[i].DesiredSize.Width + ColumnGap;
        }
    }
    private void Place(Control c, double x, double y, double rowHeight, double width)
    {
        double h = c.DesiredSize.Height;
        double top = AlignBottom ? y + rowHeight - h : y + ((rowHeight - h) / 2);
        c.Arrange(new Rect(x, top, Math.Max(0, width), h));
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        if (Grow && Visible is [var first, .. var rest] && !double.IsInfinity(availableSize.Width))
        {
            (bool oneLine, double firstWidth) = Row(first, rest, availableSize.Width);
            first.Measure(new Size(oneLine ? Math.Max(0, firstWidth) : availableSize.Width, double.PositiveInfinity));
        }
        return Layout(availableSize.Width, arrange: false);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        _ = Layout(finalSize.Width, arrange: true);
        return finalSize;
    }
}