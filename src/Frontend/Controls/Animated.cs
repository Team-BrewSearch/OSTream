using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
namespace OSTream.Frontend.Controls;

public abstract class AnimatedControl : Control
{
    public static readonly StyledProperty<bool> AnimateProperty = AvaloniaProperty.Register<AnimatedControl, bool>(nameof(Animate), true);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = Avalonia.Controls.Documents.TextElement.ForegroundProperty.AddOwner<AnimatedControl>();
    private TimeSpan _start = TimeSpan.MinValue;
    private bool _scheduled;
    static AnimatedControl()
    {
        AffectsRender<AnimatedControl>(ForegroundProperty, AnimateProperty);
    }
    protected AnimatedControl()
    {
        LayoutUpdated += (_, _) => Schedule();
    }
    public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    protected double Time { get; private set; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AnimateProperty)
        {
            Schedule();
        }
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Schedule();
    }
    private void Schedule()
    {
        if (_scheduled || !Animate || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        _scheduled = true;
        top.RequestAnimationFrame(OnFrame);
    }
    private void OnFrame(TimeSpan now)
    {
        _scheduled = false;
        if (TopLevel.GetTopLevel(this) is null)
        {
            return;
        }
        if (_start == TimeSpan.MinValue)
        {
            _start = now;
        }
        if (Animate)
        {
            Time = (now - _start).TotalSeconds;
        }
        InvalidateVisual();
        Schedule();
    }
}
public sealed class Spinner : AnimatedControl
{
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Spinner, double>(nameof(Size), 16);
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    protected override Size MeasureOverride(Size availableSize)
    {
        return new(Size, Size);
    }
    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
        {
            return;
        }
        double angle = Time / 0.7 * 2 * Math.PI;
        double r = (Size - 2) / 2;
        Point c = new(Size / 2, Size / 2);
        double start = angle + (Math.PI / 4);
        double end = start + (Math.PI * 1.5);
        StreamGeometry geometry = new();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(c + new Point(Math.Cos(start) * r, Math.Sin(start) * r), false);
            g.ArcTo(c + new Point(Math.Cos(end) * r, Math.Sin(end) * r), new Size(r, r), 0, true, SweepDirection.Clockwise);
        }
        context.DrawGeometry(null, new Pen(brush, 2, lineCap: PenLineCap.Flat), geometry);
    }
}
public sealed class EqBars : AnimatedControl
{
    public static readonly StyledProperty<double> PeriodProperty = AvaloniaProperty.Register<EqBars, double>(nameof(Period), 0.8);
    public double[] Heights { get; set; } = [0.6, 1, 0.45];
    public double[] Delays { get; set; } = [-0.3, -0.1, -0.5];
    public double Period { get => GetValue(PeriodProperty); set => SetValue(PeriodProperty, value); }
    protected override Size MeasureOverride(Size availableSize)
    {
        return new(10, double.IsInfinity(availableSize.Height) ? 12 : availableSize.Height);
    }
    private static readonly Avalonia.Animation.Easings.SplineEasing EaseInOut = new(0.42, 0, 0.58, 1);
    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
        {
            return;
        }
        double h = Bounds.Height;
        for (int i = 0; i < 3; i++)
        {
            double t = (((Time - Delays[i]) / Period % 1) + 1) % 1;
            double half = t < 0.5 ? t * 2 : (1 - t) * 2;
            double scale = 0.4 + (0.6 * EaseInOut.Ease(half));
            double barHeight = h * Heights[i] * scale;
            context.DrawRectangle(brush, null, new RoundedRect(new Rect(i * 4, h - barHeight, 2, barHeight), 1));
        }
    }
}
public sealed class Skeleton : AnimatedControl
{
    public static readonly StyledProperty<IBrush?> BaseBrushProperty = AvaloniaProperty.Register<Skeleton, IBrush?>(nameof(BaseBrush));
    public static readonly StyledProperty<IBrush?> ShineBrushProperty = AvaloniaProperty.Register<Skeleton, IBrush?>(nameof(ShineBrush));
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = AvaloniaProperty.Register<Skeleton, CornerRadius>(nameof(CornerRadius), new CornerRadius(6));
    public IBrush? BaseBrush { get => GetValue(BaseBrushProperty); set => SetValue(BaseBrushProperty, value); }
    public IBrush? ShineBrush { get => GetValue(ShineBrushProperty); set => SetValue(ShineBrushProperty, value); }
    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public override void Render(DrawingContext context)
    {
        RoundedRect rect = new(new Rect(Bounds.Size), CornerRadius);
        context.DrawRectangle(BaseBrush, null, rect);
        if (ShineBrush is null)
        {
            return;
        }
        double x = -240 + (Time % 1.3 / 1.3 * 480);
        using (context.PushClip(rect))
        using (context.PushTransform(Matrix.CreateTranslation(x, 0)))
        {
            context.DrawRectangle(ShineBrush, null, new Rect(0, 0, 240, Bounds.Height));
        }
    }
}