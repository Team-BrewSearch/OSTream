using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
namespace OSTream.Frontend.Controls;

public sealed class IconText : StackPanel
{
    public static readonly StyledProperty<IconKind> KindProperty = AvaloniaProperty.Register<IconText, IconKind>(nameof(Kind));
    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<IconText, double>(nameof(IconSize), 13);
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<IconText, string?>(nameof(Text));
    public static readonly StyledProperty<string?> SuffixProperty = AvaloniaProperty.Register<IconText, string?>(nameof(Suffix));
    private readonly Icon _icon = new() { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _suffix = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" }, IsVisible = false };
    static IconText()
    {
        OrientationProperty.OverrideDefaultValue<IconText>(Orientation.Horizontal);
        SpacingProperty.OverrideDefaultValue<IconText>(7);
    }
    public IconText()
    {
        Children.Add(_icon);
        Children.Add(_text);
        Children.Add(_suffix);
        _icon.Size = IconSize;
    }
    public IconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double IconSize { get => GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Suffix { get => GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public override string ToString()
    {
        return string.IsNullOrEmpty(Suffix) ? Text ?? "" : $"{Text} {Suffix}";
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
        {
            _icon.Kind = Kind;
            _icon.IsVisible = Kind != IconKind.None;
        }
        else if (change.Property == IconSizeProperty)
        {
            _icon.Size = IconSize;
        }
        else if (change.Property == TextProperty)
        {
            _text.Text = Text;
            _text.IsVisible = !string.IsNullOrEmpty(Text);
        }
        else if (change.Property == SuffixProperty)
        {
            _suffix.Text = Suffix;
            _suffix.IsVisible = !string.IsNullOrEmpty(Suffix);
        }
    }
}
public sealed class StateBlock : ContentControl
{
    public static readonly StyledProperty<IconKind> IconProperty = AvaloniaProperty.Register<StateBlock, IconKind>(nameof(Icon), IconKind.Info);
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<StateBlock, string?>(nameof(Title));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<StateBlock, string?>(nameof(Text));
    public IconKind Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
}
public static class Entrance
{
    public static readonly AttachedProperty<string?> KindProperty = AvaloniaProperty.RegisterAttached<Visual, string?>("Kind", typeof(Entrance));
    public static readonly AttachedProperty<int> DelayProperty = AvaloniaProperty.RegisterAttached<Visual, int>("Delay", typeof(Entrance));
    public static string? GetKind(Visual v)
    {
        return v.GetValue(KindProperty);
    }
    public static void SetKind(Visual v, string? value)
    {
        _ = v.SetValue(KindProperty, value);
    }
    public static int GetDelay(Visual v)
    {
        return v.GetValue(DelayProperty);
    }
    public static void SetDelay(Visual v, int value)
    {
        _ = v.SetValue(DelayProperty, value);
    }
    static Entrance()
    {
        _ = KindProperty.Changed.AddClassHandler<Visual>((v, _) =>
        {
            v.AttachedToVisualTree -= Run;
            if (GetKind(v) is not null)
            {
                v.AttachedToVisualTree += Run;
            }
        });
    }
    private static void Run(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Visual v || GetKind(v) is not { } kind)
        {
            return;
        }
        Play(v, kind, GetDelay(v));
    }
    public static void Play(Visual v, string kind, int delayMs = 0)
    {
        if (TopLevel.GetTopLevel(v) is not { } top)
        {
            return;
        }
        bool pop = kind == "pop-in";
        double duration = (pop ? Motion.Normal : Motion.Slow).TotalMilliseconds;
        TranslateTransform translate = new(0, pop ? -4 : 6);
        ScaleTransform scale = new(pop ? 0.96 : 1, pop ? 0.96 : 1);
        v.RenderTransform = pop ? new TransformGroup { Children = { scale, translate } } : translate;
        v.Opacity = 0;
        TimeSpan? start = null;
        void Frame(TimeSpan now)
        {
            start ??= now;
            double elapsed = (now - start.Value).TotalMilliseconds - delayMs;
            if (elapsed < 0)
            {
                top.RequestAnimationFrame(Frame);
                return;
            }
            double t = Math.Min(1, elapsed / duration);
            double eased = Motion.Out.Ease(t);
            v.Opacity = eased;
            translate.Y = (pop ? -4 : 6) * (1 - eased);
            if (pop)
            {
                scale.ScaleX = scale.ScaleY = 0.96 + (0.04 * eased);
            }
            if (t < 1)
            {
                top.RequestAnimationFrame(Frame);
                return;
            }
            v.ClearValue(Visual.OpacityProperty);
            v.ClearValue(Visual.RenderTransformProperty);
        }
        top.RequestAnimationFrame(Frame);
    }
}
public sealed class DashedBorder : Decorator
{
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<DashedBorder, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<DashedBorder>();
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner<DashedBorder>();
    private static readonly DashStyle Dashes = new([3, 3], 0);
    static DashedBorder()
    {
        AffectsRender<DashedBorder>(StrokeProperty, BackgroundProperty, CornerRadiusProperty);
    }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public override void Render(DrawingContext context)
    {
        Rect rect = new Rect(Bounds.Size).Deflate(0.5);
        RoundedRect rounded = new(rect, CornerRadius);
        context.DrawRectangle(Background, Stroke is null ? null : new Pen(Stroke, 1, Dashes), rounded);
    }
}
public sealed class PreviewBars : Control
{
    public static readonly StyledProperty<bool> DarkProperty = AvaloniaProperty.Register<PreviewBars, bool>(nameof(Dark));
    private static readonly IBrush LightBar = new SolidColorBrush(Color.Parse("#d7d7e0"));
    private static readonly IBrush LightAccent = new SolidColorBrush(Color.Parse("#7c3aed"));
    private static readonly IBrush DarkBar = new SolidColorBrush(Color.Parse("#2e2e3a"));
    private static readonly IBrush DarkAccent = new SolidColorBrush(Color.Parse("#a78bfa"));
    static PreviewBars()
    {
        AffectsRender<PreviewBars>(DarkProperty);
    }
    public bool Dark { get => GetValue(DarkProperty); set => SetValue(DarkProperty, value); }
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        IBrush bar = Dark ? DarkBar : LightBar;
        context.DrawRectangle(bar, null, new RoundedRect(new Rect(0, 0, w * 0.55, 5), 3));
        using (context.PushOpacity(0.6))
        {
            context.DrawRectangle(bar, null, new RoundedRect(new Rect(0, 9, w * 0.8, 5), 3));
        }
        context.DrawRectangle(Dark ? DarkAccent : LightAccent, null, new RoundedRect(new Rect(0, Bounds.Height - 7, w * 0.36, 7), 3));
    }
}
public sealed class DiagonalClip : Decorator
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = base.ArrangeOverride(finalSize);
        StreamGeometry geometry = new();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(new Point(size.Width, 0), true);
            g.LineTo(new Point(size.Width, size.Height));
            g.LineTo(new Point(0, size.Height));
            g.EndFigure(true);
        }
        Clip = geometry;
        return size;
    }
}
public sealed class Square : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double side = double.IsInfinity(availableSize.Width) ? (Child?.DesiredSize.Width ?? 0) : availableSize.Width;
        Child?.Measure(new Size(side, side));
        return new Size(side, side);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double side = finalSize.Width;
        Child?.Arrange(new Rect(0, 0, side, side));
        return new Size(side, side);
    }
}
public sealed class FractionBox : Decorator
{
    public static readonly StyledProperty<double> FractionProperty = AvaloniaProperty.Register<FractionBox, double>(nameof(Fraction), 1);
    public static readonly StyledProperty<double> MaxChildWidthProperty = AvaloniaProperty.Register<FractionBox, double>(nameof(MaxChildWidth), double.PositiveInfinity);
    static FractionBox()
    {
        AffectsMeasure<FractionBox>(FractionProperty, MaxChildWidthProperty);
    }
    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double MaxChildWidth { get => GetValue(MaxChildWidthProperty); set => SetValue(MaxChildWidthProperty, value); }
    private double ChildWidth(double available)
    {
        return Math.Min(MaxChildWidth, double.IsInfinity(available) ? MaxChildWidth : available * Fraction);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = ChildWidth(availableSize.Width);
        Child?.Measure(new Size(double.IsInfinity(width) ? availableSize.Width : width, availableSize.Height));
        return new Size(double.IsInfinity(availableSize.Width) ? Child?.DesiredSize.Width ?? 0 : availableSize.Width, Child?.DesiredSize.Height ?? 0);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, ChildWidth(finalSize.Width), finalSize.Height));
        return finalSize;
    }
}
public static class QuietFocus
{
    public static void Move(InputElement target)
    {
        StyledElement? current = TopLevel.GetTopLevel(target)?.FocusManager?.GetFocusedElement() as StyledElement;
        _ = target.Focus(current?.Classes.Contains(":focus-visible") == true ? NavigationMethod.Tab : NavigationMethod.Pointer);
    }
}
