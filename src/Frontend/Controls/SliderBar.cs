using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Globalization;

namespace OSTream.Frontend.Controls;

public sealed class SliderBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(Value));
    public static readonly StyledProperty<double> BufferedProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(Buffered));
    public static readonly StyledProperty<double> TrackHeightProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(TrackHeight), 5);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<SliderBar, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> BufferedBrushProperty = AvaloniaProperty.Register<SliderBar, IBrush?>(nameof(BufferedBrush));
    public static readonly StyledProperty<IBrush?> FillBrushProperty = AvaloniaProperty.Register<SliderBar, IBrush?>(nameof(FillBrush));
    public static readonly StyledProperty<BoxShadows> FillShadowProperty = AvaloniaProperty.Register<SliderBar, BoxShadows>(nameof(FillShadow));
    public static readonly StyledProperty<BoxShadows> ThumbShadowProperty = AvaloniaProperty.Register<SliderBar, BoxShadows>(nameof(ThumbShadow));
    public static readonly StyledProperty<IBrush?> TipBackgroundProperty = AvaloniaProperty.Register<SliderBar, IBrush?>(nameof(TipBackground));
    public static readonly StyledProperty<IBrush?> TipForegroundProperty = AvaloniaProperty.Register<SliderBar, IBrush?>(nameof(TipForeground));
    public static readonly StyledProperty<BoxShadows> TipShadowProperty = AvaloniaProperty.Register<SliderBar, BoxShadows>(nameof(TipShadow));
    public static readonly StyledProperty<double> ThumbScaleProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(ThumbScale));
    public static readonly StyledProperty<double> StepProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(Step), 0.05);
    private double _hover = -1;
    private double _dragValue;
    static SliderBar()
    {
        FocusableProperty.OverrideDefaultValue<SliderBar>(true);
        AffectsRender<SliderBar>(ValueProperty, BufferedProperty, TrackHeightProperty, TrackBrushProperty, BufferedBrushProperty,
            FillBrushProperty, FillShadowProperty, ThumbShadowProperty, ThumbScaleProperty, TipBackgroundProperty, TipForegroundProperty);
    }
    public SliderBar()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        Transitions = [new DoubleTransition { Property = ThumbScaleProperty, Duration = Motion.Normal, Easing = Motion.Standard }];
    }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Buffered { get => GetValue(BufferedProperty); set => SetValue(BufferedProperty, value); }
    public double TrackHeight { get => GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? BufferedBrush { get => GetValue(BufferedBrushProperty); set => SetValue(BufferedBrushProperty, value); }
    public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public BoxShadows FillShadow { get => GetValue(FillShadowProperty); set => SetValue(FillShadowProperty, value); }
    public BoxShadows ThumbShadow { get => GetValue(ThumbShadowProperty); set => SetValue(ThumbShadowProperty, value); }
    public IBrush? TipBackground { get => GetValue(TipBackgroundProperty); set => SetValue(TipBackgroundProperty, value); }
    public IBrush? TipForeground { get => GetValue(TipForegroundProperty); set => SetValue(TipForegroundProperty, value); }
    public BoxShadows TipShadow { get => GetValue(TipShadowProperty); set => SetValue(TipShadowProperty, value); }
    public double ThumbScale { get => GetValue(ThumbScaleProperty); set => SetValue(ThumbScaleProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public Func<double, string>? TipText { get; set; }
    public bool IsDragging { get; private set; }
    public event Action<double>? Input;
    public event Action<double>? Commit;
    private double Shown => IsDragging ? _dragValue : Math.Clamp(Value, 0, 1);
    private double RatioAt(Point p)
    {
        return Bounds.Width > 0 ? Math.Clamp(p.X / Bounds.Width, 0, 1) : 0;
    }

    private void UpdateThumb()
    {
        ThumbScale = IsEffectivelyEnabled && (IsDragging || IsPointerOver || (IsFocused && IsKeyboardFocusWithin)) ? 1 : 0;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsEffectivelyEnabledProperty || change.Property == IsFocusedProperty)
        {
            if (!IsPointerOver && !IsDragging)
            {
                _hover = -1;
            }

            Cursor = new Cursor(IsEffectivelyEnabled ? StandardCursorType.Hand : StandardCursorType.Arrow);
            UpdateThumb();
            InvalidateVisual();
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        IsDragging = true;
        e.Pointer.Capture(this);
        _dragValue = _hover = RatioAt(e.GetPosition(this));
        UpdateThumb();
        InvalidateVisual();
        Input?.Invoke(_dragValue);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        _hover = RatioAt(e.GetPosition(this));
        if (IsDragging)
        {
            _dragValue = _hover;
            Input?.Invoke(_dragValue);
        }
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!IsDragging)
        {
            return;
        }

        IsDragging = false;
        double value = RatioAt(e.GetPosition(this));
        e.Pointer.Capture(null);
        SetCurrentValue(ValueProperty, value);
        UpdateThumb();
        Commit?.Invoke(value);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!IsDragging)
        {
            return;
        }

        IsDragging = false;
        _hover = -1;
        UpdateThumb();
        InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (IsDragging)
        {
            return;
        }

        _hover = -1;
        InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        double? next = e.Key switch
        {
            Key.Right or Key.Up => Value + Step,
            Key.Left or Key.Down => Value - Step,
            Key.PageUp => Value + 0.1,
            Key.PageDown => Value - 0.1,
            Key.Home => 0,
            Key.End => 1,
            _ => null,
        };
        if (next is not { } v)
        {
            return;
        }

        e.Handled = true;
        v = Math.Clamp(v, 0, 1);
        SetCurrentValue(ValueProperty, v);
        Commit?.Invoke(v);
    }
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = TrackHeight;
        double y = (Bounds.Height - h) / 2;
        double radius = (h / 2) + 0.5;
        context.DrawRectangle(TrackBrush, null, new RoundedRect(new Rect(0, y, w, h), radius));
        double buffered = Math.Clamp(Buffered, 0, 1) * w;
        if (buffered > 0 && BufferedBrush is not null)
        {
            context.DrawRectangle(BufferedBrush, null, new RoundedRect(new Rect(0, y, buffered, h), radius));
        }

        double fill = Shown * w;
        if (fill > 0)
        {
            context.DrawRectangle(FillBrush, null, new RoundedRect(new Rect(0, y, fill, h), radius), FillShadow);
        }

        if (ThumbScale > 0.01 && IsEffectivelyEnabled)
        {
            double r = 6 * ThumbScale;
            Point center = new(fill, Bounds.Height / 2);
            context.DrawRectangle(Brushes.White, null, new RoundedRect(new Rect(center.X - r, center.Y - r, 2 * r, 2 * r), r), ThumbShadow);
        }
        if (TipText is { } tip && _hover >= 0 && IsEffectivelyEnabled)
        {
            FormattedText text = new(tip(_hover), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface((Application.Current?.FindResource("MonoFont") as FontFamily) ?? FontFamily.Default), 10.5, TipForeground);
            Rect box = new((_hover * w) - ((text.Width + 12) / 2), Bounds.Height - 18 - (text.Height + 6), text.Width + 12, text.Height + 6);
            context.DrawRectangle(TipBackground, null, new RoundedRect(box, 5), TipShadow);
            context.DrawText(text, new Point(box.X + 6, box.Y + 3));
        }
    }
}