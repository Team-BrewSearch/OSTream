using System.Collections.Generic;
namespace OSTream.Core.Models;
public sealed record Playlist
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<Track> Tracks { get; init; } = [];
    public long CreatedAt { get; init; }
    public long UpdatedAt { get; init; }
}
public sealed record LibraryData
{
    public IReadOnlyList<AlbumSummary> FavAlbums { get; init; } = [];
    public IReadOnlyList<Track> FavTracks { get; init; } = [];
    public IReadOnlyList<Track> Recent { get; init; } = [];
    public IReadOnlyList<Playlist> Playlists { get; init; } = [];
}