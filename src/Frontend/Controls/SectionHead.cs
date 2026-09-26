using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
namespace OSTream.Controls;
public sealed class SectionHead : Panel
{
    public static readonly StyledProperty<double> ColumnGapProperty = AvaloniaProperty.Register<SectionHead, double>(nameof(ColumnGap), 14);
    public static readonly StyledProperty<double> RowGapProperty = AvaloniaProperty.Register<SectionHead, double>(nameof(RowGap), 10);
    public static readonly StyledProperty<bool> AlignBottomProperty = AvaloniaProperty.Register<SectionHead, bool>(nameof(AlignBottom));
    public static readonly StyledProperty<bool> GrowProperty = AvaloniaProperty.Register<SectionHead, bool>(nameof(Grow));
    static SectionHead() => AffectsMeasure<SectionHead>(ColumnGapProperty, RowGapProperty, AlignBottomProperty, GrowProperty);
    public double ColumnGap { get => GetValue(ColumnGapProperty); set => SetValue(ColumnGapProperty, value); }
    public double RowGap { get => GetValue(RowGapProperty); set => SetValue(RowGapProperty, value); }
    public bool AlignBottom { get => GetValue(AlignBottomProperty); set => SetValue(AlignBottomProperty, value); }
    public bool Grow { get => GetValue(GrowProperty); set => SetValue(GrowProperty, value); }
    Control[] Visible => Children.Where(c => c.IsVisible).ToArray();
    double RestWidth(Control[] rest) => rest.Sum(c => c.DesiredSize.Width) + ColumnGap * Math.Max(0, rest.Length - 1);
    (bool OneLine, double FirstWidth) Row(Control first, Control[] rest, double width)
    {
        var restWidth = rest.Length > 0 ? RestWidth(rest) + ColumnGap : 0;
        var basis = Grow ? first.MinWidth : first.DesiredSize.Width;
        var oneLine = double.IsInfinity(width) || basis + restWidth <= width;
        return (oneLine, Grow ? width - restWidth : Math.Min(first.DesiredSize.Width, width - restWidth));
    }
    Size Layout(double width, bool arrange)
    {
        var items = Visible;
        if (items.Length == 0) return default;
        var first = items[0];
        var rest = items.Skip(1).ToArray();
        var restWidth = RestWidth(rest);
        var (oneLine, firstWidth) = Row(first, rest, width);
        if (oneLine)
        {
            var height = items.Max(c => c.DesiredSize.Height);
            if (arrange)
            {
                Place(first, 0, 0, height, firstWidth);
                var x = width - restWidth;
                foreach (var c in rest)
                {
                    Place(c, x, 0, height, c.DesiredSize.Width);
                    x += c.DesiredSize.Width + ColumnGap;
                }
            }
            return new Size(double.IsInfinity(width) ? first.DesiredSize.Width + (rest.Length > 0 ? ColumnGap + restWidth : 0) : width, height);
        }
        var y = first.DesiredSize.Height;
        if (arrange) Place(first, 0, 0, first.DesiredSize.Height, Grow ? width : Math.Min(first.DesiredSize.Width, width));
        double lineX = 0, lineHeight = 0;
        y += RowGap;
        var lineStart = 0;
        for (var i = 0; i < rest.Length; i++)
        {
            var c = rest[i];
            if (lineX > 0 && lineX + c.DesiredSize.Width > width)
            {
                if (arrange) PlaceLine(rest, lineStart, i, y, lineHeight);
                y += lineHeight + RowGap;
                lineX = 0;
                lineHeight = 0;
                lineStart = i;
            }
            lineX += c.DesiredSize.Width + ColumnGap;
            lineHeight = Math.Max(lineHeight, c.DesiredSize.Height);
        }
        if (arrange) PlaceLine(rest, lineStart, rest.Length, y, lineHeight);
        return new Size(width, y + lineHeight);
    }
    void PlaceLine(Control[] items, int from, int to, double y, double height)
    {
        double x = 0;
        for (var i = from; i < to; i++)
        {
            Place(items[i], x, y, height, items[i].DesiredSize.Width);
            x += items[i].DesiredSize.Width + ColumnGap;
        }
    }
    void Place(Control c, double x, double y, double rowHeight, double width)
    {
        var h = c.DesiredSize.Height;
        var top = AlignBottom ? y + rowHeight - h : y + (rowHeight - h) / 2;
        c.Arrange(new Rect(x, top, Math.Max(0, width), h));
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        if (Grow && Visible is [var first, .. var rest] && !double.IsInfinity(availableSize.Width))
        {
            var (oneLine, firstWidth) = Row(first, rest, availableSize.Width);
            first.Measure(new Size(oneLine ? Math.Max(0, firstWidth) : availableSize.Width, double.PositiveInfinity));
        }
        return Layout(availableSize.Width, arrange: false);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, arrange: true);
        return finalSize;
    }
}