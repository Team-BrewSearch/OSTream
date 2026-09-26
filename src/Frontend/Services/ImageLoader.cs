using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using OSTream.Core;
using OSTream.Core.Caching;
using OSTream.Core.Khinsider;
using OSTream.Core.Storage;
namespace OSTream.Services;
public sealed class ImageLoader : IDisposable
{
    const long MemoryBudget = 16L * 1024 * 1024;
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan StaleTemp = TimeSpan.FromMinutes(1);
    readonly HttpClient _http;
    readonly string _dir;
    readonly long _diskBudget;
    readonly int _trimEvery;
    readonly LruCache<string, Bitmap> _memory = new(MemoryBudget, bitmap => (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
    readonly SingleFlight<Bitmap?> _pending = new();
    readonly SingleFlight<byte[]?> _downloads = new();
    readonly SemaphoreSlim _throttle = new(6);
    readonly SemaphoreSlim _decoders = new(2);
    readonly CancellationTokenSource _trimCts = new();
    readonly CancellationToken _trimToken;
    readonly Lock _trimLock = new();
    Task _trimTask;
    int _writes;
    volatile bool _disposed;
    public ImageLoader(HttpClient http, string cacheDir) : this(http, cacheDir, 256L * 1024 * 1024, 200)
    {
    }
    internal ImageLoader(HttpClient http, string cacheDir, long diskBudget, int trimEvery)
    {
        _http = http;
        _dir = Path.Combine(cacheDir, "images");
        _diskBudget = diskBudget;
        _trimEvery = trimEvery;
        _trimToken = _trimCts.Token;
        _trimTask = Task.Run(() => TrimDisk(_trimToken));
    }
    internal Task Trimmed
    {
        get
        {
            lock (_trimLock) return _trimTask;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trimCts.Cancel();
        try
        {
            Trimmed.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _trimCts.Dispose();
    }
    void ScheduleTrim()
    {
        if (_disposed) return;
        lock (_trimLock) _trimTask = _trimTask.ContinueWith(_ => TrimDisk(_trimToken), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
    static int Bucket(int width) => Math.Clamp((width + 63) / 64 * 64, 64, 2048);
    static string Key(string url, int width) => $"{width}|{url}";
    public Bitmap? Peek(string url, int width) => _memory.TryGet(Key(url, Bucket(width)), out var bitmap) ? bitmap : null;
    public Task<Bitmap?> LoadAsync(string url, int width)
    {
        width = Bucket(width);
        if (Peek(url, width) is { } hit) return Task.FromResult<Bitmap?>(hit);
        return _pending.Run(Key(url, width), _ => LoadCoreAsync(url, width));
    }
    async Task<Bitmap?> LoadCoreAsync(string url, int width)
    {
        var bytes = await BytesAsync(url).ConfigureAwait(false);
        if (bytes is null) return null;
        var bitmap = await Task.Run(async () =>
        {
            await _decoders.WaitAsync().ConfigureAwait(false);
            try
            {
                using var stream = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                _decoders.Release();
            }
        }).ConfigureAwait(false);
        if (bitmap is not null && (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4 <= MemoryBudget / 4) _memory.Set(Key(url, width), bitmap);
        return bitmap;
    }
    string CachePath(string url) => Path.Combine(_dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))));
    public string? CachedFile(string url)
    {
        var path = CachePath(url);
        return File.Exists(path) ? path : null;
    }
    public async Task<string?> EnsureFileAsync(string url)
    {
        if (CachedFile(url) is { } path) return path;
        return await BytesAsync(url).ConfigureAwait(false) is null ? null : CachedFile(url);
    }
    Task<byte[]?> BytesAsync(string url) => _downloads.Run(url, DownloadAsync);
    async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            var path = CachePath(url);
            if (File.Exists(path))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }
            if (KhinsiderClient.SecureMediaUrl(url) is not { } secure) return null;
            await _throttle.WaitAsync().ConfigureAwait(false);
            try
            {
                using var cts = new CancellationTokenSource(Timeout);
                using var request = KhinsiderClient.MediaRequest(secure);
                using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    Directory.CreateDirectory(_dir);
                    await AtomicFile.WriteAsync(path, stream => stream.WriteAsync(bytes).AsTask()).ConfigureAwait(false);
                    if (Interlocked.Increment(ref _writes) % _trimEvery == 0) ScheduleTrim();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
                return bytes;
            }
            finally
            {
                _throttle.Release();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }
    void TrimDisk(CancellationToken ct)
    {
        FileInfo[] files;
        try
        {
            if (ct.IsCancellationRequested || !Directory.Exists(_dir)) return;
            files = new DirectoryInfo(_dir).GetFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        var now = DateTime.UtcNow;
        long total = 0;
        foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc))
        {
            if (ct.IsCancellationRequested) return;
            bool drop;
            if (file.Name.EndsWith(AtomicFile.TempSuffix, StringComparison.Ordinal) || file.Name.EndsWith(".tmp", StringComparison.Ordinal))
                drop = now - file.LastWriteTimeUtc >= StaleTemp;
            else
            {
                total += file.Length;
                drop = total > _diskBudget;
            }
            if (drop) AtomicFile.TryDelete(file.FullName);
        }
    }
}