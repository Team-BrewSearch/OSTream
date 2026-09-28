using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using OSTream.Controls;
namespace OSTream.Themes;
public static class Tokens
{
    public const string UiFontName = "avares://OSTream/Assets/Fonts#Inter";
    public const string MonoFontName = "avares://OSTream/Assets/Fonts#JetBrains Mono";
    static readonly (string Key, string Dark, string Light)[] Colors =
    [
        ("Bg0", "#08080b", "#f5f5f8"),
        ("Bg1", "#0e0e13", "#ffffff"),
        ("Bg2", "#15151b", "#ffffff"),
        ("Bg3", "#1c1c24", "#ececf2"),
        ("Bg4", "#25252f", "#e2e2ea"),
        ("Control", "#15151b", "#ececf1"),
        ("ControlActive", "#25252f", "#ffffff"),
        ("Border1", "#22222b", "#e5e5ec"),
        ("Border2", "#2e2e3a", "#d7d7e0"),
        ("Border3", "#3a3a48", "#c2c2ce"),
        ("Text", "#f4f4f7", "#15151c"),
        ("Text2", "#9b9ba7", "#55556a"),
        ("Text3", "#8b8b99", "#626276"),
        ("Text3Dim", "#998b8b99", "#99626276"),
        ("Accent", "#a78bfa", "#7c3aed"),
        ("Accent2", "#c4b5fd", "#6d28d9"),
        ("AccentDeep", "#7c3aed", "#5b21b6"),
        ("OnAccent", "#140b2e", "#ffffff"),
        ("AccentGlow", Rgba(167, 139, 250, 0.35), Rgba(124, 58, 237, 0.24)),
        ("AccentSoft", Rgba(167, 139, 250, 0.12), Rgba(124, 58, 237, 0.10)),
        ("AccentSofter", Rgba(167, 139, 250, 0.06), Rgba(124, 58, 237, 0.05)),
        ("AccentLine", Rgba(167, 139, 250, 0.35), Rgba(124, 58, 237, 0.30)),
        ("Green", "#4ade80", "#16a34a"),
        ("GreenSoft", Rgba(74, 222, 128, 0.14), Rgba(22, 163, 74, 0.12)),
        ("Red", "#f87171", "#dc2626"),
        ("RedSoft", Rgba(248, 113, 113, 0.14), Rgba(220, 38, 38, 0.10)),
        ("Amber", "#fbbf24", "#d97706"),
        ("Glass", Rgba(14, 14, 19, 0.72), Rgba(255, 255, 255, 0.74)),
        ("GlassStrong", Rgba(14, 14, 19, 0.85), Rgba(255, 255, 255, 0.88)),
        ("Panel", Rgba(11, 11, 15, 0.4), Rgba(255, 255, 255, 0.5)),
        ("Popover", Rgba(21, 21, 27, 0.92), Rgba(255, 255, 255, 0.96)),
        ("Scrim", Rgba(4, 4, 8, 0.62), Rgba(20, 20, 32, 0.32)),
        ("BackdropGlow2", Rgba(56, 189, 248, 0.05), Rgba(56, 189, 248, 0.07)),
        ("ArtShade", Rgba(0, 0, 0, 0.4), Rgba(0, 0, 0, 0.32)),
        ("Skeleton", Rgba(255, 255, 255, 0.045), Rgba(20, 20, 40, 0.06)),
        ("SkeletonShine", Rgba(255, 255, 255, 0.07), Rgba(20, 20, 40, 0.10)),
    ];
    static string Rgba(int r, int g, int b, double a) => $"#{(int)System.Math.Round(a * 255):X2}{r:X2}{g:X2}{b:X2}";
    static double Blur(double css) => css <= 0 ? 0 : System.Math.Max(0, (css / 2 - 0.5) / 0.57735);
    static BoxShadow Shadow(double x, double y, double blur, double spread, Color color, bool inset = false) =>
        new() { OffsetX = x, OffsetY = y, Blur = Blur(blur), Spread = spread, Color = color, IsInset = inset };
    static BoxShadows Shadows(params BoxShadow[] shadows) =>
        shadows.Length == 1 ? new BoxShadows(shadows[0]) : new BoxShadows(shadows[0], shadows[1..]);
    public static void Install(Application app)
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        foreach (var (key, d, l) in Colors)
        {
            dark[key] = new SolidColorBrush(Color.Parse(d));
            light[key] = new SolidColorBrush(Color.Parse(l));
            dark[key + "Color"] = Color.Parse(d);
            light[key + "Color"] = Color.Parse(l);
        }
        AddComposites(dark, isDark: true);
        AddComposites(light, isDark: false);
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = light;
        foreach (var (key, value) in Static()) app.Resources[key] = value;
        app.Resources["ButtonTransitions"] = Fade(Motion.Normal, withTransform: true, withBorder: true);
        app.Resources["FastTransitions"] = Fade(Motion.Fast, withTransform: false, withBorder: false);
    }
    static Transitions Fade(TimeSpan duration, bool withTransform, bool withBorder)
    {
        var easing = Motion.Standard;
        var transitions = new Transitions
        {
            new BrushFadeTransition { Property = TemplatedControl.BackgroundProperty, Duration = duration, Easing = easing },
            new BrushFadeTransition { Property = TextElement.ForegroundProperty, Duration = duration, Easing = easing },
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = easing },
        };
        if (withBorder) transitions.Add(new BrushFadeTransition { Property = TemplatedControl.BorderBrushProperty, Duration = duration, Easing = easing });
        if (withTransform) transitions.Add(new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing });
        return transitions;
    }
    static Color C(ResourceDictionary d, string key) => (Color)d[key + "Color"]!;
    static void AddComposites(ResourceDictionary d, bool isDark)
    {
        var glow = C(d, "AccentGlow");
        var black = (double a) => Color.FromArgb((byte)(a * 255), 0, 0, 0);
        var shade = isDark ? black : (double a) => Color.FromArgb((byte)(a * 255), 20, 20, 40);
        d["ShadowSm"] = Shadows(Shadow(0, 1, 2, 0, shade(isDark ? 0.4 : 0.08)));
        d["ShadowMd"] = Shadows(Shadow(0, 4, 16, 0, shade(isDark ? 0.45 : 0.1)));
        d["ShadowLg"] = Shadows(Shadow(0, 12, 40, 0, shade(isDark ? 0.55 : 0.16)));
        d["ShadowPop"] = Shadows(Shadow(0, 18, 50, 0, shade(isDark ? 0.6 : 0.18)), Shadow(0, 0, 0, 1, C(d, isDark ? "Border2" : "Border1")));
        var insetHi = Shadow(0, 1, 0, 0, isDark ? Color.FromArgb(15, 255, 255, 255) : Color.FromArgb(230, 255, 255, 255), inset: true);
        d["SegmentedActiveShadow"] = Shadows(Shadow(0, 1, 2, 0, shade(isDark ? 0.4 : 0.08)), insetHi);
        d["Glow6"] = Shadows(Shadow(0, 0, 6, 0, glow));
        d["Glow8"] = Shadows(Shadow(0, 0, 8, 0, glow));
        d["Glow10"] = Shadows(Shadow(0, 0, 10, 0, glow));
        d["Glow12"] = Shadows(Shadow(0, 0, 12, 0, glow));
        d["Glow20"] = Shadows(Shadow(0, 0, 20, 0, glow));
        d["Glow28"] = Shadows(Shadow(0, 0, 28, 0, glow));
        d["GlowGreen"] = Shadows(Shadow(0, 0, 8, 0, C(d, "GreenSoft")));
        d["BrandGlow"] = Shadows(Shadow(0, 0, 20, 0, glow), Shadow(0, 1, 0, 0, Color.FromArgb(51, 255, 255, 255), inset: true));
        d["FocusRing"] = Shadows(Shadow(0, 0, 0, 3, C(d, "AccentSoft")));
        d["CardHover"] = Shadows(Shadow(0, 4, 16, 0, shade(isDark ? 0.45 : 0.1)));
        d["CardSelected"] = Shadows(Shadow(0, 0, 0, 1, C(d, "Accent")), Shadow(0, 8, 24, -8, glow));
        d["ThemeOptionSelected"] = Shadows(Shadow(0, 0, 0, 1, C(d, "Accent")), Shadow(0, 6, 18, -8, glow));
        d["AlbumPlayShadow"] = Shadows(Shadow(0, 4, 16, 0, black(0.5)), Shadow(0, 0, 20, 0, glow));
        d["SliderThumbShadow"] = Shadows(Shadow(0, 0, 0, 1, black(0.12)), Shadow(0, 0, 10, 0, glow));
        d["NoShadow"] = new BoxShadows();
        d["AccentGradient"] = Horizontal(C(d, "Accent"), C(d, "Accent2"));
        d["NavActiveGradient"] = Horizontal(C(d, "AccentSoft"), Colors_.Transparent(C(d, "AccentSoft")), 0.8);
        d["TrackPlayingGradient"] = Horizontal(C(d, "AccentSofter"), Colors_.Transparent(C(d, "AccentSofter")), 0.7);
        d["TrackPlayingHoverGradient"] = Horizontal(C(d, "AccentSoft"), C(d, "Bg2"), 0.7);
        d["SkeletonShineGradient"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors_.Transparent(C(d, "SkeletonShine")), 0),
                new GradientStop(C(d, "SkeletonShine"), 0.5),
                new GradientStop(Colors_.Transparent(C(d, "SkeletonShine")), 1),
            },
        };
    }
    static LinearGradientBrush Horizontal(Color from, Color to, double toOffset = 1) => new()
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, toOffset) },
    };
    static IEnumerable<(string, object)> Static()
    {
        string[][] art =
        [
            ["#1e1b4b", "#7c3aed", "#ec4899"],
            ["#082f49", "#0891b2", "#22d3ee"],
            ["#4c0519", "#be123c", "#fb923c"],
            ["#1e293b", "#4f46e5", "#22d3ee"],
            ["#052e16", "#16a34a", "#facc15"],
            ["#3b0764", "#a21caf", "#f472b6"],
        ];
        for (var i = 0; i < art.Length; i++)
            yield return ($"Art{i + 1}", Diagonal(Color.Parse(art[i][0]), Color.Parse(art[i][1]), Color.Parse(art[i][2])));
        yield return ("BrandGradient", new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#a78bfa"), 0), new GradientStop(Color.Parse("#7c3aed"), 1) },
        });
        yield return ("MonoFont", new FontFamily(MonoFontName));
        yield return ("UiFont", new FontFamily(UiFontName));
    }
    static LinearGradientBrush Diagonal(Color a, Color b, Color c) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(a, 0), new GradientStop(b, 0.55), new GradientStop(c, 1) },
    };
    static class Colors_
    {
        public static Color Transparent(Color c) => Color.FromArgb(0, c.R, c.G, c.B);
    }
}