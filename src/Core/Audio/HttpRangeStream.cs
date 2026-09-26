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
    const long ReachDistance = 512 * 1024;
    const int MaxRetries = 3;
    static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);
    readonly HttpClient _http;
    readonly Func<HttpRequestMessage> _newRequest;
    readonly byte[]?[] _blocks;
    readonly int[] _filled;
    readonly Stack<byte[]> _spare = new(); 
    readonly object _gate = new();
    readonly SemaphoreSlim _wakeDownloader = new(0, 1);
    readonly CancellationTokenSource _dispose = new();
    readonly TimeSpan _idleTimeout;
    long _position;
    long _readPosition;   
    long _cursor;         
    long _restartAt = -1; 
    int _evictFrom;       
    Exception? _fault;
    bool _interrupt;
    int _failures;
    HttpRangeStream(HttpClient http, Func<HttpRequestMessage> newRequest, long length, TimeSpan idleTimeout)
    {
        _http = http;
        _newRequest = newRequest;
        _idleTimeout = idleTimeout;
        Length = length;
        var count = (int)((length + BlockSize - 1) / BlockSize);
        if (count > 100_000) throw new IOException("Audio stream too large to buffer.");
        _blocks = new byte[count][];
        _filled = new int[count];
    }
    public static Task<HttpRangeStream> OpenAsync(HttpClient http, Func<HttpRequestMessage> newRequest, CancellationToken ct) =>
        OpenAsync(http, newRequest, ct, DefaultIdleTimeout);
    internal static async Task<HttpRangeStream> OpenAsync(HttpClient http, Func<HttpRequestMessage> newRequest, CancellationToken ct, TimeSpan idleTimeout)
    {
        var response = await SendAsync(http, newRequest, 0, ct).ConfigureAwait(false);
        try
        {
            var length = TotalLength(response);
            if (length <= 0) throw new IOException("The audio stream has no known length.");
            var stream = new HttpRangeStream(http, newRequest, length, idleTimeout);
            _ = Task.Run(() => stream.DownloadLoopAsync(response), CancellationToken.None);
            return stream;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
    static async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<HttpRequestMessage> newRequest, long offset, CancellationToken ct)
    {
        using var request = newRequest();
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"The audio server returned HTTP {status}.", null, (HttpStatusCode)status);
        }
        return response;
    }
    static long TotalLength(HttpResponseMessage response) =>
        response.Content.Headers.ContentRange?.Length
        ?? (response.StatusCode == HttpStatusCode.OK ? response.Content.Headers.ContentLength ?? -1 : -1);
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; }
    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, Length);
    }
    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => Length + offset,
    };
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0 || _position >= Length) return 0;
        var block = (int)(_position / BlockSize);
        var inBlock = (int)(_position % BlockSize);
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
                if (_filled[block] > inBlock) break;
                if (_fault is not null) throw new IOException("The audio stream could not be loaded.", _fault);
                var needsRestart = _position < _cursor || _position - _cursor > ReachDistance;
                if (needsRestart && _restartAt != (long)block * BlockSize)
                {
                    _restartAt = (long)block * BlockSize;
                    WakeDownloader();
                }
                Monitor.Wait(_gate, 250);
            }
            var available = Math.Min(_filled[block] - inBlock, buffer.Length);
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
        lock (_gate) _interrupt = false;
    }
    public long BufferedFrom(long position)
    {
        lock (_gate)
        {
            var end = Math.Clamp(position, 0, Length);
            while (end < Length)
            {
                var block = (int)(end / BlockSize);
                var inBlock = (int)(end % BlockSize);
                if (_filled[block] <= inBlock) break;
                end += _filled[block] - inBlock;
                if (_filled[block] < Math.Min(BlockSize, Length - (long)block * BlockSize)) break;
            }
            return end - position;
        }
    }
    void WakeDownloader()
    {
        if (_wakeDownloader.CurrentCount > 0) return;
        try
        {
            _wakeDownloader.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }
    async Task<long> WaitForRestartAsync(CancellationToken ct)
    {
        while (true)
        {
            lock (_gate)
                if (_restartAt >= 0) return TakeRestart();
            await _wakeDownloader.WaitAsync(500, ct).ConfigureAwait(false);
        }
    }
    void Evict()
    {
        var keepFrom = (int)(Math.Max(0, _readPosition - KeepBehind) / BlockSize);
        for (var i = _evictFrom; i < keepFrom; i++)
        {
            if (_blocks[i] is { Length: BlockSize } spare)
            {
                _spare.Push(spare);
                while (_spare.Count > 32) _spare.TryPop(out _);
            }
            _blocks[i] = null;
            _filled[i] = 0;
        }
        _evictFrom = Math.Max(_evictFrom, keepFrom);
    }
    byte[] NewBlock(int size) => size == BlockSize && _spare.TryPop(out var spare) ? spare : new byte[size];
    async Task DownloadLoopAsync(HttpResponseMessage first)
    {
        var ct = _dispose.Token;
        HttpResponseMessage? response = first;
        long offset = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                response ??= await SendAsync(_http, _newRequest, offset, ct).ConfigureAwait(false);
                var skip = response.StatusCode == HttpStatusCode.OK ? offset : 0;
                await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var restarted = await PumpAsync(body, offset, skip, ct).ConfigureAwait(false);
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
                if (giveUp) break;
                try
                {
                    var baseMs = 500 * (1 << (_failures - 1));
                    var jitter = baseMs * (0.8 + Random.Shared.NextDouble() * 0.4);
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
    long TakeRestart()
    {
        var at = Math.Max(0, _restartAt);
        _restartAt = -1;
        _cursor = at;
        return at;
    }
    async Task<long> PumpAsync(Stream body, long offset, long skip, CancellationToken ct)
    {
        var scratch = new byte[16 * 1024];
        while (skip > 0)
        {
            var read = await body.ReadAsync(scratch.AsMemory(0, (int)Math.Min(scratch.Length, skip)), ct).ConfigureAwait(false);
            if (read == 0) throw new IOException("The audio stream ended early.");
            skip -= read;
        }
        lock (_gate) _cursor = offset;
        while (_cursor < Length)
        {
            int block, inBlock, room;
            byte[] target;
            bool keep;
            while (true)
            {
                lock (_gate)
                {
                    if (_restartAt >= 0) return TakeRestart();
                    if (_cursor - _readPosition <= MaxAhead) break;
                }
                await _wakeDownloader.WaitAsync(250, ct).ConfigureAwait(false);
            }
            lock (_gate)
            {
                block = (int)(_cursor / BlockSize);
                inBlock = (int)(_cursor % BlockSize);
                var size = (int)Math.Min(BlockSize, Length - (long)block * BlockSize);
                keep = _blocks[block] is not null && _filled[block] == size;
                if (!keep && inBlock == 0) _filled[block] = 0;
                target = keep ? scratch : _blocks[block] ??= NewBlock(size);
                _evictFrom = Math.Min(_evictFrom, block);
                room = keep ? Math.Min(scratch.Length, size - inBlock) : size - inBlock;
            }
            int read;
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                idle.CancelAfter(_idleTimeout);
                read = await body.ReadAsync(target.AsMemory(keep ? 0 : inBlock, room), idle.Token).ConfigureAwait(false);
            }
            if (read == 0) throw new IOException("The audio stream ended early.");
            _failures = 0;
            lock (_gate)
            {
                if (!keep && !ReferenceEquals(_blocks[block], target))
                    return _restartAt >= 0 ? TakeRestart() : _readPosition / BlockSize * BlockSize;
                if (!keep) _filled[block] = inBlock + read;
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
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}