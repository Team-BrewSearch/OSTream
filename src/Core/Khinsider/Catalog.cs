using OSTream.Core.Caching;
using OSTream.Core.Html;
using OSTream.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Core.Khinsider;

public sealed class Catalog(KhinsiderClient client)
{
    private readonly LruCache<string, Album> _albums = new(Config.AlbumCacheSize);
    private readonly SingleFlight<Album> _pending = new();
    private readonly LruCache<string, IReadOnlyList<AlbumSummary>> _lists = new(16, ttl: Config.PageTtl);
    private void Remember(Album album)
    {
        _albums.Set(album.Slug, album);
    }

    public Album? Cached(string? slug)
    {
        return !string.IsNullOrEmpty(slug) && _albums.TryGet(slug, out Album? album) ? album : null;
    }

    public Task<Album> GetAlbumAsync(string slug, CancellationToken ct = default)
    {
        if (Cached(slug) is { } hit)
        {
            return Task.FromResult(hit);
        }

        Task<Album> task = _pending.Run(slug, LoadAlbumAsync);
        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }
    private async Task<Album> LoadAlbumAsync(string slug)
    {
        (string? finalUrl, string? html) = await client.GetPageAsync(Formatting.AlbumPath(slug)).ConfigureAwait(false);
        Album album = Parse(() => Parsers.ParseAlbum(HtmlParser.Parse(html), finalUrl, slug))
                    ?? throw new KhinsiderException("This album has no track list.");
        Remember(album);
        return album;
    }
    public Task<IReadOnlyList<AlbumSummary>> SearchAsync(string query, CancellationToken ct = default)
    {
        return GetAlbumListAsync($"/search?search={Formatting.EncodeUriComponent(query)}", ct);
    }

    public Task<IReadOnlyList<AlbumSummary>> GetListAsync(string id, CancellationToken ct = default)
    {
        return GetAlbumListAsync(Config.ListFor(id).Path, ct);
    }

    private async Task<IReadOnlyList<AlbumSummary>> GetAlbumListAsync(string path, CancellationToken ct)
    {
        if (_lists.TryGet(path, out IReadOnlyList<AlbumSummary>? cached))
        {
            return cached;
        }

        (string _, string? html) = await client.GetPageAsync(path, ct).ConfigureAwait(false);
        List<AlbumSummary> albums = Parse(() => Parsers.ParseAlbumList(HtmlParser.Parse(html)));
        if (albums.Count > 0)
        {
            _lists.Set(path, albums);
        }

        return albums;
    }
    public async Task<string> RandomAlbumSlugAsync(CancellationToken ct = default)
    {
        (string? finalUrl, string? html) = await client.GetPageAsync("/random-album", ct).ConfigureAwait(false);
        string slug = Formatting.SlugFromUrl(finalUrl);
        if (slug.Length > 0 && Parse(() => Parsers.ParseAlbum(HtmlParser.Parse(html), finalUrl, slug)) is { } album)
        {
            Remember(album);
        }

        return slug;
    }
    private static T Parse<T>(Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception ex) when (ex is not KhinsiderException)
        {
            throw new KhinsiderException("KHInsider sent a page OSTream could not read.");
        }
    }
}