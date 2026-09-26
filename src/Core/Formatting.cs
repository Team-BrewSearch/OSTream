using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace OSTream.Core;

public static partial class Formatting
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static long JsRound(double value)
    {
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }
    public static string Time(double sec)
    {
        if (!double.IsFinite(sec) || sec < 0)
        {
            sec = 0;
        }
        long h = (long)(sec / 3600);
        long m = (long)(sec % 3600 / 60);
        long s = (long)(sec % 60);
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }
    public static int ParseDuration(string? text)
    {
        double total = 0;
        foreach (string part in (text ?? "").Trim().Split(':'))
        {
            double n = 0;
            if (part.Length > 0 && !double.TryParse(part, NumberStyles.Float, Inv, out n))
            {
                return 0;
            }
            total = (total * 60) + n;
        }
        return (int)total;
    }
    public static string Length(double sec)
    {
        if (sec <= 0 || !double.IsFinite(sec))
        {
            return "";
        }
        long h = (long)(sec / 3600);
        long m = (long)(sec % 3600 / 60);
        return h > 0 ? $"{h}h {m}m" : m > 0 ? $"{m}m {JsRound(sec % 60)}s" : $"{JsRound(sec)}s";
    }
    public static string Eta(double? sec)
    {
        return sec is not { } s || !double.IsFinite(s)
            ? ""
            : s < 60 ? $"~{Math.Max(1, JsRound(s))} s" : s < 3600 ? $"~{JsRound(s / 60)} min" : $"~{(s / 3600).ToString("0.0", Inv)} h";
    }
    public static string TimeAgo(long unixMs)
    {
        long sec = JsRound((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - unixMs) / 1000.0);
        return sec < 60
            ? "just now"
            : sec < 3600
            ? $"{sec / 60} min ago"
            : sec < 86400 ? $"{sec / 3600} h ago" : sec < 86400 * 7 ? $"{sec / 86400} d ago" : ShortDate(unixMs);
    }
    public static string ShortDate(long unixMs)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("d", CultureInfo.CurrentCulture);
    }
    public static long NowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
    [GeneratedRegex(@"([\d.]+)\s*([KMGT]?B)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();
    public static long ParseSize(string? text)
    {
        Match m = SizeRegex().Match((text ?? "").Replace(",", ""));
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, Inv, out double n))
        {
            return 0;
        }
        double unit = m.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            "TB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d,
        };
        return JsRound(n * unit);
    }
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];
    public static string Bytes(double bytes)
    {
        if (bytes <= 0 || !double.IsFinite(bytes))
        {
            return "0 B";
        }
        int i = Math.Min(Units.Length - 1, (int)Math.Floor(Math.Log(bytes) / Math.Log(1024)));
        double n = bytes / Math.Pow(1024, i);
        string value = n >= 100 || i == 0 ? JsRound(n).ToString(Inv) : n.ToString("0.0", Inv);
        return $"{value} {Units[i]}";
    }
    public static string Plural(long n, string word, string? pluralWord = null)
    {
        return $"{n} {(n == 1 ? word : pluralWord ?? (word + "s"))}";
    }
    public static string JoinNonEmpty(string separator, params string?[] parts)
    {
        StringBuilder sb = new();
        foreach (string? part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }
            if (sb.Length > 0)
            {
                _ = sb.Append(separator);
            }
            _ = sb.Append(part);
        }
        return sb.ToString();
    }
    public static string AlbumSubtitle(IReadOnlyList<string> platforms, string year, string type)
    {
        string platform = platforms.Count > 1 ? $"{platforms[0]} +{platforms.Count - 1}" : platforms.Count == 1 ? platforms[0] : "";
        return JoinNonEmpty(" · ", platform, year, type);
    }
    [GeneratedRegex(@"/game-soundtracks/album/([^/?#]+)")]
    private static partial Regex SlugRegex();
    public static string SlugFromUrl(string? url)
    {
        Match m = SlugRegex().Match(url ?? "");
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : "";
    }
    public static string AlbumPath(string slug)
    {
        return Config.AlbumPath + EncodeUriComponent(slug);
    }
    public static string AlbumUrl(string slug)
    {
        return Config.BaseUrl + AlbumPath(slug);
    }
    public static string SiteUrl(string? path)
    {
        return string.IsNullOrEmpty(path) ? ""
            : path.StartsWith("//", StringComparison.Ordinal) ? "https:" + path
            : path.StartsWith('/') ? Config.BaseUrl + path
            : path;
    }
    public static string LargerThumb(string? url)
    {
        return string.IsNullOrEmpty(url) ? "" : url.Replace("/thumbs_small/", "/thumbs/").Replace("/images/nsfw_small.png", "/images/nsfw.png");
    }
    public static string EncodeUriComponent(string value)
    {
        string escaped = Uri.EscapeDataString(value);
        return escaped.Contains('%')
            ? escaped.Replace("%21", "!").Replace("%27", "'").Replace("%28", "(").Replace("%29", ")").Replace("%2A", "*")
            : escaped;
    }
    public static int HashIndex(string? text, int n = 6)
    {
        string s = text ?? "";
        int h = 0;
        for (int i = 0; i < s.Length; i++)
        {
            h = unchecked((h * 31) + s[i]);
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
        }
        return (int)(Math.Abs((long)h) % n) + 1;
    }
    public static string NewId()
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> id = stackalloc char[12];
        for (int i = 0; i < 8; i++)
        {
            id[i] = digits[Random.Shared.Next(36)];
        }
        long now = NowMs();
        for (int i = 11; i >= 8; i--, now /= 36)
        {
            id[i] = digits[(int)(now % 36)];
        }
        return new string(id);
    }
    public static T[] Shuffled<T>(IReadOnlyList<T> list)
    {
        T[] copy = new T[list.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = list[i];
        }
        Random.Shared.Shuffle(copy);
        return copy;
    }
}