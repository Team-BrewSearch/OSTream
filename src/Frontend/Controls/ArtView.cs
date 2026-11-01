using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OSTream.Core;
using OSTream.Frontend.Services;
using System;
using System.Threading.Tasks;

namespace OSTream.Frontend.Controls;

public class ArtView : Border
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<ArtView, string?>(nameof(Source));
    public static readonly StyledProperty<string?> SeedProperty = AvaloniaProperty.Register<ArtView, string?>(nameof(Seed));
    public static readonly StyledProperty<double> GlyphSizeProperty = AvaloniaProperty.Register<ArtView, double>(nameof(GlyphSize), 22);
    public static readonly StyledProperty<bool> SharpenProperty = AvaloniaProperty.Register<ArtView, bool>(nameof(Sharpen));
    public static ImageLoader? Loader { get; set; }
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
    private readonly Icon _glyph = new() { Kind = IconKind.Music, StrokeWidth = 1.8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private int _request;
    private string? _shown;
    private int _decodedWidth;
    public ArtView()
    {
        ClipToBounds = true;
        _glyph.Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
        _image.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = Motion.Standard }];
        Child = new Panel { Children = { _glyph, _image } };
        UpdateBackground();
    }
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string? Seed { get => GetValue(SeedProperty); set => SetValue(SeedProperty, value); }
    public double GlyphSize { get => GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public bool Sharpen { get => GetValue(SharpenProperty); set => SetValue(SharpenProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeedProperty)
        {
            UpdateBackground();
        }
        else if (change.Property == GlyphSizeProperty)
        {
            _glyph.Size = GlyphSize;
        }
        else if (change.Property == SourceProperty || change.Property == SharpenProperty)
        {
            if (change.Property == SourceProperty && Seed is null)
            {
                UpdateBackground();
            }
            _ = Reload();
        }
        else if (change.Property == BoundsProperty && !string.IsNullOrEmpty(Source) && (_shown is null || DecodeWidth > _decodedWidth * 1.25))
        {
            _ = Reload();
        }
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _glyph.Size = GlyphSize;
        _ = Reload();
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _request++;
    }
    private void UpdateBackground()
    {
        Background = Application.Current?.FindResource($"Art{Formatting.HashIndex(Seed ?? Source)}") as IBrush;
    }

    private int DecodeWidth => (int)Math.Ceiling(Math.Max(Bounds.Width, 48) * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
    private async Task Reload()
    {
        int request = ++_request;
        string? source = Source;
        if (string.IsNullOrEmpty(source) || Loader is null)
        {
            Show(null, animate: false);
            _shown = null;
            return;
        }
        if (Bounds.Width <= 0 || !this.IsAttachedToVisualTree())
        {
            return;
        }

        string primary = Sharpen ? Formatting.LargerThumb(source) : source;
        int width = DecodeWidth;
        _decodedWidth = width;
        if (Loader.Peek(primary, width) is { } cached)
        {
            _shown = source;
            Show(cached, animate: false);
            return;
        }
        if (_shown != source)
        {
            Show(null, animate: false);
        }

        Bitmap? bitmap = await Loader.LoadAsync(primary, width);
        if (bitmap is null && primary != source)
        {
            bitmap = await Loader.LoadAsync(source, width);
        }

        if (request != _request || (bitmap is null && _shown == source))
        {
            return;
        }

        bool fresh = _shown != source;
        _shown = source;
        Show(bitmap, animate: fresh);
    }
    private void Show(Bitmap? bitmap, bool animate)
    {
        _image.Source = bitmap;
        _glyph.IsVisible = bitmap is null;
        if (!animate)
        {
            Transitions? transitions = _image.Transitions;
            _image.Transitions = null;
            _image.Opacity = bitmap is null ? 0 : 1;
            _image.Transitions = transitions;
        }
        else
        {
            _image.Opacity = bitmap is null ? 0 : 1;
        }
    }
}
