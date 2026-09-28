using System;
using System.Collections.Generic;
namespace OSTream.Core;

public sealed record AlbumListInfo(string Id, string Label, string Title, string Path, bool Ranked);
public static class Config
{
    public const string AppName = "OSTream";
    public const string BaseUrl = "https://downloads.khinsider.com";
    public const string Host = "downloads.khinsider.com";
    public const string AlbumPath = "/game-soundtracks/album/";
    public static readonly IReadOnlyList<AlbumListInfo> Lists =
    [
        new("latest", "Latest", "Latest additions", "/", false),
        new("top40", "Top 40", "Top 40 this week", "/top40", true),
        new("favorites", "Most favorited", "Most favorited of all time", "/most-favorites", true),
        new("new", "New & popular", "Most popular new albums", "/top-100-newly-added", true),
    ];
    public const int GridPage = 24;
    public const int AlbumCacheSize = 20;
    public const int RecentLimit = 100;
    public const int SearchDebounceMs = 450;
    public const int FilterMinTracks = 20;
    public const int RelatedLimit = 12;
    public const int UpNextLimit = 200;
    public const int MaxToasts = 4;
    public static readonly TimeSpan PageTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TrackUrlTtl = TimeSpan.FromMinutes(20);
    public const int ParallelTracks = 3;
    public const int MaxTracksPerJob = 2000;
    public static readonly string[] MediaHosts = ["downloads.khinsider.com", "vgmtreasurechest.com"];
    public const string UserAgent = "OSTream/1.0 (+https://github.com/Team-BrewSearch/OSTream)";
    public static AlbumListInfo ListFor(string? id)
    {
        foreach (AlbumListInfo list in Lists)
        {
            if (list.Id == id)
            {
                return list;
            }
        }

        return Lists[0];
    }
}