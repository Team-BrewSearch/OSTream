using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
namespace OSTream.Core.Audio;
public enum EngineEventKind { Playing, Paused, Waiting, Ended, Error }
public readonly record struct EngineEvent(EngineEventKind Kind, long Session, string? Error = null);
public sealed class AudioEngine : IDisposable
{
    const int AlBuffers = 4;
    const int StartThreshold = 2;
    const int ChunkQueue = 8;
    const long IdleClose = 30_000;
    readonly Thread _thread;
    readonly ConcurrentQueue<Action> _commands = new();
    readonly AutoResetEvent _wake = new(false);
    volatile bool _disposed;
    IntPtr _device, _context;
    uint _source;
    readonly uint[] _buffers = new uint[AlBuffers];
    readonly Stack<uint> _free = new();
    readonly Queue<(uint Id, long Start, int Frames)> _queued = new();
    bool _float32;
    long _lastDeviceCheck;
    long _idleSince;
    string? _deviceError;
    Session? _session;
    float _volume = 1f;
    short[] _pcm16 = [];
    long _positionFramesLong;
    bool _libraryMissing;
    volatile int _sampleRate = 44100;
    public AudioEngine()
    {
        _thread = new Thread(Run) { Name = "OSTream audio", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }
    public event Action<EngineEvent>? Event;
    public double Position => Interlocked.Read(ref _positionFramesLong) / (double)_sampleRate;
    public double Duration { get; private set; }
    public double BufferedAhead
    {
        get
        {
            var session = _session;
            if (session is null || Duration <= 0) return 0;
            var stream = session.Stream;
            var bytesPerSecond = stream.Length / Duration;
            return bytesPerSecond > 0 ? stream.BufferedFrom(session.Decoder.StreamPosition) / bytesPerSecond : 0;
        }
    }
    void Post(Action command)
    {
        if (_disposed) return;
        _commands.Enqueue(command);
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }
    public void Load(IAudioDecoder decoder, HttpRangeStream stream, long sessionId)
    {
        Duration = decoder.TotalFrames > 0 ? decoder.TotalFrames / (double)decoder.SampleRate : 0;
        Post(() => StartSession(new Session(decoder, stream, sessionId)));
    }
    public void Play() => Post(() =>
    {
        if (_session is not { } s || s.WantPlay) return;
        var reopened = _source == 0;
        if (reopened && !OpenDevice()) return;
        s.WantPlay = true;
        if (s.Ended) SeekTo(0);
        else if (reopened) SeekTo(Interlocked.Read(ref _positionFramesLong));
        s.Ended = false;
        s.Announced = false;
    });
    public void Pause() => Post(() =>
    {
        if (_session is not { } s || !s.WantPlay) return;
        s.WantPlay = false;
        if (_source != 0) OpenAl.alSourcePause(_source);
        Raise(EngineEventKind.Paused);
    });
    public void Stop() => Post(() => EndSession());
    public void Seek(double seconds) => Post(() =>
    {
        if (_session is null) return;
        SeekTo((long)(Math.Max(0, seconds) * _session.Decoder.SampleRate));
    });
    public void SetVolume(double volume) => Post(() =>
    {
        _volume = (float)Math.Clamp(volume, 0, 1);
        if (_source != 0) OpenAl.alSourcef(_source, OpenAl.AL_GAIN, _volume);
    });
    void Run()
    {
        while (!_disposed)
        {
            while (_commands.TryDequeue(out var command)) command();
            if (_disposed) break;
            if (_session is { } session && _source != 0) Pump(session);
            CheckDevice();
            _wake.WaitOne(_source == 0 ? Timeout.Infinite : _session?.WantPlay == true ? 10 : 250);
        }
        EndSession();
        ReleaseDevice();
    }
    bool OpenDevice()
    {
        if (_source == 0 && !_libraryMissing)
        {
            ReleaseDevice();
            InitDevice();
        }
        _idleSince = Environment.TickCount64;
        if (_source != 0 && _deviceError is null) return true;
        Raise(EngineEventKind.Error, _deviceError ?? "No audio output device is available.");
        return false;
    }
    void InitDevice()
    {
        try
        {
            _device = OpenAl.alcOpenDevice(null);
            if (_device == IntPtr.Zero)
            {
                _deviceError = "No audio output device is available.";
                return;
            }
            _context = OpenAl.alcCreateContext(_device, null);
            OpenAl.alcMakeContextCurrent(_context);
            uint[] sourceArray = new uint[1];
            OpenAl.alGenSources(1, sourceArray);
            _source = sourceArray[0];
            OpenAl.alGenBuffers(AlBuffers, _buffers);
            foreach (var b in _buffers) _free.Push(b);
            OpenAl.alSourcei(_source, OpenAl.AL_SOURCE_RELATIVE, 1);
            OpenAl.alSourcef(_source, OpenAl.AL_GAIN, _volume);
            _float32 = OpenAl.HasAlExtension("AL_EXT_FLOAT32");
            _deviceError = OpenAl.alGetError() == 0 ? null : "The audio device could not be initialized.";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _deviceError = "The audio library (OpenAL) could not be loaded.";
            _libraryMissing = true;
            _source = 0;
        }
    }
    void ReleaseDevice()
    {
        if (_device == IntPtr.Zero) return;
        if (_source != 0)
        {
            FlushSource();
            uint[] sourceArray = new uint[1] { _source };
            OpenAl.alDeleteSources(1, sourceArray);
            OpenAl.alDeleteBuffers(AlBuffers, _buffers);
        }
        OpenAl.alcMakeContextCurrent(IntPtr.Zero);
        if (_context != IntPtr.Zero) OpenAl.alcDestroyContext(_context);
        OpenAl.alcCloseDevice(_device);
        _device = _context = IntPtr.Zero;
        _source = 0;
        _free.Clear();
        _pcm16 = [];
    }
    void CheckDevice()
    {
        var now = Environment.TickCount64;
        if (_source == 0 || now - _lastDeviceCheck < 1000) return;
        _lastDeviceCheck = now;
        if (_session is { WantPlay: true }) _idleSince = now;
        else if (now - _idleSince > IdleClose)
        {
            ReleaseDevice();
            return;
        }
        OpenAl.alcGetIntegerv(_device, OpenAl.ALC_CONNECTED, 1, out int connected);
        if (connected != 0 || !OpenAl.ReopenDeviceIfNeeded(_device)) return;
        if (_session is { WantPlay: true })
            OpenAl.alSourcePlay(_source);
    }
    void StartSession(Session session)
    {
        EndSession();
        _session = session;
        _sampleRate = session.Decoder.SampleRate;
        SetPosition(0);
        session.Start();
        session.WantPlay = OpenDevice();
    }
    void EndSession()
    {
        if (_session is null) return;
        if (_source != 0) FlushSource();
        _session.Dispose();
        _session = null;
    }
    void FlushSource()
    {
        OpenAl.alSourceStop(_source);
        var queued = OpenAl.GetSource(_source, OpenAl.AL_BUFFERS_QUEUED);
        while (queued-- > 0)
        {
            uint[] bufferArray = new uint[1];
            OpenAl.alSourceUnqueueBuffers(_source, 1, bufferArray);
        }
        while (_queued.Count > 0) _free.Push(_queued.Dequeue().Id);
    }
    void SeekTo(long frame)
    {
        var session = _session!;
        if (_source != 0) FlushSource();
        session.RequestSeek(frame);
        session.Ended = false;
        SetPosition(frame);
        if (session.WantPlay) session.Announced = false;
    }
    void SetPosition(long frames) => Interlocked.Exchange(ref _positionFramesLong, Math.Max(0, frames));
    void Pump(Session session)
    {
        var processed = OpenAl.GetSource(_source, OpenAl.AL_BUFFERS_PROCESSED);
        while (processed-- > 0 && _queued.Count > 0)
        {
            uint[] bufferArray = new uint[1];
            OpenAl.alSourceUnqueueBuffers(_source, 1, bufferArray);
            var done = _queued.Dequeue();
            _free.Push(done.Id);
            if (_queued.Count == 0) SetPosition(done.Start + done.Frames);
        }
        while (_free.Count > 0 && session.TryTake(out var chunk))
        {
            if (chunk.Error is not null)
            {
                FlushSource();
                session.Failed = true;
                Raise(EngineEventKind.Error, chunk.Error);
                return;
            }
            if (chunk.Frames == 0)
            {
                session.Eof = true;
                continue;
            }
            var id = _free.Pop();
            Upload(id, chunk, session.Decoder.Channels, session.Decoder.SampleRate);
            _queued.Enqueue((id, chunk.Start, chunk.Frames));
            uint[] queueArray = new uint[1] { id };
            OpenAl.alSourceQueueBuffers(_source, 1, queueArray);
            ArrayPool<float>.Shared.Return(chunk.Data);
        }
        if (session.Failed) return;
        if (_queued.Count > 0) SetPosition(_queued.Peek().Start + OpenAl.GetSource(_source, OpenAl.AL_SAMPLE_OFFSET));
        var state = OpenAl.GetSource(_source, OpenAl.AL_SOURCE_STATE);
        if (!session.WantPlay || session.Ended) return;
        if (state == OpenAl.AL_PLAYING)
        {
            if (!session.Announced)
            {
                session.Announced = true;
                Raise(EngineEventKind.Playing);
            }
            return;
        }
        if (_queued.Count >= StartThreshold || (_queued.Count > 0 && (session.Eof || _free.Count == 0)))
        {
            OpenAl.alSourcePlay(_source);
            return;
        }
        if (_queued.Count == 0 && session.Eof && session.QueueEmpty)
        {
            session.Ended = true;
            session.WantPlay = false;
            Raise(EngineEventKind.Ended);
            return;
        }
        if (session.Announced)
        {
            session.Announced = false;
            Raise(EngineEventKind.Waiting);
        }
    }
    void Upload(uint id, in Chunk chunk, int channels, int rate)
    {
        var samples = chunk.Frames * channels;
        if (_float32)
        {
            OpenAl.alBufferData(id, channels == 1 ? OpenAl.AL_FORMAT_MONO_FLOAT32 : OpenAl.AL_FORMAT_STEREO_FLOAT32, chunk.Data, samples * 4, rate);
            return;
        }
        if (_pcm16.Length < samples) _pcm16 = new short[samples];
        for (var i = 0; i < samples; i++) _pcm16[i] = (short)Math.Clamp(chunk.Data[i] * 32767f, -32768f, 32767f);
        OpenAl.alBufferData(id, channels == 1 ? OpenAl.AL_FORMAT_MONO16 : OpenAl.AL_FORMAT_STEREO16, _pcm16, samples * 2, rate);
    }
    void Raise(EngineEventKind kind, string? error = null)
    {
        var session = _session;
        if (session is not null) Event?.Invoke(new EngineEvent(kind, session.Id, error));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Set();
        if (_thread.Join(TimeSpan.FromSeconds(4))) _wake.Dispose();
    }
    readonly record struct Chunk(float[] Data, int Frames, long Start, string? Error = null);
    sealed class Session(IAudioDecoder decoder, HttpRangeStream stream, long id) : IDisposable
    {
        readonly Queue<Chunk> _chunks = new();
        readonly object _gate = new();
        readonly CancellationTokenSource _stop = new();
        long _pendingSeek = -1;
        Thread? _producer;
        public long Id { get; } = id;
        public IAudioDecoder Decoder { get; } = decoder;
        public HttpRangeStream Stream { get; } = stream;
        public bool WantPlay, Announced, Eof, Ended, Failed;
        public bool QueueEmpty
        {
            get
            {
                lock (_gate) return _chunks.Count == 0;
            }
        }
        public void Start()
        {
            _producer = new Thread(Produce) { Name = "OSTream decode", IsBackground = true };
            _producer.Start();
        }
        public bool TryTake(out Chunk chunk)
        {
            lock (_gate)
            {
                if (_chunks.Count == 0 || _pendingSeek >= 0)
                {
                    chunk = default;
                    return false;
                }
                chunk = _chunks.Dequeue();
                Monitor.PulseAll(_gate);
                return true;
            }
        }
        public void RequestSeek(long frame)
        {
            lock (_gate)
            {
                _pendingSeek = frame;
                Eof = false;
                Failed = false;
                while (_chunks.Count > 0)
                {
                    var stale = _chunks.Dequeue();
                    if (stale.Frames > 0) ArrayPool<float>.Shared.Return(stale.Data);
                }
                Monitor.PulseAll(_gate);
            }
            Stream.Interrupt();
        }
        void Produce()
        {
            var channels = Decoder.Channels;
            var framesPerChunk = Math.Max(4096, Decoder.SampleRate / 5);
            var finished = false;
            while (!_stop.IsCancellationRequested)
            {
                long seek;
                lock (_gate)
                {
                    seek = _pendingSeek;
                    if (seek < 0 && (finished || _chunks.Count >= ChunkQueue))
                    {
                        Monitor.Wait(_gate, 100);
                        continue;
                    }
                }
                try
                {
                    if (seek >= 0)
                    {
                        Stream.ClearInterrupt();
                        Decoder.Seek(seek);
                        finished = false;
                        lock (_gate)
                        {
                            if (_pendingSeek == seek) _pendingSeek = -1;
                        }
                        continue;
                    }
                    var start = Decoder.Position;
                    var data = ArrayPool<float>.Shared.Rent(framesPerChunk * channels);
                    var frames = Decoder.Read(data.AsSpan(0, framesPerChunk * channels));
                    if (frames == 0) ArrayPool<float>.Shared.Return(data);
                    lock (_gate)
                    {
                        if (_pendingSeek >= 0)
                        {
                            if (frames > 0) ArrayPool<float>.Shared.Return(data);
                            continue;
                        }
                        _chunks.Enqueue(frames > 0 ? new Chunk(data, frames, start) : new Chunk([], 0, start));
                    }
                    finished = frames == 0;
                }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
                {
                    lock (_gate)
                        if (_pendingSeek < 0) _pendingSeek = Decoder.Position;
                }
                catch (Exception) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    lock (_gate) _chunks.Enqueue(new Chunk([], 0, 0, "The audio stream could not be loaded."));
                    finished = true;
                }
            }
        }
        public void Dispose()
        {
            _stop.Cancel();
            if (Stream is HttpRangeStream range) range.Interrupt();
            Stream.Dispose();
            lock (_gate)
            {
                Monitor.PulseAll(_gate);
                while (_chunks.Count > 0)
                {
                    var chunk = _chunks.Dequeue();
                    if (chunk.Frames > 0) ArrayPool<float>.Shared.Return(chunk.Data);
                }
            }
            if (_producer?.Join(TimeSpan.FromSeconds(2)) != false) Decoder.Dispose();
        }
    }
}
