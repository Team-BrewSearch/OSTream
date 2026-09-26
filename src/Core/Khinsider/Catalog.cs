using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OSTream.Core.Caching;
using OSTream.Core.Html;
using OSTream.Core.Models;
namespace OSTream.Core.Khinsider;
public sealed class Catalog(KhinsiderClient client)
{
    readonly LruCache<string, Album> _albums = new(Config.AlbumCacheSize);
    readonly SingleFlight<Album> _pending = new();
    readonly LruCache<string, IReadOnlyList<AlbumSummary>> _lists = new(16, ttl: Config.PageTtl);
    void Remember(Album album) => _albums.Set(album.Slug, album);
    public Album? Cached(string? slug) => !string.IsNullOrEmpty(slug) && _albums.TryGet(slug, out var album) ? album : null;
    public Task<Album> GetAlbumAsync(string slug, CancellationToken ct = default)
    {
        if (Cached(slug) is { } hit) return Task.FromResult(hit);
        var task = _pending.Run(slug, LoadAlbumAsync);
        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }
    async Task<Album> LoadAlbumAsync(string slug)
    {
        var (finalUrl, html) = await client.GetPageAsync(Formatting.AlbumPath(slug)).ConfigureAwait(false);
        var album = Parse(() => Parsers.ParseAlbum(HtmlParser.Parse(html), finalUrl, slug))
                    ?? throw new KhinsiderException("This album has no track list.");
        Remember(album);
        return album;
    }
    public Task<IReadOnlyList<AlbumSummary>> SearchAsync(string query, CancellationToken ct = default) =>
        GetAlbumListAsync($"/search?search={Formatting.EncodeUriComponent(query)}", ct);
    public Task<IReadOnlyList<AlbumSummary>> GetListAsync(string id, CancellationToken ct = default) =>
        GetAlbumListAsync(Config.ListFor(id).Path, ct);
    async Task<IReadOnlyList<AlbumSummary>> GetAlbumListAsync(string path, CancellationToken ct)
    {
        if (_lists.TryGet(path, out var cached)) return cached;
        var (_, html) = await client.GetPageAsync(path, ct).ConfigureAwait(false);
        var albums = Parse(() => Parsers.ParseAlbumList(HtmlParser.Parse(html)));
        if (albums.Count > 0) _lists.Set(path, albums);
        return albums;
    }
    public async Task<string> RandomAlbumSlugAsync(CancellationToken ct = default)
    {
        var (finalUrl, html) = await client.GetPageAsync("/random-album", ct).ConfigureAwait(false);
        var slug = Formatting.SlugFromUrl(finalUrl);
        if (slug.Length > 0 && Parse(() => Parsers.ParseAlbum(HtmlParser.Parse(html), finalUrl, slug)) is { } album) Remember(album);
        return slug;
    }
    static T Parse<T>(Func<T> parse)
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