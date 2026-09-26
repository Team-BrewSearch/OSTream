using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OSTream.Controls;
using OSTream.Core;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
namespace OSTream.Services;
public sealed record TrackMenuContext(bool InAlbum = false, string? PlaylistId = null, int Index = 0, int Count = 0);
public sealed class UserActions(AppServices app)
{
    public async Task PlayAlbumAsync(string slug, bool shuffle = false)
    {
        try
        {
            var album = await app.Catalog.GetAlbumAsync(slug);
            app.Player.PlayList(album.Tracks, 0, shuffle ? true : null);
        }
        catch (Exception ex) when (ex is KhinsiderException or OperationCanceledException)
        {
            app.Toasts.Error(ex, "Could not load the album");
        }
    }
    public void ToggleFavorite(Track track)
    {
        var on = app.Library.ToggleFavTrack(track);
        app.Toasts.Toast(on ? $"Added “{track.Title}” to favorites" : $"Removed “{track.Title}” from favorites", on ? ToastKind.Success : ToastKind.Info);
    }
    public void ToggleFavorite(Album album)
    {
        var on = app.Library.ToggleFavAlbum(album.ToSummary());
        app.Toasts.Toast(on ? $"Added “{album.Title}” to favorites" : $"Removed “{album.Title}” from favorites", on ? ToastKind.Success : ToastKind.Info);
    }
    public async void NewPlaylistWith(IReadOnlyList<Track> tracks)
    {
        try
        {
            var name = await app.Dialogs.PromptAsync("New playlist", placeholder: "My playlist", confirm: "Create");
            if (name is null) return;
            var playlist = app.Library.CreatePlaylist(name, tracks);
            app.Toasts.Toast($"Created “{playlist.Name}”", ToastKind.Success, Formatting.Plural(tracks.Count, "track"),
                new ToastAction("View", () => app.Navigator.Navigate($"/playlists/{playlist.Id}")));
        }
        catch (Exception ex)
        {
            app.Toasts.Error(ex, "Could not create playlist");
        }
    }
    public IReadOnlyList<MenuEntry> PlaylistSubmenu(IReadOnlyList<Track> tracks)
    {
        var playlists = app.Library.Value.Playlists;
        var entries = new List<MenuEntry> { new("New playlist…", IconKind.Plus, () => NewPlaylistWith(tracks)) };
        if (playlists.Count > 0) entries.Add(MenuEntry.Separator);
        entries.AddRange(playlists.Select(p => new MenuEntry(p.Name, IconKind.Book, () =>
        {
            var added = app.Library.AddToPlaylist(p.Id, tracks);
            app.Toasts.Toast(added > 0 ? $"Added {Formatting.Plural(added, "track")} to “{p.Name}”" : $"Already in “{p.Name}”",
                added > 0 ? ToastKind.Success : ToastKind.Info);
        }, Hint: p.Tracks.Count.ToString(CultureInfo.InvariantCulture))));
        return entries;
    }
    public void OpenTrackMenu(object anchor, Track track, TrackMenuContext context)
    {
        var fav = app.Library.IsFavTrack(track.Id);
        var items = new List<MenuEntry>
        {
            new("Play next", IconKind.PlayNext, () =>
            {
                app.Player.PlayNext([track]);
                app.Toasts.Toast($"“{track.Title}” plays next");
            }),
            new("Add to up next", IconKind.QueueAdd, () =>
            {
                app.Player.AddToQueue([track]);
                app.Toasts.Toast($"Added “{track.Title}” to up next");
            }),
            new("Add to playlist", IconKind.Book, Submenu: () => PlaylistSubmenu([track])),
            MenuEntry.Separator,
            new(fav ? "Remove from favorites" : "Add to favorites", fav ? IconKind.HeartFill : IconKind.Heart, () => ToggleFavorite(track)),
            new("Download", IconKind.Download, () => app.Downloads.DownloadTrack(track)),
        };
        if (context.PlaylistId is { } playlistId)
        {
            items.Add(MenuEntry.Separator);
            if (context.Index > 0) items.Add(new("Move up", IconKind.Up, () => app.Library.MoveInPlaylist(playlistId, context.Index, context.Index - 1)));
            if (context.Index < context.Count - 1) items.Add(new("Move down", IconKind.Down, () => app.Library.MoveInPlaylist(playlistId, context.Index, context.Index + 1)));
            items.Add(new("Remove from playlist", IconKind.Trash, () => app.Library.RemoveFromPlaylist(playlistId, context.Index), Danger: true));
        }
        items.Add(MenuEntry.Separator);
        if (!context.InAlbum && track.Album.Slug.Length > 0)
            items.Add(new("Go to album", IconKind.ChevronRight, () => app.Navigator.Navigate(AlbumRoute(track.Album.Slug))));
        items.Add(new("Open on KHInsider", IconKind.External, () => _ = app.Shell.OpenUrl(Formatting.SiteUrl(track.Path))));
        app.Menus.Open(anchor, items);
    }
    public void OpenAlbumMenu(object anchor, Album album) => app.Menus.Open(anchor,
    [
        new("Play next", IconKind.PlayNext, () =>
        {
            app.Player.PlayNext(album.Tracks);
            app.Toasts.Toast($"“{album.Title}” plays next");
        }),
        new("Add to up next", IconKind.QueueAdd, () =>
        {
            app.Player.AddToQueue(album.Tracks);
            app.Toasts.Toast($"Added {Formatting.Plural(album.Tracks.Count, "track")} to up next");
        }),
        new("Add to playlist", IconKind.Book, Submenu: () => PlaylistSubmenu(album.Tracks)),
        MenuEntry.Separator,
        new("Copy link", IconKind.Link, () => _ = app.Shell.CopyText(Formatting.AlbumUrl(album.Slug))),
        new("Open on KHInsider", IconKind.External, () => _ = app.Shell.OpenUrl(Formatting.AlbumUrl(album.Slug))),
    ]);
    public static string AlbumRoute(string slug) => "/album/" + Formatting.EncodeUriComponent(slug);
}