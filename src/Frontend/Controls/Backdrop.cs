using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
namespace OSTream.Controls;
public sealed class Backdrop : Control
{
    public static readonly AttachedProperty<bool> IsSourceProperty =
        AvaloniaProperty.RegisterAttached<Backdrop, Visual, bool>("IsSource");
    public static readonly StyledProperty<IBrush?> TintProperty = AvaloniaProperty.Register<Backdrop, IBrush?>(nameof(Tint));
    public static readonly StyledProperty<IBrush?> UnderProperty = AvaloniaProperty.Register<Backdrop, IBrush?>(nameof(Under));
    public static readonly StyledProperty<double> BlurProperty = AvaloniaProperty.Register<Backdrop, double>(nameof(Blur), 24);
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner<Backdrop>();
    static readonly List<WeakReference<Visual>> Sources = [];
    readonly Layer _layer;
    Visual? _source;
    static Backdrop()
    {
        AffectsArrange<Backdrop>(BlurProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<Backdrop>(false);
        IsSourceProperty.Changed.AddClassHandler<Visual>((visual, e) =>
        {
            Sources.RemoveAll(w => !w.TryGetTarget(out var v) || v == visual);
            if (e.NewValue is true) Sources.Add(new WeakReference<Visual>(visual));
        });
    }
    public Backdrop()
    {
        _layer = new Layer(this);
        VisualChildren.Add(_layer);
        UpdateEffect();
    }
    public static bool GetIsSource(Visual visual) => visual.GetValue(IsSourceProperty);
    public static void SetIsSource(Visual visual, bool value) => visual.SetValue(IsSourceProperty, value);
    public IBrush? Tint { get => GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public IBrush? Under { get => GetValue(UnderProperty); set => SetValue(UnderProperty, value); }
    public double Blur { get => GetValue(BlurProperty); set => SetValue(BlurProperty, value); }
    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    double Pad => Math.Ceiling(Math.Max(0, Blur) * 3);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BlurProperty) UpdateEffect();
        else if (change.Property == TintProperty || change.Property == UnderProperty) _layer.InvalidateVisual();
        else if (change.Property == BoundsProperty || change.Property == CornerRadiusProperty)
            Clip = new RectangleGeometry(new Rect(Bounds.Size), CornerRadius.TopLeft, CornerRadius.TopLeft);
    }
    void UpdateEffect()
    {
        _layer.Effect = Blur > 0.5 ? new BlurEffect { Radius = (Blur - 0.5) / 0.57735 } : null;
        _layer.InvalidateVisual();
    }
    protected override Size MeasureOverride(Size availableSize) => default;
    protected override Size ArrangeOverride(Size finalSize)
    {
        _layer.Arrange(new Rect(finalSize).Inflate(Pad));
        return finalSize;
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var top = TopLevel.GetTopLevel(this);
        _source = Sources
            .Select(w => w.TryGetTarget(out var v) ? v : null)
            .OfType<Visual>()
            .Where(v => TopLevel.GetTopLevel(v) == top && !v.IsVisualAncestorOf(this))
            .OrderBy(v => v.GetVisualAncestors().Count())
            .FirstOrDefault();
        if (_source is Layoutable source) source.LayoutUpdated += OnSourceChanged;
        LayoutUpdated += OnSourceChanged;
        ActualThemeVariantChanged += OnSourceChanged;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_source is Layoutable source) source.LayoutUpdated -= OnSourceChanged;
        LayoutUpdated -= OnSourceChanged;
        ActualThemeVariantChanged -= OnSourceChanged;
        _source = null;
    }
    void OnSourceChanged(object? sender, EventArgs e)
    {
        if (IsEffectivelyVisible) _layer.InvalidateVisual();
    }
    sealed class Layer(Backdrop owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            var area = new Rect(Bounds.Size);
            if (owner.TryFindResource("Bg0", owner.ActualThemeVariant, out var bg) && bg is IBrush background)
                context.DrawRectangle(background, null, area);
            if (owner._source is { } source && owner.IsEffectivelyVisible)
            {
                context.DrawRectangle(new VisualBrush(source)
                {
                    SourceRect = new RelativeRect(area.Translate(LayoutOrigin(this) - LayoutOrigin(source)), RelativeUnit.Absolute),
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                }, null, area);
            }
            if (owner.Under is { } under) context.DrawRectangle(under, null, area);
            if (owner.Tint is { } tint) context.DrawRectangle(tint, null, area);
        }
        static Vector LayoutOrigin(Visual visual)
        {
            var at = default(Vector);
            for (Visual? v = visual; v is not null and not TopLevel; v = v.GetVisualParent()) at += v.Bounds.Position;
            return at;
        }
    }
}