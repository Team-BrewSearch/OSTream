using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OSTream.Core.Models;
namespace OSTream.Core.Storage;
public sealed class LibraryStore : IDisposable
{
    readonly JsonStore<LibraryData> _store;
    HashSet<string> _favTrackIds = [];
    HashSet<string> _favAlbumSlugs = [];
    public LibraryStore() : this(Path.Combine(AppPaths.ConfigDir, "library.json")) { }
    public LibraryStore(string path)
    {
        _store = new JsonStore<LibraryData>(path, JsonContext.Default.LibraryData, new LibraryData(), Normalize);
        Index(Value);
    }
    internal JsonStore<LibraryData> Store => _store;
    public LibraryData Value => _store.Value;
    static LibraryData Normalize(LibraryData d) => new()
    {
        FavAlbums = d.FavAlbums?.OfType<AlbumSummary>().Select(NormalizeAlbum).ToArray() ?? [],
        FavTracks = NormalizeTracks(d.FavTracks),
        Recent = NormalizeTracks(d.Recent),
        Playlists = d.Playlists?.OfType<Playlist>()
            .Select(p => p with { Id = p.Id ?? "", Name = p.Name ?? "", Tracks = NormalizeTracks(p.Tracks) }).ToArray() ?? [],
    };
    static Track[] NormalizeTracks(IReadOnlyList<Track>? tracks) => tracks?.OfType<Track>().Select(NormalizeTrack).ToArray() ?? [];
    static Track NormalizeTrack(Track t) => t with
    {
        Id = t.Id ?? "",
        Path = t.Path ?? "",
        Cd = t.Cd ?? "",
        Number = t.Number ?? "",
        Title = t.Title ?? "",
        Duration = t.Duration ?? "",
        Sizes = t.Sizes ?? new Dictionary<string, string>(),
        Album = t.Album is { } a ? a with { Slug = a.Slug ?? "", Title = a.Title ?? "", Thumb = a.Thumb ?? "" } : new AlbumRef(),
    };
    static AlbumSummary NormalizeAlbum(AlbumSummary a) => a with
    {
        Slug = a.Slug ?? "",
        Title = a.Title ?? "",
        Thumb = a.Thumb ?? "",
        Type = a.Type ?? "",
        Year = a.Year ?? "",
        Catalog = a.Catalog ?? "",
        Platforms = a.Platforms?.OfType<string>().ToArray() ?? [],
    };
    public event Action<LibraryData, LibraryData>? Changed;
    void Set(Func<LibraryData, LibraryData> patch)
    {
        var old = Value;
        var next = patch(old);
        if (EqualityComparer<LibraryData>.Default.Equals(next, old)) return;
        _store.Set(next);
        Changed?.Invoke(old, next);
    }
    void Index(LibraryData data)
    {
        _favTrackIds = data.FavTracks.Select(t => t.Id).ToHashSet();
        _favAlbumSlugs = data.FavAlbums.Select(a => a.Slug).ToHashSet();
    }
    public static Track Slim(Track t) => t with
    {
        Id = t.Id.Length > 0 ? t.Id : t.Path,
        Index = 0,
        PlayedAt = null,
    };
    static AlbumSummary Slim(AlbumSummary a) => new()
    {
        Slug = a.Slug,
        Title = a.Title,
        Thumb = a.Thumb,
        Year = a.Year,
        Platforms = a.Platforms,
        Type = a.Type,
    };
    public bool IsFavAlbum(string slug) => _favAlbumSlugs.Contains(slug);
    public bool IsFavTrack(string id) => _favTrackIds.Contains(id);
    public bool ToggleFavAlbum(AlbumSummary album)
    {
        var on = !IsFavAlbum(album.Slug);
        if (on) _favAlbumSlugs.Add(album.Slug); else _favAlbumSlugs.Remove(album.Slug);
        Set(d => d with { FavAlbums = on ? [Slim(album), .. d.FavAlbums] : d.FavAlbums.Where(a => a.Slug != album.Slug).ToArray() });
        return on;
    }
    public bool ToggleFavTrack(Track track)
    {
        var slim = Slim(track);
        var on = !IsFavTrack(slim.Id);
        if (on) _favTrackIds.Add(slim.Id); else _favTrackIds.Remove(slim.Id);
        Set(d => d with { FavTracks = on ? [slim, .. d.FavTracks] : d.FavTracks.Where(t => t.Id != slim.Id).ToArray() });
        return on;
    }
    public void AddRecent(Track track)
    {
        var item = Slim(track) with { PlayedAt = Formatting.NowMs() };
        Set(d => d with { Recent = d.Recent.Where(t => t.Id != item.Id).Prepend(item).Take(Config.RecentLimit).ToArray() });
    }
    public void ClearRecent() => Set(d => d.Recent.Count == 0 ? d : d with { Recent = [] });
    public Playlist? GetPlaylist(string? id) => id is null ? null : Value.Playlists.FirstOrDefault(p => p.Id == id);
    void UpdatePlaylist(string id, Func<Playlist, Playlist?> change) => Set(d =>
    {
        var changed = false;
        var playlists = d.Playlists.Select(p =>
        {
            if (p.Id != id || change(p) is not { } next) return p;
            changed = true;
            return next with { UpdatedAt = Formatting.NowMs() };
        }).ToArray();
        return changed ? d with { Playlists = playlists } : d;
    });
    public Playlist CreatePlaylist(string name, IEnumerable<Track>? tracks = null)
    {
        var now = Formatting.NowMs();
        var playlist = new Playlist
        {
            Id = Formatting.NewId(),
            Name = name.Trim() is { Length: > 0 } trimmed ? trimmed : "New playlist",
            Tracks = (tracks ?? []).Select(Slim).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        Set(d => d with { Playlists = [playlist, .. d.Playlists] });
        return playlist;
    }
    public void RenamePlaylist(string id, string name) =>
        UpdatePlaylist(id, p => p with { Name = name.Trim() is { Length: > 0 } trimmed ? trimmed : "Untitled playlist" });
    public void DeletePlaylist(string id) => Set(d => d with { Playlists = d.Playlists.Where(p => p.Id != id).ToArray() });
    public int AddToPlaylist(string id, IEnumerable<Track> tracks)
    {
        var playlist = GetPlaylist(id);
        if (playlist is null) return 0;
        var existing = playlist.Tracks.Select(t => t.Id).ToHashSet();
        var fresh = tracks.Select(Slim).Where(t => existing.Add(t.Id)).ToArray();
        if (fresh.Length > 0) UpdatePlaylist(id, p => p with { Tracks = [.. p.Tracks, .. fresh] });
        return fresh.Length;
    }
    public void RemoveFromPlaylist(string id, int index) =>
        UpdatePlaylist(id, p => index < 0 || index >= p.Tracks.Count ? null : p with { Tracks = p.Tracks.Where((_, i) => i != index).ToArray() });
    public void MoveInPlaylist(string id, int from, int to) => UpdatePlaylist(id, p =>
    {
        if (to < 0 || to >= p.Tracks.Count || from < 0 || from >= p.Tracks.Count) return null;
        var tracks = p.Tracks.ToList();
        var item = tracks[from];
        tracks.RemoveAt(from);
        tracks.Insert(to, item);
        return p with { Tracks = tracks };
    });
    public void Dispose() => _store.Dispose();
}