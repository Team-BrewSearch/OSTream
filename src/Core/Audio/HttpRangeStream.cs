using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Core.Audio;

public sealed class HttpRangeStream : Stream
{
    public const int BlockSize = 256 * 1024;
    public const long MaxAhead = 4L * 1024 * 1024;
    public const long KeepBehind = 1L * 1024 * 1024;
    private const long ReachDistance = 512 * 1024;
    private const int MaxRetries = 3;
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _http;
    private readonly Func<HttpRequestMessage> _newRequest;
    private readonly byte[]?[] _blocks;
    private readonly int[] _filled;
    private readonly Stack<byte[]> _spare = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wakeDownloader = new(0, 1);
    private readonly CancellationTokenSource _dispose = new();
    private readonly TimeSpan _idleTimeout;
    private long _position;
    private long _readPosition;
    private long _cursor;
    private long _restartAt = -1;
    private int _evictFrom;
    private Exception? _fault;
    private bool _interrupt;
    private int _failures;
    private HttpRangeStream(HttpClient http, Func<HttpRequestMessage> newRequest, long length, TimeSpan idleTimeout)
    {
        _http = http;
        _newRequest = newRequest;
        _idleTimeout = idleTimeout;
        Length = length;
        int count = (int)((length + BlockSize - 1) / BlockSize);
        if (count > 100_000)
        {
            throw new IOException("Audio stream too large to buffer.");
        }

        _blocks = new byte[count][];
        _filled = new int[count];
    }
    public static Task<HttpRangeStream> OpenAsync(HttpClient http, Func<HttpRequestMessage> newRequest, CancellationToken ct)
    {
        return OpenAsync(http, newRequest, ct, DefaultIdleTimeout);
    }

    internal static async Task<HttpRangeStream> OpenAsync(HttpClient http, Func<HttpRequestMessage> newRequest, CancellationToken ct, TimeSpan idleTimeout)
    {
        HttpResponseMessage response = await SendAsync(http, newRequest, 0, ct).ConfigureAwait(false);
        try
        {
            long length = TotalLength(response);
            if (length <= 0)
            {
                throw new IOException("The audio stream has no known length.");
            }

            HttpRangeStream stream = new(http, newRequest, length, idleTimeout);
            _ = Task.Run(() => stream.DownloadLoopAsync(response), CancellationToken.None);
            return stream;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<HttpRequestMessage> newRequest, long offset, CancellationToken ct)
    {
        using HttpRequestMessage request = newRequest();
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
        HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
        {
            int status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"The audio server returned HTTP {status}.", null, (HttpStatusCode)status);
        }
        return response;
    }
    private static long TotalLength(HttpResponseMessage response)
    {
        return response.Content.Headers.ContentRange?.Length
        ?? (response.StatusCode == HttpStatusCode.OK ? response.Content.Headers.ContentLength ?? -1 : -1);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; }
    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, Length);
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        return Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => Length + offset,
        };
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0 || _position >= Length)
        {
            return 0;
        }

        int block = (int)(_position / BlockSize);
        int inBlock = (int)(_position % BlockSize);
        lock (_gate)
        {
            _readPosition = _position;
            while (true)
            {
                ObjectDisposedException.ThrowIf(_dispose.IsCancellationRequested, this);
                if (_interrupt)
                {
                    _interrupt = false;
                    throw new OperationCanceledException("Read interrupted.");
                }
                if (_filled[block] > inBlock)
                {
                    break;
                }

                if (_fault is not null)
                {
                    throw new IOException("The audio stream could not be loaded.", _fault);
                }

                bool needsRestart = _position < _cursor || _position - _cursor > ReachDistance;
                if (needsRestart && _restartAt != (long)block * BlockSize)
                {
                    _restartAt = (long)block * BlockSize;
                    WakeDownloader();
                }
                _ = Monitor.Wait(_gate, 250);
            }
            int available = Math.Min(_filled[block] - inBlock, buffer.Length);
            _blocks[block].AsSpan(inBlock, available).CopyTo(buffer);
            _position += available;
            _readPosition = _position;
            Evict();
            WakeDownloader();
            return available;
        }
    }
    public void Interrupt()
    {
        lock (_gate)
        {
            _interrupt = true;
            Monitor.PulseAll(_gate);
        }
    }
    public void ClearInterrupt()
    {
        lock (_gate)
        {
            _interrupt = false;
        }
    }
    public long BufferedFrom(long position)
    {
        lock (_gate)
        {
            long end = Math.Clamp(position, 0, Length);
            while (end < Length)
            {
                int block = (int)(end / BlockSize);
                int inBlock = (int)(end % BlockSize);
                if (_filled[block] <= inBlock)
                {
                    break;
                }

                end += _filled[block] - inBlock;
                if (_filled[block] < Math.Min(BlockSize, Length - ((long)block * BlockSize)))
                {
                    break;
                }
            }
            return end - position;
        }
    }
    private void WakeDownloader()
    {
        if (_wakeDownloader.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _ = _wakeDownloader.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }
    private async Task<long> WaitForRestartAsync(CancellationToken ct)
    {
        while (true)
        {
            lock (_gate)
            {
                if (_restartAt >= 0)
                {
                    return TakeRestart();
                }
            }

            _ = await _wakeDownloader.WaitAsync(500, ct).ConfigureAwait(false);
        }
    }
    private void Evict()
    {
        int keepFrom = (int)(Math.Max(0, _readPosition - KeepBehind) / BlockSize);
        for (int i = _evictFrom; i < keepFrom; i++)
        {
            if (_blocks[i] is { Length: BlockSize } spare)
            {
                _spare.Push(spare);
                while (_spare.Count > 32)
                {
                    _ = _spare.TryPop(out _);
                }
            }
            _blocks[i] = null;
            _filled[i] = 0;
        }
        _evictFrom = Math.Max(_evictFrom, keepFrom);
    }
    private byte[] NewBlock(int size)
    {
        return size == BlockSize && _spare.TryPop(out byte[]? spare) ? spare : new byte[size];
    }

    private async Task DownloadLoopAsync(HttpResponseMessage first)
    {
        CancellationToken ct = _dispose.Token;
        HttpResponseMessage? response = first;
        long offset = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                response ??= await SendAsync(_http, _newRequest, offset, ct).ConfigureAwait(false);
                long skip = response.StatusCode == HttpStatusCode.OK ? offset : 0;
                await using Stream body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                long restarted = await PumpAsync(body, offset, skip, ct).ConfigureAwait(false);
                response.Dispose();
                response = null;
                offset = restarted >= 0 ? restarted : await WaitForRestartAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                response?.Dispose();
                response = null;
                bool giveUp;
                lock (_gate)
                {
                    offset = _restartAt >= 0 ? TakeRestart() : _cursor / BlockSize * BlockSize;
                    giveUp = ++_failures > MaxRetries;
                    if (giveUp)
                    {
                        _fault = ex;
                        Monitor.PulseAll(_gate);
                    }
                }
                if (giveUp)
                {
                    break;
                }

                try
                {
                    int baseMs = 500 * (1 << (_failures - 1));
                    double jitter = baseMs * (0.8 + (Random.Shared.NextDouble() * 0.4));
                    await Task.Delay(TimeSpan.FromMilliseconds(jitter), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        response?.Dispose();
    }
    private long TakeRestart()
    {
        long at = Math.Max(0, _restartAt);
        _restartAt = -1;
        _cursor = at;
        return at;
    }
    private async Task<long> PumpAsync(Stream body, long offset, long skip, CancellationToken ct)
    {
        byte[] scratch = new byte[16 * 1024];
        while (skip > 0)
        {
            int read = await body.ReadAsync(scratch.AsMemory(0, (int)Math.Min(scratch.Length, skip)), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("The audio stream ended early.");
            }

            skip -= read;
        }
        lock (_gate)
        {
            _cursor = offset;
        }

        while (_cursor < Length)
        {
            int block, inBlock, room;
            byte[] target;
            bool keep;
            while (true)
            {
                lock (_gate)
                {
                    if (_restartAt >= 0)
                    {
                        return TakeRestart();
                    }

                    if (_cursor - _readPosition <= MaxAhead)
                    {
                        break;
                    }
                }
                _ = await _wakeDownloader.WaitAsync(250, ct).ConfigureAwait(false);
            }
            lock (_gate)
            {
                block = (int)(_cursor / BlockSize);
                inBlock = (int)(_cursor % BlockSize);
                int size = (int)Math.Min(BlockSize, Length - ((long)block * BlockSize));
                keep = _blocks[block] is not null && _filled[block] == size;
                if (!keep && inBlock == 0)
                {
                    _filled[block] = 0;
                }

                target = keep ? scratch : _blocks[block] ??= NewBlock(size);
                _evictFrom = Math.Min(_evictFrom, block);
                room = keep ? Math.Min(scratch.Length, size - inBlock) : size - inBlock;
            }
            int read;
            using (CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                idle.CancelAfter(_idleTimeout);
                read = await body.ReadAsync(target.AsMemory(keep ? 0 : inBlock, room), idle.Token).ConfigureAwait(false);
            }
            if (read == 0)
            {
                throw new IOException("The audio stream ended early.");
            }

            _failures = 0;
            lock (_gate)
            {
                if (!keep && !ReferenceEquals(_blocks[block], target))
                {
                    return _restartAt >= 0 ? TakeRestart() : _readPosition / BlockSize * BlockSize;
                }

                if (!keep)
                {
                    _filled[block] = inBlock + read;
                }

                _cursor += read;
                Monitor.PulseAll(_gate);
            }
        }
        return -1;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_dispose.IsCancellationRequested)
        {
            _dispose.Cancel();
            lock (_gate)
            {
                Array.Clear(_blocks);
                _spare.Clear();
                Monitor.PulseAll(_gate);
            }
            WakeDownloader();
        }
        base.Dispose(disposing);
    }
    public override void Flush() { }
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}