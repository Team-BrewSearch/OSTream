using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using System;
namespace OSTream.Frontend.Controls;

public sealed class ProgressFill : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ProgressFill, double>(nameof(Value));
    public static readonly StyledProperty<double> ShownProperty = AvaloniaProperty.Register<ProgressFill, double>(nameof(Shown));
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<ProgressFill, IBrush?>(nameof(Track));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<ProgressFill, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<BoxShadows> FillShadowProperty = AvaloniaProperty.Register<ProgressFill, BoxShadows>(nameof(FillShadow));
    public static readonly StyledProperty<bool> IndeterminateProperty = AvaloniaProperty.Register<ProgressFill, bool>(nameof(Indeterminate));
    public static readonly StyledProperty<TimeSpan> DurationProperty = AvaloniaProperty.Register<ProgressFill, TimeSpan>(nameof(Duration), TimeSpan.FromMilliseconds(280));
    private TimeSpan _start = TimeSpan.MinValue;
    private double _time;
    private bool _scheduled;
    static ProgressFill()
    {
        AffectsRender<ProgressFill>(ShownProperty, TrackProperty, FillProperty, FillShadowProperty, IndeterminateProperty);
    }
    public ProgressFill()
    {
        UpdateTransition();
        LayoutUpdated += (_, _) => Schedule();
    }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Shown { get => GetValue(ShownProperty); set => SetValue(ShownProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public BoxShadows FillShadow { get => GetValue(FillShadowProperty); set => SetValue(FillShadowProperty, value); }
    public bool Indeterminate { get => GetValue(IndeterminateProperty); set => SetValue(IndeterminateProperty, value); }
    public TimeSpan Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    protected override Size MeasureOverride(Size availableSize)
    {
        return new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 0);
    }
    private void UpdateTransition()
    {
        Transitions = Duration > TimeSpan.Zero
            ? [new DoubleTransition { Property = ShownProperty, Duration = Duration, Easing = Duration.TotalMilliseconds >= 280 ? Motion.Standard : new Avalonia.Animation.Easings.LinearEasing() }]
            : null;
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
        {
            Shown = Math.Clamp(Value, 0, 1);
        }
        else if (change.Property == DurationProperty)
        {
            UpdateTransition();
        }
        else if (change.Property == IndeterminateProperty)
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
        if (_scheduled || !Indeterminate || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        _scheduled = true;
        top.RequestAnimationFrame(now =>
        {
            _scheduled = false;
            if (_start == TimeSpan.MinValue)
            {
                _start = now;
            }
            _time = (now - _start).TotalSeconds;
            InvalidateVisual();
            Schedule();
        });
    }
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        double radius = h / 2;
        RoundedRect rect = new(new Rect(0, 0, w, h), radius);
        context.DrawRectangle(Track, null, rect);
        if (Indeterminate)
        {
            double t = Motion.Standard.Ease(_time % 1.1 / 1.1);
            double segment = w * 0.4;
            double x = -segment + (t * 3.5 * segment);
            using (context.PushClip(rect))
            {
                context.DrawRectangle(Fill, null, new RoundedRect(new Rect(x, 0, segment, h), radius));
            }
            return;
        }
        double fill = Math.Clamp(Shown, 0, 1) * w;
        if (fill > 0.5)
        {
            context.DrawRectangle(Fill, null, new RoundedRect(new Rect(0, 0, fill, h), radius), FillShadow);
        }
    }
}