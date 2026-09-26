using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace OSTream.Core.Models;

public sealed record AlbumSummary
{
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Thumb { get; init; } = "";
    public IReadOnlyList<string> Platforms { get; init; } = [];
    public string Type { get; init; } = "";
    public string Year { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Catalog { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Rank { get; init; }
}
public sealed record AlbumRef
{
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Thumb { get; init; } = "";
    public int TrackCount { get; init; }
}
public sealed record Track
{
    public string Id { get; init; } = "";
    public string Path { get; init; } = "";
    [JsonIgnore]
    public int Index { get; init; }
    public string Cd { get; init; } = "";
    public string Number { get; init; } = "";
    public string Title { get; init; } = "";
    public string Duration { get; init; } = "";
    public int DurationSec { get; init; }
    public IReadOnlyDictionary<string, string> Sizes { get; init; } = new Dictionary<string, string>();
    public AlbumRef Album { get; init; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? PlayedAt { get; init; }
}
public sealed record AlbumImage(string Full, string Thumb);
public sealed record Album
{
    public string Slug { get; init; } = "";
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<string> AltTitles { get; init; } = [];
    public IReadOnlyList<AlbumImage> Images { get; init; } = [];
    public string Cover { get; init; } = "";
    public string CoverFull { get; init; } = "";
    public IReadOnlyList<string> Platforms { get; init; } = [];
    public IReadOnlyList<string> Developers { get; init; } = [];
    public IReadOnlyList<string> Publishers { get; init; } = [];
    public IReadOnlyDictionary<string, string> TotalSizes { get; init; } = new Dictionary<string, string>();
    public string Year { get; init; } = "";
    public string Catalog { get; init; } = "";
    public int? FileCount { get; init; }
    public string DateAdded { get; init; } = "";
    public string Type { get; init; } = "";
    public IReadOnlyList<string> Formats { get; init; } = [];
    public bool HasCd { get; init; }
    public IReadOnlyList<Track> Tracks { get; init; } = [];
    public int TotalSec { get; init; }
    public string TotalLength { get; init; } = "";
    public IReadOnlyList<AlbumSummary> Related { get; init; } = [];
    public string Thumb => Cover;
    public AlbumSummary ToSummary()
    {
        return new()
        {
            Slug = Slug,
            Title = Title,
            Thumb = Cover,
            Year = Year,
            Platforms = Platforms,
            Type = Type,
        };
    }
}