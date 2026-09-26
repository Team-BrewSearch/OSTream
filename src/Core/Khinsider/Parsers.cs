using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OSTream.Core.Html;
using OSTream.Core.Models;
namespace OSTream.Core.Khinsider;
public static partial class Parsers
{
    static readonly string[] AudioFormats = ["mp3", "flac", "alac", "m4a", "aac", "ogg", "wav"];
    const string AlbumHref = "/game-soundtracks/album/";
    static string Text(HtmlNode? node) => node?.Text ?? "";
    static bool IsAlbumLink(HtmlNode node) => node.Name == "a" && (node.Attr("href")?.Contains(AlbumHref, StringComparison.Ordinal) ?? false);
    static IEnumerable<HtmlNode> CellsOf(HtmlNode row) => row.Children.Where(c => c.Name == "td");
    static string ImgSrc(HtmlNode? node) => Formatting.SiteUrl(node?.First("img")?.Attr("src"));
    static int Span(HtmlNode cell) => int.TryParse(cell.Attr("colspan"), out var n) && n > 1 ? n : 1;
    static List<string?> HeaderColumns(HtmlNode? headerRow, Func<string, int, string[]> mapLabel)
    {
        var columns = new List<string?>();
        if (headerRow is null) return columns;
        foreach (var th in headerRow.Children)
        {
            var span = Span(th);
            var keys = mapLabel(Text(th).ToLowerInvariant(), span);
            for (var i = 0; i < span; i++) columns.Add(i < keys.Length ? keys[i] : null);
        }
        return columns;
    }
    static Dictionary<string, HtmlNode> RowByColumn(IEnumerable<HtmlNode> cells, List<string?> columns)
    {
        var map = new Dictionary<string, HtmlNode>();
        var col = 0;
        foreach (var cell in cells)
        {
            if (col < columns.Count && columns[col] is { } key) map[key] = cell;
            col += Span(cell);
        }
        return map;
    }
    static HtmlNode? Cell(Dictionary<string, HtmlNode> cells, string key) => cells.GetValueOrDefault(key);
    static int? ParseIntOrNull(string text)
    {
        var s = text.AsSpan().TrimStart();
        var len = 0;
        if (len < s.Length && (s[len] == '-' || s[len] == '+')) len++;
        while (len < s.Length && char.IsAsciiDigit(s[len])) len++;
        return int.TryParse(s[..len], out var n) ? n : null;
    }
    [GeneratedRegex(@"\s*\((\d{4})\)$")]
    private static partial Regex YearSuffix();
    static (string Title, string Year) SplitYear(string title)
    {
        var m = YearSuffix().Match(title);
        return m.Success ? (title[..m.Index].Trim(), m.Groups[1].Value) : (title, "");
    }
    [GeneratedRegex(@",\s*")]
    private static partial Regex CommaList();
    static string[] SplitList(string value) => CommaList().Split(value).Where(s => s.Length > 0).ToArray();
    public static List<AlbumSummary> ParseAlbumList(HtmlNode doc)
    {
        var albums = new List<AlbumSummary>();
        var seen = new HashSet<string>();
        foreach (var table in doc.Descendants("table").Where(t => t.HasClass("albumList") || t.Id == "top40").ToList())
        {
            var rows = table.Descendants("tr").ToList();
            var headerRow = rows.FirstOrDefault(r => r.First("th") is not null);
            var columns = HeaderColumns(headerRow, (label, _) => label switch
            {
                "album" => ["title"],
                "platform" => ["platform"],
                "type" => ["type"],
                "year" => ["year"],
                "#" => ["rank"],
                _ => [],
            });
            foreach (var row in rows)
            {
                if (row == headerRow) continue;
                var titleLink = row.First(a => IsAlbumLink(a) && a.Ancestors().Any(p => p.Name == "td" && !p.HasClass("albumIcon")));
                var slug = Formatting.SlugFromUrl(titleLink?.Attr("href"));
                if (titleLink is null || slug.Length == 0 || !seen.Add(slug)) continue;
                var cells = RowByColumn(CellsOf(row), columns);
                var title = Text(titleLink);
                var year = Text(Cell(cells, "year"));
                if (year.Length == 0) (title, year) = SplitYear(title);
                var catalog = Text(titleLink.Parent?.First("span"));
                if (catalog.StartsWith('[')) catalog = catalog[1..];
                if (catalog.EndsWith(']')) catalog = catalog[..^1];
                var platformCell = Cell(cells, "platform");
                var platformLinks = platformCell?.Descendants("a").Select(Text).Where(t => t.Length > 0).ToArray() ?? [];
                albums.Add(new AlbumSummary
                {
                    Slug = slug,
                    Title = title,
                    Thumb = ImgSrc(row.First(td => td.Name == "td" && td.HasClass("albumIcon"))),
                    Platforms = platformLinks.Length > 0 ? platformLinks : SplitList(Text(platformCell)),
                    Type = Text(Cell(cells, "type")),
                    Year = year,
                    Catalog = catalog,
                    Rank = ParseIntOrNull(Text(Cell(cells, "rank"))),
                });
            }
        }
        return albums;
    }
    sealed record InfoLine(string Text, List<string> Links);
    static List<InfoLine> ParseInfoLines(HtmlNode? p)
    {
        var lines = new List<InfoLine>();
        if (p is null) return lines;
        var text = new StringBuilder();
        var links = new List<string>();
        void Flush()
        {
            if (text.ToString().Trim().Length > 0) lines.Add(new(text.ToString(), links));
            text.Clear();
            links = [];
        }
        foreach (var node in p.ChildNodes)
        {
            if (node.Name == "br")
            {
                Flush();
                continue;
            }
            if (node.Name is "table" or "div") continue;
            if (node.IsElement)
            {
                IEnumerable<HtmlNode> anchors = node.Name == "a" ? [node] : node.Descendants("a");
                links.AddRange(anchors.Select(Text).Where(t => t.Length > 0));
                text.Append(node.TextContent);
            }
            else if (node.IsText)
            {
                text.Append(node.Data);
            }
        }
        Flush();
        return lines;
    }
    [GeneratedRegex(@"^([^:]+):\s*(.*)$")]
    private static partial Regex InfoLineRegex();
    [GeneratedRegex(@"\s*\|.*$")]
    private static partial Regex PipeSuffix();
    [GeneratedRegex(@"change log", RegexOptions.IgnoreCase)]
    private static partial Regex ChangeLog();
    [GeneratedRegex(@"([\d.,]+\s*[KMGT]?B)\s*\(([^)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalSize();
    [GeneratedRegex(@"^\d+h(\s\d+m)?$|^\d+m(\s\d+s)?$|^\d+s$")]
    private static partial Regex FooterLength();
    sealed class AlbumInfo
    {
        public IReadOnlyList<string> Platforms = [];
        public IReadOnlyList<string> Developers = [];
        public IReadOnlyList<string> Publishers = [];
        public readonly Dictionary<string, string> TotalSizes = [];
        public string Year = "", Catalog = "", DateAdded = "", Type = "";
        public int? FileCount;
    }
    static AlbumInfo ParseAlbumInfo(HtmlNode? p)
    {
        var info = new AlbumInfo();
        foreach (var (rawLine, links) in ParseInfoLines(p))
        {
            var m = InfoLineRegex().Match(HtmlNode.Collapse(rawLine));
            if (!m.Success) continue;
            var key = m.Groups[1].Value.Trim().ToLowerInvariant();
            var value = PipeSuffix().Replace(m.Groups[2].Value, "").Trim();
            var list = links.Where(l => !ChangeLog().IsMatch(l)).ToArray();
            var asList = list.Length > 0 ? list : SplitList(value);
            switch (key)
            {
                case "platforms" or "platform": info.Platforms = asList; break;
                case "year": info.Year = value; break;
                case "catalog number": info.Catalog = value; break;
                case "developed by": info.Developers = asList; break;
                case "published by": info.Publishers = asList; break;
                case "number of files": info.FileCount = ParseIntOrNull(value); break;
                case "date added": info.DateAdded = value; break;
                case "album type": info.Type = value; break;
                case "total filesize":
                    foreach (Match size in TotalSize().Matches(value))
                        info.TotalSizes[size.Groups[2].Value.Trim().ToLowerInvariant()] = size.Groups[1].Value.Trim();
                    break;
            }
        }
        return info;
    }
    public static Album? ParseAlbum(HtmlNode doc, string url, string? slugOverride = null)
    {
        var page = doc.ById("pageContent");
        var table = doc.First(n => n.Name == "table" && n.Id == "songlist");
        if (page is null || table is null) return null;
        var urlSlug = Formatting.SlugFromUrl(url);
        var slug = string.IsNullOrEmpty(slugOverride) ? urlSlug : slugOverride;
        var title = Text(page.First("h2"));
        if (title.Length == 0) title = urlSlug;
        var altEl = page.First(n => n.HasClass("albuminfoAlternativeTitles"));
        var altTitles = altEl?.ChildNodes.Where(n => n.IsText).Select(n => HtmlNode.Collapse(n.Data ?? "")).Where(t => t.Length > 0).ToArray() ?? [];
        var info = ParseAlbumInfo(page.First(n => n.Name == "p" && string.Equals(n.Attr("align"), "left", StringComparison.OrdinalIgnoreCase)));
        var images = page.Descendants("a")
            .Where(a => a.Ancestors().Any(n => n.HasClass("albumImage")))
            .Select(a =>
            {
                var href = Formatting.SiteUrl(a.Attr("href"));
                var thumb = ImgSrc(a);
                return new AlbumImage(href, thumb.Length > 0 ? thumb : href);
            })
            .Where(img => img.Full.Length > 0 || img.Thumb.Length > 0)
            .ToArray();
        var columns = HeaderColumns(table.ById("songlist_header"), (label, span) =>
        {
            if (label == "song name") return span >= 2 ? ["title", "duration"] : ["title"];
            if (label == "cd") return ["cd"];
            if (label == "#") return ["number"];
            return AudioFormats.Contains(label) ? [$"fmt:{label}"] : [];
        });
        var formats = columns.Where(c => c is not null && c.StartsWith("fmt:", StringComparison.Ordinal)).Select(c => c![4..]).ToArray();
        var cover = images.Length > 0 ? images[0].Thumb : "";
        var tracks = new List<Track>();
        var rows = new List<(HtmlNode Link, Dictionary<string, HtmlNode> Cells)>();
        foreach (var row in table.Descendants("tr"))
        {
            if (row.Id is "songlist_header" or "songlist_footer") continue;
            var link = row.First(IsAlbumLink);
            if (link is null) continue;
            rows.Add((link, RowByColumn(CellsOf(row), columns)));
        }
        var albumRef = new AlbumRef { Slug = slug, Title = title, Thumb = cover, TrackCount = rows.Count };
        foreach (var (link, cells) in rows)
        {
            var sizes = new Dictionary<string, string>();
            foreach (var fmt in formats)
            {
                var size = Text(Cell(cells, $"fmt:{fmt}"));
                if (size.Length > 0) sizes[fmt] = size;
            }
            var path = link.Attr("href") ?? "";
            var duration = Text(Cell(cells, "duration"));
            var number = Text(Cell(cells, "number"));
            if (number.EndsWith('.')) number = number[..^1];
            var trackTitle = Text(Cell(cells, "title"));
            tracks.Add(new Track
            {
                Id = path,
                Path = path,
                Index = tracks.Count,
                Cd = Text(Cell(cells, "cd")),
                Number = number,
                Title = trackTitle.Length > 0 ? trackTitle : Text(link),
                Duration = duration,
                DurationSec = Formatting.ParseDuration(duration),
                Sizes = sizes,
                Album = albumRef,
            });
        }
        var footerLength = table.Descendants()
            .Where(n => n.Id == "songlist_footer")
            .SelectMany(n => n.Descendants("th"))
            .Select(Text)
            .FirstOrDefault(t => FooterLength().IsMatch(t)) ?? "";
        var related = new List<AlbumSummary>();
        var seen = new HashSet<string> { urlSlug };
        foreach (var td in page.Descendants("td").Where(td => td.HasClass("albumIconLarge")))
        {
            var a = td.First(IsAlbumLink);
            var relSlug = Formatting.SlugFromUrl(a?.Attr("href"));
            if (a is null || relSlug.Length == 0 || !seen.Add(relSlug)) continue;
            var relTitle = Text(a.First("p"));
            if (relTitle.Length == 0) relTitle = Text(a);
            var (cleanTitle, year) = SplitYear(relTitle);
            related.Add(new AlbumSummary { Slug = relSlug, Title = cleanTitle, Thumb = ImgSrc(a), Year = year });
        }
        return new Album
        {
            Slug = slug,
            Url = url,
            Title = title,
            AltTitles = altTitles,
            Images = images,
            Cover = cover,
            CoverFull = images.Length > 0 ? images[0].Full : "",
            Platforms = info.Platforms,
            Developers = info.Developers,
            Publishers = info.Publishers,
            TotalSizes = info.TotalSizes,
            Year = info.Year,
            Catalog = info.Catalog,
            FileCount = info.FileCount,
            DateAdded = info.DateAdded,
            Type = info.Type,
            Formats = formats,
            HasCd = tracks.Any(t => t.Cd.Length > 0),
            Tracks = tracks,
            TotalSec = tracks.Sum(t => t.DurationSec),
            TotalLength = footerLength,
            Related = related,
        };
    }
    [GeneratedRegex(@"href=""(https?://[^""]+?\.(mp3|flac|m4a|ogg|wav))""", RegexOptions.IgnoreCase)]
    private static partial Regex AudioLink();
    public static Dictionary<string, string> ParseTrackUrls(string html)
    {
        var urls = new Dictionary<string, string>();
        foreach (Match m in AudioLink().Matches(html))
        {
            var href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            if (KhinsiderClient.SecureMediaUrl(href) is { } secure) urls.TryAdd(m.Groups[2].Value.ToLowerInvariant(), secure);
        }
        return urls;
    }
}