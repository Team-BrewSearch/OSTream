using Avalonia.Media.Imaging;
using OSTream.Core;
using OSTream.Core.Caching;
using OSTream.Core.Khinsider;
using OSTream.Core.Storage;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Frontend.Services;

public sealed class ImageLoader : IDisposable
{
    private const long MemoryBudget = 16L * 1024 * 1024;
    private const long MaxSourcePixels = MemoryBudget / 4;
    private const int MaxOutputDimension = 2048;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleTemp = TimeSpan.FromMinutes(1);
    private readonly HttpClient _http;
    private readonly string _dir;
    private readonly long _diskBudget;
    private readonly int _trimEvery;
    private readonly LruCache<string, Bitmap> _memory = new(MemoryBudget, bitmap => (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
    private readonly SingleFlight<Bitmap?> _pending = new();
    private readonly SingleFlight<byte[]?> _downloads = new();
    private readonly SemaphoreSlim _throttle = new(6);
    private readonly SemaphoreSlim _decoders = new(2);
    private readonly CancellationTokenSource _trimCts = new();
    private readonly CancellationToken _trimToken;
    private readonly Lock _trimLock = new();
    private Task _trimTask;
    private int _writes;
    private volatile bool _disposed;
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
            lock (_trimLock)
            {
                return _trimTask;
            }
        }
    }
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trimCts.Cancel();
        try
        {
            _ = Trimmed.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _trimCts.Dispose();
    }
    private void ScheduleTrim()
    {
        if (_disposed)
        {
            return;
        }

        lock (_trimLock)
        {
            _trimTask = _trimTask.ContinueWith(_ => TrimDisk(_trimToken), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }
    private static int Bucket(int width)
    {
        return Math.Clamp((width + 63) / 64 * 64, 64, 2048);
    }

    private static string Key(string url, int width)
    {
        return $"{width}|{url}";
    }

    public Bitmap? Peek(string url, int width)
    {
        return _memory.TryGet(Key(url, Bucket(width)), out Bitmap? bitmap) ? bitmap : null;
    }

    public Task<Bitmap?> LoadAsync(string url, int width)
    {
        width = Bucket(width);
        return Peek(url, width) is { } hit ? Task.FromResult<Bitmap?>(hit) : _pending.Run(Key(url, width), _ => LoadCoreAsync(url, width));
    }
    private async Task<Bitmap?> LoadCoreAsync(string url, int width)
    {
        byte[]? bytes = await BytesAsync(url).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        Bitmap? bitmap = await Task.Run(async () =>
        {
            await _decoders.WaitAsync().ConfigureAwait(false);
            try
            {
                int sourceWidth;
                int sourceHeight;
                using (MemoryStream probe = new(bytes))
                using (SKCodec? codec = SKCodec.Create(probe))
                {
                    if (codec is null)
                    {
                        return null;
                    }

                    sourceWidth = codec.Info.Width;
                    sourceHeight = codec.Info.Height;
                }

                // Avalonia's Skia decoder may allocate both a source-sized bitmap and a resized bitmap.
                if (sourceWidth <= 0 || sourceHeight <= 0 || (long)sourceWidth * sourceHeight > MaxSourcePixels)
                {
                    return null;
                }

                int decodeWidth = Math.Min(width, sourceWidth);
                using MemoryStream stream = new(bytes);
                if ((long)sourceHeight * decodeWidth > (long)MaxOutputDimension * sourceWidth)
                {
                    int decodeHeight = Math.Min(sourceHeight, MaxOutputDimension);
                    return (long)sourceWidth * decodeHeight < sourceHeight
                        ? null
                        : Bitmap.DecodeToHeight(stream, decodeHeight, BitmapInterpolationMode.HighQuality);
                }

                return (long)sourceHeight * decodeWidth < sourceWidth
                    ? null
                    : Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                _ = _decoders.Release();
            }
        }).ConfigureAwait(false);
        if (bitmap is not null && (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4 <= MemoryBudget / 4)
        {
            _memory.Set(Key(url, width), bitmap);
        }

        return bitmap;
    }
    private string CachePath(string url)
    {
        return Path.Combine(_dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))));
    }

    public string? CachedFile(string url)
    {
        string path = CachePath(url);
        return File.Exists(path) ? path : null;
    }
    public async Task<string?> EnsureFileAsync(string url)
    {
        return CachedFile(url) is { } path ? path : await BytesAsync(url).ConfigureAwait(false) is null ? null : CachedFile(url);
    }
    private Task<byte[]?> BytesAsync(string url)
    {
        return _downloads.Run(url, DownloadAsync);
    }

    private async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            string path = CachePath(url);
            if (File.Exists(path))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }
            if (KhinsiderClient.SecureMediaUrl(url) is not { } secure)
            {
                return null;
            }

            await _throttle.WaitAsync().ConfigureAwait(false);
            try
            {
                using CancellationTokenSource cts = new(Timeout);
                using HttpRequestMessage request = KhinsiderClient.MediaRequest(secure);
                using HttpResponseMessage response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    _ = Directory.CreateDirectory(_dir);
                    await AtomicFile.WriteAsync(path, stream => stream.WriteAsync(bytes).AsTask()).ConfigureAwait(false);
                    if (Interlocked.Increment(ref _writes) % _trimEvery == 0)
                    {
                        ScheduleTrim();
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
                return bytes;
            }
            finally
            {
                _ = _throttle.Release();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }
    private void TrimDisk(CancellationToken ct)
    {
        FileInfo[] files;
        try
        {
            if (ct.IsCancellationRequested || !Directory.Exists(_dir))
            {
                return;
            }

            files = new DirectoryInfo(_dir).GetFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        DateTime now = DateTime.UtcNow;
        long total = 0;
        foreach (FileInfo? file in files.OrderByDescending(f => f.LastWriteTimeUtc))
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            bool drop;
            if (file.Name.EndsWith(AtomicFile.TempSuffix, StringComparison.Ordinal) || file.Name.EndsWith(".tmp", StringComparison.Ordinal))
            {
                drop = now - file.LastWriteTimeUtc >= StaleTemp;
            }
            else
            {
                total += file.Length;
                drop = total > _diskBudget;
            }
            if (drop)
            {
                AtomicFile.TryDelete(file.FullName);
            }
        }
    }
}
