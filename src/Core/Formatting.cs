using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace OSTream.Core;
public static partial class Formatting
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static long JsRound(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
    public static string Time(double sec)
    {
        if (!double.IsFinite(sec) || sec < 0) sec = 0;
        var h = (long)(sec / 3600);
        var m = (long)(sec % 3600 / 60);
        var s = (long)(sec % 60);
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }
    public static int ParseDuration(string? text)
    {
        double total = 0;
        foreach (var part in (text ?? "").Trim().Split(':'))
        {
            double n = 0;
            if (part.Length > 0 && !double.TryParse(part, NumberStyles.Float, Inv, out n)) return 0;
            total = total * 60 + n;
        }
        return (int)total;
    }
    public static string Length(double sec)
    {
        if (sec <= 0 || !double.IsFinite(sec)) return "";
        var h = (long)(sec / 3600);
        var m = (long)(sec % 3600 / 60);
        if (h > 0) return $"{h}h {m}m";
        if (m > 0) return $"{m}m {JsRound(sec % 60)}s";
        return $"{JsRound(sec)}s";
    }
    public static string Eta(double? sec)
    {
        if (sec is not { } s || !double.IsFinite(s)) return "";
        if (s < 60) return $"~{Math.Max(1, JsRound(s))} s";
        if (s < 3600) return $"~{JsRound(s / 60)} min";
        return $"~{(s / 3600).ToString("0.0", Inv)} h";
    }
    public static string TimeAgo(long unixMs)
    {
        var sec = JsRound((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - unixMs) / 1000.0);
        if (sec < 60) return "just now";
        if (sec < 3600) return $"{sec / 60} min ago";
        if (sec < 86400) return $"{sec / 3600} h ago";
        if (sec < 86400 * 7) return $"{sec / 86400} d ago";
        return ShortDate(unixMs);
    }
    public static string ShortDate(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("d", CultureInfo.CurrentCulture);
    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    [GeneratedRegex(@"([\d.]+)\s*([KMGT]?B)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();
    public static long ParseSize(string? text)
    {
        var m = SizeRegex().Match((text ?? "").Replace(",", ""));
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, Inv, out var n)) return 0;
        var unit = m.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            "TB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d,
        };
        return JsRound(n * unit);
    }
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];
    public static string Bytes(double bytes)
    {
        if (bytes <= 0 || !double.IsFinite(bytes)) return "0 B";
        var i = Math.Min(Units.Length - 1, (int)Math.Floor(Math.Log(bytes) / Math.Log(1024)));
        var n = bytes / Math.Pow(1024, i);
        var value = n >= 100 || i == 0 ? JsRound(n).ToString(Inv) : n.ToString("0.0", Inv);
        return $"{value} {Units[i]}";
    }
    public static string Plural(long n, string word, string? pluralWord = null) =>
        $"{n} {(n == 1 ? word : pluralWord ?? word + "s")}";
    public static string JoinNonEmpty(string separator, params string?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            if (sb.Length > 0) sb.Append(separator);
            sb.Append(part);
        }
        return sb.ToString();
    }
    public static string AlbumSubtitle(IReadOnlyList<string> platforms, string year, string type)
    {
        var platform = platforms.Count > 1 ? $"{platforms[0]} +{platforms.Count - 1}" : platforms.Count == 1 ? platforms[0] : "";
        return JoinNonEmpty(" · ", platform, year, type);
    }
    [GeneratedRegex(@"/game-soundtracks/album/([^/?#]+)")]
    private static partial Regex SlugRegex();
    public static string SlugFromUrl(string? url)
    {
        var m = SlugRegex().Match(url ?? "");
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : "";
    }
    public static string AlbumPath(string slug) => Config.AlbumPath + EncodeUriComponent(slug);
    public static string AlbumUrl(string slug) => Config.BaseUrl + AlbumPath(slug);
    public static string SiteUrl(string? path) =>
        string.IsNullOrEmpty(path) ? ""
        : path.StartsWith("//", StringComparison.Ordinal) ? "https:" + path
        : path.StartsWith('/') ? Config.BaseUrl + path
        : path;
    public static string LargerThumb(string? url) =>
        string.IsNullOrEmpty(url) ? "" : url.Replace("/thumbs_small/", "/thumbs/").Replace("/images/nsfw_small.png", "/images/nsfw.png");
    public static string EncodeUriComponent(string value)
    {
        var escaped = Uri.EscapeDataString(value);
        return escaped.Contains('%')
            ? escaped.Replace("%21", "!").Replace("%27", "'").Replace("%28", "(").Replace("%29", ")").Replace("%2A", "*")
            : escaped;
    }
    public static int HashIndex(string? text, int n = 6)
    {
        var s = text ?? "";
        int h = 0;
        for (var i = 0; i < s.Length; i++)
        {
            h = unchecked(h * 31 + s[i]);
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
        }
        return (int)(Math.Abs((long)h) % n) + 1;
    }
    public static string NewId()
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> id = stackalloc char[12];
        for (var i = 0; i < 8; i++) id[i] = digits[Random.Shared.Next(36)];
        var now = NowMs();
        for (var i = 11; i >= 8; i--, now /= 36) id[i] = digits[(int)(now % 36)];
        return new string(id);
    }
    public static T[] Shuffled<T>(IReadOnlyList<T> list)
    {
        var copy = new T[list.Count];
        for (var i = 0; i < copy.Length; i++) copy[i] = list[i];
        Random.Shared.Shuffle(copy);
        return copy;
    }
}