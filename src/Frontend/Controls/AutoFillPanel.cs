using System;
using Avalonia;
using Avalonia.Controls;
namespace OSTream.Controls;
public sealed class AutoFillPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty = AvaloniaProperty.Register<AutoFillPanel, double>(nameof(MinItemWidth), 150);
    public static readonly StyledProperty<double> ColumnSpacingProperty = AvaloniaProperty.Register<AutoFillPanel, double>(nameof(ColumnSpacing), 16);
    public static readonly StyledProperty<double> RowSpacingProperty = AvaloniaProperty.Register<AutoFillPanel, double>(nameof(RowSpacing), 16);
    static AutoFillPanel() => AffectsMeasure<AutoFillPanel>(MinItemWidthProperty, ColumnSpacingProperty, RowSpacingProperty);
    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    (int Columns, double ItemWidth) Layout(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return (1, MinItemWidth);
        var columns = Math.Max(1, (int)Math.Floor((width + ColumnSpacing) / (MinItemWidth + ColumnSpacing)));
        return (columns, (width - ColumnSpacing * (columns - 1)) / columns);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, itemWidth) = Layout(availableSize.Width);
        double height = 0, rowHeight = 0;
        var visible = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++visible % columns == 0)
            {
                height += rowHeight + RowSpacing;
                rowHeight = 0;
            }
        }
        if (visible % columns != 0) height += rowHeight + RowSpacing;
        if (visible > 0) height -= RowSpacing;
        var width = double.IsInfinity(availableSize.Width) ? columns * itemWidth : availableSize.Width;
        return new Size(width, Math.Max(0, height));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Layout(finalSize.Width);
        var index = 0;
        double y = 0;
        var row = new Control[columns];
        var count = 0;
        void Flush()
        {
            double rowHeight = 0;
            for (var i = 0; i < count; i++) rowHeight = Math.Max(rowHeight, row[i].DesiredSize.Height);
            for (var i = 0; i < count; i++) row[i].Arrange(new Rect(i * (itemWidth + ColumnSpacing), y, itemWidth, rowHeight));
            y += rowHeight + RowSpacing;
            count = 0;
        }
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            row[count++] = child;
            index++;
            if (count == columns) Flush();
        }
        if (count > 0) Flush();
        return finalSize;
    }
}