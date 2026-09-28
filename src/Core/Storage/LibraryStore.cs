using OSTream.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace OSTream.Core.Storage;

public sealed class LibraryStore : IDisposable
{
    private HashSet<string> _favTrackIds = [];
    private HashSet<string> _favAlbumSlugs = [];
    public LibraryStore() : this(Path.Combine(AppPaths.ConfigDir, "library.json")) { }
    public LibraryStore(string path)
    {
        Store = new JsonStore<LibraryData>(path, JsonContext.Default.LibraryData, new LibraryData(), Normalize);
        Index(Value);
    }
    internal JsonStore<LibraryData> Store { get; }
    public LibraryData Value => Store.Value;
    private static LibraryData Normalize(LibraryData d)
    {
        return new()
        {
            FavAlbums = d.FavAlbums?.OfType<AlbumSummary>().Select(NormalizeAlbum).ToArray() ?? [],
            FavTracks = NormalizeTracks(d.FavTracks),
            Recent = NormalizeTracks(d.Recent),
            Playlists = d.Playlists?.OfType<Playlist>()
            .Select(p => p with { Id = p.Id ?? "", Name = p.Name ?? "", Tracks = NormalizeTracks(p.Tracks) }).ToArray() ?? [],
        };
    }

    private static Track[] NormalizeTracks(IReadOnlyList<Track>? tracks)
    {
        return tracks?.OfType<Track>().Select(NormalizeTrack).ToArray() ?? [];
    }

    private static Track NormalizeTrack(Track t)
    {
        return t with
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
    }

    private static AlbumSummary NormalizeAlbum(AlbumSummary a)
    {
        return a with
        {
            Slug = a.Slug ?? "",
            Title = a.Title ?? "",
            Thumb = a.Thumb ?? "",
            Type = a.Type ?? "",
            Year = a.Year ?? "",
            Catalog = a.Catalog ?? "",
            Platforms = a.Platforms?.OfType<string>().ToArray() ?? [],
        };
    }

    public event Action<LibraryData, LibraryData>? Changed;
    private void Set(Func<LibraryData, LibraryData> patch)
    {
        LibraryData old = Value;
        LibraryData next = patch(old);
        if (EqualityComparer<LibraryData>.Default.Equals(next, old))
        {
            return;
        }

        Store.Set(next);
        Changed?.Invoke(old, next);
    }
    private void Index(LibraryData data)
    {
        _favTrackIds = data.FavTracks.Select(t => t.Id).ToHashSet();
        _favAlbumSlugs = data.FavAlbums.Select(a => a.Slug).ToHashSet();
    }
    public static Track Slim(Track t)
    {
        return t with
        {
            Id = t.Id.Length > 0 ? t.Id : t.Path,
            Index = 0,
            PlayedAt = null,
        };
    }

    private static AlbumSummary Slim(AlbumSummary a)
    {
        return new()
        {
            Slug = a.Slug,
            Title = a.Title,
            Thumb = a.Thumb,
            Year = a.Year,
            Platforms = a.Platforms,
            Type = a.Type,
        };
    }

    public bool IsFavAlbum(string slug)
    {
        return _favAlbumSlugs.Contains(slug);
    }

    public bool IsFavTrack(string id)
    {
        return _favTrackIds.Contains(id);
    }

    public bool ToggleFavAlbum(AlbumSummary album)
    {
        bool on = !IsFavAlbum(album.Slug);
        _ = on ? _favAlbumSlugs.Add(album.Slug) : _favAlbumSlugs.Remove(album.Slug);

        Set(d => d with { FavAlbums = on ? [Slim(album), .. d.FavAlbums] : d.FavAlbums.Where(a => a.Slug != album.Slug).ToArray() });
        return on;
    }
    public bool ToggleFavTrack(Track track)
    {
        Track slim = Slim(track);
        bool on = !IsFavTrack(slim.Id);
        _ = on ? _favTrackIds.Add(slim.Id) : _favTrackIds.Remove(slim.Id);

        Set(d => d with { FavTracks = on ? [slim, .. d.FavTracks] : d.FavTracks.Where(t => t.Id != slim.Id).ToArray() });
        return on;
    }
    public void AddRecent(Track track)
    {
        Track item = Slim(track) with { PlayedAt = Formatting.NowMs() };
        Set(d => d with { Recent = d.Recent.Where(t => t.Id != item.Id).Prepend(item).Take(Config.RecentLimit).ToArray() });
    }
    public void ClearRecent()
    {
        Set(d => d.Recent.Count == 0 ? d : d with { Recent = [] });
    }

    public Playlist? GetPlaylist(string? id)
    {
        return id is null ? null : Value.Playlists.FirstOrDefault(p => p.Id == id);
    }

    private void UpdatePlaylist(string id, Func<Playlist, Playlist?> change)
    {
        Set(d =>
        {
            bool changed = false;
            Playlist[] playlists = d.Playlists.Select(p =>
            {
                if (p.Id != id || change(p) is not { } next)
                {
                    return p;
                }

                changed = true;
                return next with { UpdatedAt = Formatting.NowMs() };
            }).ToArray();
            return changed ? d with { Playlists = playlists } : d;
        });
    }

    public Playlist CreatePlaylist(string name, IEnumerable<Track>? tracks = null)
    {
        long now = Formatting.NowMs();
        Playlist playlist = new()
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
    public void RenamePlaylist(string id, string name)
    {
        UpdatePlaylist(id, p => p with { Name = name.Trim() is { Length: > 0 } trimmed ? trimmed : "Untitled playlist" });
    }

    public void DeletePlaylist(string id)
    {
        Set(d => d with { Playlists = d.Playlists.Where(p => p.Id != id).ToArray() });
    }

    public int AddToPlaylist(string id, IEnumerable<Track> tracks)
    {
        Playlist? playlist = GetPlaylist(id);
        if (playlist is null)
        {
            return 0;
        }

        HashSet<string> existing = playlist.Tracks.Select(t => t.Id).ToHashSet();
        Track[] fresh = tracks.Select(Slim).Where(t => existing.Add(t.Id)).ToArray();
        if (fresh.Length > 0)
        {
            UpdatePlaylist(id, p => p with { Tracks = [.. p.Tracks, .. fresh] });
        }

        return fresh.Length;
    }
    public void RemoveFromPlaylist(string id, int index)
    {
        UpdatePlaylist(id, p => index < 0 || index >= p.Tracks.Count ? null : p with { Tracks = p.Tracks.Where((_, i) => i != index).ToArray() });
    }

    public void MoveInPlaylist(string id, int from, int to)
    {
        UpdatePlaylist(id, p =>
        {
            if (to < 0 || to >= p.Tracks.Count || from < 0 || from >= p.Tracks.Count)
            {
                return null;
            }

            List<Track> tracks = p.Tracks.ToList();
            Track item = tracks[from];
            tracks.RemoveAt(from);
            tracks.Insert(to, item);
            return p with { Tracks = tracks };
        });
    }

    public void Dispose()
    {
        Store.Dispose();
    }
}