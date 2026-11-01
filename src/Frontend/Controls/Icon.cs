using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.Generic;
using System.Globalization;
namespace OSTream.Frontend.Controls;

public enum IconKind
{
    None,
    Play, Pause, Prev, Next, Shuffle, Repeat, RepeatOne,
    Heart, HeartFill, Download, Check, CheckBold, More, X, XBold, Retry, Folder, External, Search,
    Queue, QueueAdd, PlayNext, Volume, VolumeLow, VolumeMute, Music, MusicBrand,
    Home, Downloads, Clock, Book, Plus, Trash, Edit, Up, Down, ChevronLeft, ChevronRight, ChevronDown,
    Back, Dice, Link, Alert, Info, WifiOff, Settings, Monitor, Sun, Moon,
}
public sealed class Icon : Control
{
    public static readonly StyledProperty<IconKind> KindProperty = AvaloniaProperty.Register<Icon, IconKind>(nameof(Kind));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Icon, double>(nameof(Size), 16);
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<Icon, double>(nameof(StrokeWidth), double.NaN);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Icon>();
    static Icon()
    {
        AffectsRender<Icon>(KindProperty, StrokeWidthProperty, ForegroundProperty);
        VerticalAlignmentProperty.OverrideDefaultValue<Icon>(VerticalAlignment.Center);
    }
    public IconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty)
        {
            InvalidateMeasure();
        }
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        return new(Size, Size);
    }

    public override void Render(DrawingContext context)
    {
        if (Kind == IconKind.None || Foreground is not { } brush || !Glyphs.TryGetValue(Kind, out Glyph? glyph))
        {
            return;
        }

        double scale = Size / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            if (glyph.Fill is not null)
            {
                context.DrawGeometry(brush, null, glyph.Fill);
            }

            if (glyph.Stroke is not null)
            {
                double width = double.IsNaN(StrokeWidth) ? glyph.Width : StrokeWidth;
                context.DrawGeometry(glyph.FillStroke ? brush : null, new Pen(brush, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), glyph.Stroke);
            }
        }
    }
    private sealed record Glyph(Geometry? Stroke, Geometry? Fill, double Width, bool FillStroke = false);
    private static StreamGeometry G(string data)
    {
        return StreamGeometry.Parse(data);
    }

    private static string Circle(double cx, double cy, double r)
    {
        return string.Create(CultureInfo.InvariantCulture, $"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0z");
    }

    private static Glyph S(string path, double width = 2)
    {
        return new(G(path), null, width);
    }

    private const string HeartPath = "M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l1 1L12 21l7.8-7.6 1-1a5.5 5.5 0 0 0 0-7.8z";
    private const string RepeatPath = "M17 2l4 4-4 4M3 11V9a3 3 0 0 1 3-3h15M7 22l-4-4 4-4M21 13v2a3 3 0 0 1-3 3H3";
    private const string SpeakerPath = "M11 5L6 9H2v6h4l5 4z";
    private static readonly Dictionary<IconKind, Glyph> Glyphs = new()
    {
        [IconKind.Play] = new(null, G("M7 4.5v15l13-7.5z"), 0),
        [IconKind.Pause] = new(null, G("M7 5h4v14H7zM13 5h4v14h-4z"), 0),
        [IconKind.Prev] = new(null, G("M6 5h2v14H6zM20 5v14L9 12z"), 0),
        [IconKind.Next] = new(null, G("M16 5h2v14h-2zM4 5v14l11-7z"), 0),
        [IconKind.More] = new(null, G(Circle(5, 12, 1.8) + Circle(12, 12, 1.8) + Circle(19, 12, 1.8)), 0),
        [IconKind.Shuffle] = S("M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5"),
        [IconKind.Repeat] = S(RepeatPath),
        [IconKind.RepeatOne] = S(RepeatPath + "M11 10h1v4"),
        [IconKind.Heart] = S(HeartPath),
        [IconKind.HeartFill] = new(G(HeartPath), null, 2, FillStroke: true),
        [IconKind.Download] = S("M12 3v12M6 11l6 6 6-6M4 21h16", 2.2),
        [IconKind.Check] = S("M20 6 9 17l-5-5", 2.4),
        [IconKind.CheckBold] = S("M20 6 9 17l-5-5", 3),
        [IconKind.X] = S("M18 6 6 18M6 6l12 12"),
        [IconKind.XBold] = S("M18 6 6 18M6 6l12 12", 2.4),
        [IconKind.Retry] = S("M21 12a9 9 0 1 1-2.6-6.4L21 8M21 3v5h-5"),
        [IconKind.Folder] = S("M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"),
        [IconKind.External] = S("M14 4h6v6M10 14 20 4M19 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h5"),
        [IconKind.Search] = S(Circle(11, 11, 7) + "M21 21l-4.3-4.3"),
        [IconKind.Queue] = new(G("M8 6H21M8 12H21M8 18H21"), G(Circle(4, 6, 1) + Circle(4, 12, 1) + Circle(4, 18, 1)), 2),
        [IconKind.QueueAdd] = S("M3 6h13M3 12h9M3 18h7M18 13v8M14 17h8"),
        [IconKind.PlayNext] = new(G("M3 6h13M3 12h9M3 18h9M16 14v6l5-3z"), G("M16 14v6l5-3z"), 2),
        [IconKind.Volume] = S(SpeakerPath + "M15.5 8.5a5 5 0 0 1 0 7M19 5a10 10 0 0 1 0 14"),
        [IconKind.VolumeLow] = S(SpeakerPath + "M15.5 8.5a5 5 0 0 1 0 7"),
        [IconKind.VolumeMute] = S(SpeakerPath + "M22 9l-6 6M16 9l6 6"),
        [IconKind.Music] = S("M9 18V5l12-2v13" + Circle(6, 18, 3) + Circle(18, 16, 3), 1.8),
        [IconKind.MusicBrand] = new(G("M9 18V5l12-2v13" + Circle(6, 18, 3) + Circle(18, 16, 3)), G(Circle(6, 18, 3) + Circle(18, 16, 3)), 2.2),
        [IconKind.Home] = S("M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2zM9 22V12h6v10"),
        [IconKind.Downloads] = S("M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3"),
        [IconKind.Clock] = S(Circle(12, 12, 9) + "M12 7v5l3 2"),
        [IconKind.Book] = S("M4 19.5A2.5 2.5 0 0 1 6.5 17H20M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"),
        [IconKind.Plus] = S("M12 5v14M5 12h14"),
        [IconKind.Trash] = S("M3 6h18M8 6V4h8v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"),
        [IconKind.Edit] = S("M12 20h9M16.5 3.5a2.1 2.1 0 1 1 3 3L7 19l-4 1 1-4z"),
        [IconKind.Up] = S("M12 19V5M5 12l7-7 7 7"),
        [IconKind.Down] = S("M12 5v14M19 12l-7 7-7-7"),
        [IconKind.ChevronLeft] = S("M15 18l-6-6 6-6"),
        [IconKind.ChevronRight] = S("M9 18l6-6-6-6"),
        [IconKind.ChevronDown] = S("M6 9l6 6 6-6"),
        [IconKind.Back] = S("M19 12H5M12 19l-7-7 7-7"),
        [IconKind.Dice] = new(G("M6 3h12a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3z"),
            G(Circle(8.5, 8.5, 1.2) + Circle(15.5, 15.5, 1.2) + Circle(15.5, 8.5, 1.2) + Circle(8.5, 15.5, 1.2)), 2),
        [IconKind.Link] = S("M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"),
        [IconKind.Alert] = S(Circle(12, 12, 9) + "M12 8v4M12 16h.01"),
        [IconKind.Info] = S(Circle(12, 12, 9) + "M12 11v5M12 8h.01"),
        [IconKind.WifiOff] = S("M2 2l20 20M8.5 16.4a5 5 0 0 1 7 0M5 12.9a10 10 0 0 1 5.2-2.7M19 12.9a10 10 0 0 0-2.1-1.6M2 8.8a15 15 0 0 1 4.2-2.6M22 8.8A15 15 0 0 0 10.7 5M12 20h.01"),
        [IconKind.Settings] = S(Circle(12, 12, 3) + "M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-2.8 1.2V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-2.8-1.2l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0-1.2-2.8H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.2-2.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 2.8-1.2V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 2.8 1.2l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0 1.2 2.8H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"),
        [IconKind.Monitor] = S("M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM8 21h8M12 17v4"),
        [IconKind.Sun] = S(Circle(12, 12, 4) + "M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"),
        [IconKind.Moon] = S("M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"),
    };
}