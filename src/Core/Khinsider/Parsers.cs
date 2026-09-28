using OSTream.Core.Html;
using OSTream.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace OSTream.Core.Khinsider;

public static partial class Parsers
{
    private static readonly string[] AudioFormats = ["mp3", "flac", "alac", "m4a", "aac", "ogg", "wav"];
    private const string AlbumHref = "/game-soundtracks/album/";
    private static string Text(HtmlNode? node)
    {
        return node?.Text ?? "";
    }

    private static bool IsAlbumLink(HtmlNode node)
    {
        return node.Name == "a" && (node.Attr("href")?.Contains(AlbumHref, StringComparison.Ordinal) ?? false);
    }

    private static IEnumerable<HtmlNode> CellsOf(HtmlNode row)
    {
        return row.Children.Where(c => c.Name == "td");
    }

    private static string ImgSrc(HtmlNode? node)
    {
        return Formatting.SiteUrl(node?.First("img")?.Attr("src"));
    }

    private static int Span(HtmlNode cell)
    {
        return int.TryParse(cell.Attr("colspan"), out int n) && n > 1 ? n : 1;
    }

    private static List<string?> HeaderColumns(HtmlNode? headerRow, Func<string, int, string[]> mapLabel)
    {
        List<string?> columns = [];
        if (headerRow is null)
        {
            return columns;
        }

        foreach (HtmlNode th in headerRow.Children)
        {
            int span = Span(th);
            string[] keys = mapLabel(Text(th).ToLowerInvariant(), span);
            for (int i = 0; i < span; i++)
            {
                columns.Add(i < keys.Length ? keys[i] : null);
            }
        }
        return columns;
    }
    private static Dictionary<string, HtmlNode> RowByColumn(IEnumerable<HtmlNode> cells, List<string?> columns)
    {
        Dictionary<string, HtmlNode> map = [];
        int col = 0;
        foreach (HtmlNode cell in cells)
        {
            if (col < columns.Count && columns[col] is { } key)
            {
                map[key] = cell;
            }

            col += Span(cell);
        }
        return map;
    }
    private static HtmlNode? Cell(Dictionary<string, HtmlNode> cells, string key)
    {
        return cells.GetValueOrDefault(key);
    }

    private static int? ParseIntOrNull(string text)
    {
        ReadOnlySpan<char> s = text.AsSpan().TrimStart();
        int len = 0;
        if (len < s.Length && (s[len] == '-' || s[len] == '+'))
        {
            len++;
        }

        while (len < s.Length && char.IsAsciiDigit(s[len]))
        {
            len++;
        }

        return int.TryParse(s[..len], out int n) ? n : null;
    }
    [GeneratedRegex(@"\s*\((\d{4})\)$")]
    private static partial Regex YearSuffix();
    private static (string Title, string Year) SplitYear(string title)
    {
        Match m = YearSuffix().Match(title);
        return m.Success ? (title[..m.Index].Trim(), m.Groups[1].Value) : (title, "");
    }
    [GeneratedRegex(@",\s*")]
    private static partial Regex CommaList();
    private static string[] SplitList(string value)
    {
        return CommaList().Split(value).Where(s => s.Length > 0).ToArray();
    }

    public static List<AlbumSummary> ParseAlbumList(HtmlNode doc)
    {
        List<AlbumSummary> albums = [];
        HashSet<string> seen = [];
        foreach (HtmlNode? table in doc.Descendants("table").Where(t => t.HasClass("albumList") || t.Id == "top40").ToList())
        {
            List<HtmlNode> rows = table.Descendants("tr").ToList();
            HtmlNode? headerRow = rows.FirstOrDefault(r => r.First("th") is not null);
            List<string?> columns = HeaderColumns(headerRow, (label, _) => label switch
            {
                "album" => ["title"],
                "platform" => ["platform"],
                "type" => ["type"],
                "year" => ["year"],
                "#" => ["rank"],
                _ => [],
            });
            foreach (HtmlNode row in rows)
            {
                if (row == headerRow)
                {
                    continue;
                }

                HtmlNode? titleLink = row.First(a => IsAlbumLink(a) && a.Ancestors().Any(p => p.Name == "td" && !p.HasClass("albumIcon")));
                string slug = Formatting.SlugFromUrl(titleLink?.Attr("href"));
                if (titleLink is null || slug.Length == 0 || !seen.Add(slug))
                {
                    continue;
                }

                Dictionary<string, HtmlNode> cells = RowByColumn(CellsOf(row), columns);
                string title = Text(titleLink);
                string year = Text(Cell(cells, "year"));
                if (year.Length == 0)
                {
                    (title, year) = SplitYear(title);
                }

                string catalog = Text(titleLink.Parent?.First("span"));
                if (catalog.StartsWith('['))
                {
                    catalog = catalog[1..];
                }

                if (catalog.EndsWith(']'))
                {
                    catalog = catalog[..^1];
                }

                HtmlNode? platformCell = Cell(cells, "platform");
                string[] platformLinks = platformCell?.Descendants("a").Select(Text).Where(t => t.Length > 0).ToArray() ?? [];
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
    private sealed record InfoLine(string Text, List<string> Links);
    private static List<InfoLine> ParseInfoLines(HtmlNode? p)
    {
        List<InfoLine> lines = [];
        if (p is null)
        {
            return lines;
        }

        StringBuilder text = new();
        List<string> links = [];
        void Flush()
        {
            if (text.ToString().Trim().Length > 0)
            {
                lines.Add(new(text.ToString(), links));
            }

            _ = text.Clear();
            links = [];
        }
        foreach (HtmlNode node in p.ChildNodes)
        {
            if (node.Name == "br")
            {
                Flush();
                continue;
            }
            if (node.Name is "table" or "div")
            {
                continue;
            }

            if (node.IsElement)
            {
                IEnumerable<HtmlNode> anchors = node.Name == "a" ? [node] : node.Descendants("a");
                links.AddRange(anchors.Select(Text).Where(t => t.Length > 0));
                _ = text.Append(node.TextContent);
            }
            else if (node.IsText)
            {
                _ = text.Append(node.Data);
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
    private sealed class AlbumInfo
    {
        public IReadOnlyList<string> Platforms = [];
        public IReadOnlyList<string> Developers = [];
        public IReadOnlyList<string> Publishers = [];
        public readonly Dictionary<string, string> TotalSizes = [];
        public string Year = "", Catalog = "", DateAdded = "", Type = "";
        public int? FileCount;
    }
    private static AlbumInfo ParseAlbumInfo(HtmlNode? p)
    {
        AlbumInfo info = new();
        foreach ((string? rawLine, List<string>? links) in ParseInfoLines(p))
        {
            Match m = InfoLineRegex().Match(HtmlNode.Collapse(rawLine));
            if (!m.Success)
            {
                continue;
            }

            string key = m.Groups[1].Value.Trim().ToLowerInvariant();
            string value = PipeSuffix().Replace(m.Groups[2].Value, "").Trim();
            string[] list = links.Where(l => !ChangeLog().IsMatch(l)).ToArray();
            string[] asList = list.Length > 0 ? list : SplitList(value);
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
                    {
                        info.TotalSizes[size.Groups[2].Value.Trim().ToLowerInvariant()] = size.Groups[1].Value.Trim();
                    }

                    break;
            }
        }
        return info;
    }
    public static Album? ParseAlbum(HtmlNode doc, string url, string? slugOverride = null)
    {
        HtmlNode? page = doc.ById("pageContent");
        HtmlNode? table = doc.First(n => n.Name == "table" && n.Id == "songlist");
        if (page is null || table is null)
        {
            return null;
        }

        string urlSlug = Formatting.SlugFromUrl(url);
        string slug = string.IsNullOrEmpty(slugOverride) ? urlSlug : slugOverride;
        string title = Text(page.First("h2"));
        if (title.Length == 0)
        {
            title = urlSlug;
        }

        HtmlNode? altEl = page.First(n => n.HasClass("albuminfoAlternativeTitles"));
        string[] altTitles = altEl?.ChildNodes.Where(n => n.IsText).Select(n => HtmlNode.Collapse(n.Data ?? "")).Where(t => t.Length > 0).ToArray() ?? [];
        AlbumInfo info = ParseAlbumInfo(page.First(n => n.Name == "p" && string.Equals(n.Attr("align"), "left", StringComparison.OrdinalIgnoreCase)));
        AlbumImage[] images = page.Descendants("a")
            .Where(a => a.Ancestors().Any(n => n.HasClass("albumImage")))
            .Select(a =>
            {
                string href = Formatting.SiteUrl(a.Attr("href"));
                string thumb = ImgSrc(a);
                return new AlbumImage(href, thumb.Length > 0 ? thumb : href);
            })
            .Where(img => img.Full.Length > 0 || img.Thumb.Length > 0)
            .ToArray();
        List<string?> columns = HeaderColumns(table.ById("songlist_header"), (label, span) =>
        {
            return label == "song name"
                ? span >= 2 ? ["title", "duration"] : ["title"]
                : label == "cd" ? ["cd"] : label == "#" ? ["number"] : AudioFormats.Contains(label) ? [$"fmt:{label}"] : [];
        });
        string[] formats = columns.Where(c => c is not null && c.StartsWith("fmt:", StringComparison.Ordinal)).Select(c => c![4..]).ToArray();
        string cover = images.Length > 0 ? images[0].Thumb : "";
        List<Track> tracks = [];
        List<(HtmlNode Link, Dictionary<string, HtmlNode> Cells)> rows = [];
        foreach (HtmlNode row in table.Descendants("tr"))
        {
            if (row.Id is "songlist_header" or "songlist_footer")
            {
                continue;
            }

            HtmlNode? link = row.First(IsAlbumLink);
            if (link is null)
            {
                continue;
            }

            rows.Add((link, RowByColumn(CellsOf(row), columns)));
        }
        AlbumRef albumRef = new() { Slug = slug, Title = title, Thumb = cover, TrackCount = rows.Count };
        foreach ((HtmlNode? link, Dictionary<string, HtmlNode>? cells) in rows)
        {
            Dictionary<string, string> sizes = [];
            foreach (string? fmt in formats)
            {
                string size = Text(Cell(cells, $"fmt:{fmt}"));
                if (size.Length > 0)
                {
                    sizes[fmt] = size;
                }
            }
            string path = link.Attr("href") ?? "";
            string duration = Text(Cell(cells, "duration"));
            string number = Text(Cell(cells, "number"));
            if (number.EndsWith('.'))
            {
                number = number[..^1];
            }

            string trackTitle = Text(Cell(cells, "title"));
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
        string footerLength = table.Descendants()
            .Where(n => n.Id == "songlist_footer")
            .SelectMany(n => n.Descendants("th"))
            .Select(Text)
            .FirstOrDefault(t => FooterLength().IsMatch(t)) ?? "";
        List<AlbumSummary> related = [];
        HashSet<string> seen = [urlSlug];
        foreach (HtmlNode? td in page.Descendants("td").Where(td => td.HasClass("albumIconLarge")))
        {
            HtmlNode? a = td.First(IsAlbumLink);
            string relSlug = Formatting.SlugFromUrl(a?.Attr("href"));
            if (a is null || relSlug.Length == 0 || !seen.Add(relSlug))
            {
                continue;
            }

            string relTitle = Text(a.First("p"));
            if (relTitle.Length == 0)
            {
                relTitle = Text(a);
            }

            (string? cleanTitle, string? year) = SplitYear(relTitle);
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
        Dictionary<string, string> urls = [];
        foreach (Match m in AudioLink().Matches(html))
        {
            string href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            if (KhinsiderClient.SecureMediaUrl(href) is { } secure)
            {
                _ = urls.TryAdd(m.Groups[2].Value.ToLowerInvariant(), secure);
            }
        }
        return urls;
    }
}