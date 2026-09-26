using NLayer;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
namespace OSTream.Core.Audio;

public sealed class Mp3Decoder : IAudioDecoder
{
    private const int DecoderDelay = 529;
    private const int PrerollFrames = 5;
    private readonly ByteSource _src;
    private readonly MpegFrameDecoder _decoder = new();
    private readonly Mp3Frame _frame = new();
    private readonly float[] _pcm = new float[1152 * 2];
    private readonly long _audioStart;
    private readonly long _audioEnd;
    private readonly int _samplesPerFrame;
    private readonly int _startTrim;
    private readonly long _frameCount;
    private readonly double _avgFrameBytes;
    private readonly bool _cbr;
    private readonly List<long> _frameOffsets = [];
    private readonly byte[]? _toc;
    private bool _exact = true;
    private long _frameIndex;
    private int _pcmOffset, _pcmCount;
    private int _discard;
    public Mp3Decoder(Stream stream)
    {
        _src = new ByteSource(stream);
        _src.SkipId3v2();
        if (!Resync(requireNext: true))
        {
            throw new InvalidDataException("No MP3 audio found.");
        }
        Mp3Header first = _frame.Header;
        SampleRate = first.SampleRate;
        Channels = first.ChannelMode == 3 ? 1 : 2;
        _samplesPerFrame = first.Samples;
        _audioStart = _src.Position;
        _audioEnd = _src.Length;
        int encoderDelay = -1;
        int encoderPadding = 0;
        _cbr = true;
        if (_src.Ensure(first.Length))
        {
            ReadOnlySpan<byte> data = _src.Peek(first.Length);
            int xing = 4 + (first.Crc ? 2 : 0) + first.SideInfoSize;
            bool isInfo = xing + 8 <= data.Length && data.Slice(xing, 4).SequenceEqual("Info"u8);
            if (xing + 8 <= data.Length && (data.Slice(xing, 4).SequenceEqual("Xing"u8) || isInfo))
            {
                _cbr = isInfo;
                int flags = BinaryPrimitives.ReadInt32BigEndian(data[(xing + 4)..]);
                int at = xing + 8;
                if ((flags & 1) != 0 && at + 4 <= data.Length) { _frameCount = BinaryPrimitives.ReadUInt32BigEndian(data[at..]); at += 4; }
                if ((flags & 2) != 0)
                {
                    at += 4;
                }
                if ((flags & 4) != 0)
                {
                    if (at + 100 <= data.Length)
                    {
                        _toc = data.Slice(at, 100).ToArray();
                    }
                    at += 100;
                }
                if ((flags & 8) != 0)
                {
                    at += 4;
                }
                if (at + 24 <= data.Length)
                {
                    ReadOnlySpan<byte> tag = data.Slice(at, 4);
                    if (tag.SequenceEqual("LAME"u8) || tag.SequenceEqual("Lavf"u8) || tag.SequenceEqual("Lavc"u8) || tag.SequenceEqual("L3.9"u8))
                    {
                        encoderDelay = (data[at + 21] << 4) | (data[at + 22] >> 4);
                        encoderPadding = ((data[at + 22] & 0x0F) << 8) | data[at + 23];
                    }
                }
                _src.Skip(first.Length);
                _audioStart = _src.Position;
            }
            else if (36 + 26 <= data.Length && data.Slice(36, 4).SequenceEqual("VBRI"u8))
            {
                _cbr = false;
                _frameCount = BinaryPrimitives.ReadUInt32BigEndian(data[(36 + 14)..]);
                _src.Skip(first.Length);
                _audioStart = _src.Position;
            }
        }
        _avgFrameBytes = !_cbr && _frameCount > 0 && _audioEnd > _audioStart
            ? (double)(_audioEnd - _audioStart) / _frameCount
            : first.Samples / 8.0 * first.BitrateKbps * 1000 / first.SampleRate;
        _startTrim = encoderDelay >= 0 ? encoderDelay + DecoderDelay : 0;
        int endTrim = encoderDelay >= 0 ? Math.Max(0, encoderPadding - DecoderDelay) : 0;
        if (_frameCount > 0)
        {
            TotalFrames = Math.Max(0, (_frameCount * _samplesPerFrame) - _startTrim - endTrim);
        }
        else if (_audioEnd > _audioStart)
        {
            long frames = (long)Math.Round((_audioEnd - _audioStart) / _avgFrameBytes);
            TotalFrames = frames * _samplesPerFrame;
        }
        _discard = _startTrim;
    }
    public int SampleRate { get; }
    public int Channels { get; }
    public long TotalFrames { get; }
    public long Position { get; private set; }
    public long StreamPosition => _src.Position;
    private bool Resync(bool requireNext)
    {
        while (_src.Ensure(4))
        {
            if (Mp3Header.TryParse(_src.Peek(4), out Mp3Header header))
            {
                if (!requireNext) { _frame.Header = header; return true; }
                if (!_src.Ensure(header.Length + 4))
                {
                    if (_src.Ensure(header.Length)) { _frame.Header = header; return true; }
                    return false;
                }
                if (Mp3Header.TryParse(_src.Peek(header.Length + 4)[header.Length..], out Mp3Header next) && next.SameStream(header))
                {
                    _frame.Header = header;
                    return true;
                }
            }
            _src.Skip(1);
        }
        return false;
    }
    private bool NextFrame()
    {
        if (_src.Position >= _audioEnd)
        {
            return false;
        }
        if (!_src.Ensure(4))
        {
            return false;
        }
        if (!Mp3Header.TryParse(_src.Peek(4), out Mp3Header header) || header.SampleRate != SampleRate)
        {
            if (!Resync(requireNext: true))
            {
                return false;
            }
            header = _frame.Header;
        }
        if (!_src.Ensure(header.Length))
        {
            return false;
        }
        if (_exact && _frameIndex == _frameOffsets.Count)
        {
            _frameOffsets.Add(_src.Position);
        }
        _frame.Load(header, _src.Peek(header.Length));
        _src.Skip(header.Length);
        _frameIndex++;
        return true;
    }
    public int Read(Span<float> dest)
    {
        int want = dest.Length / Channels;
        int written = 0;
        while (written < want)
        {
            if (_pcmCount == 0 && !DecodeNext())
            {
                break;
            }
            if (_discard > 0)
            {
                int drop = Math.Min(_discard, _pcmCount);
                _pcmOffset += drop;
                _pcmCount -= drop;
                _discard -= drop;
                continue;
            }
            int n = Math.Min(want - written, _pcmCount);
            if (TotalFrames > 0 && _frameCount > 0)
            {
                n = (int)Math.Min(n, Math.Max(0, TotalFrames - Position));
            }
            if (n == 0)
            {
                break;
            }
            _pcm.AsSpan(_pcmOffset * Channels, n * Channels).CopyTo(dest[(written * Channels)..]);
            _pcmOffset += n;
            _pcmCount -= n;
            Position += n;
            written += n;
        }
        return written;
    }
    private bool DecodeNext()
    {
        if (!NextFrame())
        {
            return false;
        }
        int frameChannels = _frame.Header.ChannelMode == 3 ? 1 : 2;
        int samples;
        try
        {
            samples = _decoder.DecodeFrame(_frame, _frame.Pcm, 0);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or InvalidDataException or ArgumentException or EndOfStreamException)
        {
            samples = 0;
        }
        int frames = samples > 0 ? samples / frameChannels : _samplesPerFrame;
        if (samples <= 0)
        {
            Array.Clear(_frame.Pcm, 0, frames * frameChannels);
        }
        if (frameChannels == Channels)
        {
            Array.Copy(_frame.Pcm, _pcm, frames * Channels);
        }
        else if (Channels == 2)
        {
            for (int i = 0; i < frames; i++)
            {
                _pcm[2 * i] = _pcm[(2 * i) + 1] = _frame.Pcm[i];
            }
        }
        else
        {
            for (int i = 0; i < frames; i++)
            {
                _pcm[i] = (_frame.Pcm[2 * i] + _frame.Pcm[(2 * i) + 1]) * 0.5f;
            }
        }
        _pcmOffset = 0;
        _pcmCount = frames;
        return true;
    }
    public void Seek(long frame)
    {
        frame = Math.Clamp(frame, 0, TotalFrames > 0 ? TotalFrames : long.MaxValue);
        long raw = frame + _startTrim;
        long target = raw / _samplesPerFrame;
        long start = Math.Max(0, target - PrerollFrames);
        _decoder.Reset();
        _pcmCount = 0;
        if (!LocateFrame(start))
        {
            Position = TotalFrames;
            _discard = 0;
            return;
        }
        _frameIndex = start;
        for (long i = start; i < target; i++)
        {
            if (!DecodeNext())
            {
                break;
            }
        }
        _pcmCount = 0;
        _discard = (int)(raw - (target * _samplesPerFrame));
        Position = frame;
    }
    private bool LocateFrame(long index)
    {
        if (index < _frameOffsets.Count)
        {
            _src.SeekTo(_frameOffsets[(int)index]);
            _exact = true;
            return true;
        }
        if (_cbr || (_toc is not null && _frameCount > 0))
        {
            _src.SeekTo(Math.Max(_audioStart, EstimateOffset(index)));
            _exact = index == 0;
            return index == 0 || Resync(requireNext: true);
        }
        if (_frameOffsets.Count == 0)
        {
            _src.SeekTo(_audioStart);
        }
        else
        {
            _src.SeekTo(_frameOffsets[^1]);
            if (!_src.Ensure(4) || !Mp3Header.TryParse(_src.Peek(4), out Mp3Header last))
            {
                return false;
            }
            _src.SkipLong(last.Length);
        }
        while (_frameOffsets.Count <= index)
        {
            if (!_src.Ensure(4))
            {
                return false;
            }
            if (!Mp3Header.TryParse(_src.Peek(4), out Mp3Header header) || header.SampleRate != SampleRate)
            {
                if (!Resync(requireNext: true))
                {
                    return false;
                }
                header = _frame.Header;
            }
            _frameOffsets.Add(_src.Position);
            _src.SkipLong(header.Length);
        }
        _src.SeekTo(_frameOffsets[(int)index]);
        _exact = true;
        return true;
    }
    private long EstimateOffset(long index)
    {
        if (_cbr)
        {
            return _audioStart + (long)((index * _avgFrameBytes) - (_avgFrameBytes / 2));
        }
        double percent = Math.Clamp(index * 100.0 / _frameCount, 0, 99.999);
        int i = (int)percent;
        double a = _toc![i], b = i < 99 ? _toc[i + 1] : 256;
        return _audioStart + (long)((a + ((b - a) * (percent - i))) / 256.0 * (_audioEnd - _audioStart));
    }
    public void Dispose()
    {
        _src.Stream.Dispose();
    }
    private sealed class Mp3Frame : IMpegFrame
    {
        private readonly byte[] _data = new byte[2881 + 4];
        private int _length, _readOffset, _bitsRead;
        private ulong _bitBucket;
        public float[] Pcm { get; } = new float[1152 * 2];
        public Mp3Header Header;
        public void Load(Mp3Header header, ReadOnlySpan<byte> bytes)
        {
            Header = header;
            _length = Math.Min(bytes.Length, _data.Length);
            bytes[.._length].CopyTo(_data);
            Reset();
        }
        public int SampleRate => Header.SampleRate;
        public int SampleRateIndex => Header.SampleRateIndex;
        public int FrameLength => Header.Length;
        public int BitRate => Header.BitrateKbps * 1000;
        public MpegVersion Version => Header.Version;
        public MpegLayer Layer => (MpegLayer)Header.Layer;
        public MpegChannelMode ChannelMode => (MpegChannelMode)Header.ChannelMode;
        public int ChannelModeExtension => Header.ModeExtension;
        public int SampleCount => Header.Samples;
        public int BitRateIndex => Header.BitrateIndex;
        public bool IsCopyrighted => Header.Copyright;
        public bool HasCrc => Header.Crc;
        public bool IsCorrupted => false;
        public void Reset()
        {
            _readOffset = 4 + (Header.Crc ? 2 : 0);
            _bitBucket = 0;
            _bitsRead = 0;
        }
        public int ReadBits(int bitCount)
        {
            while (_bitsRead < bitCount)
            {
                if (_readOffset >= _length)
                {
                    throw new EndOfStreamException();
                }
                _bitBucket = (_bitBucket << 8) | _data[_readOffset++];
                _bitsRead += 8;
            }
            int value = (int)((_bitBucket >> (_bitsRead - bitCount)) & ((1UL << bitCount) - 1));
            _bitsRead -= bitCount;
            return value;
        }
    }
}
internal struct Mp3Header
{
    private static readonly int[][] Bitrates =
    [
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
    ];
    private static readonly int[][] SampleRates = [[44100, 48000, 32000], [22050, 24000, 16000], [11025, 12000, 8000]];
    public NLayer.MpegVersion Version;
    public int Layer, BitrateIndex, BitrateKbps, SampleRateIndex, SampleRate, ChannelMode, ModeExtension, Length, Samples;
    public bool Crc, Padding, Copyright;
    public readonly int SideInfoSize => Version == NLayer.MpegVersion.Version1 ? (ChannelMode == 3 ? 17 : 32) : (ChannelMode == 3 ? 9 : 17);
    public readonly bool SameStream(in Mp3Header other)
    {
        return Version == other.Version && Layer == other.Layer && SampleRate == other.SampleRate;
    }
    public static bool TryParse(ReadOnlySpan<byte> b, out Mp3Header h)
    {
        h = default;
        if (b.Length < 4 || b[0] != 0xFF || (b[1] & 0xE0) != 0xE0)
        {
            return false;
        }
        int versionBits = (b[1] >> 3) & 3;
        int layerBits = (b[1] >> 1) & 3;
        int bitrateIndex = b[2] >> 4;
        int rateIndex = (b[2] >> 2) & 3;
        if (versionBits == 1 || layerBits == 0 || bitrateIndex is 0 or 15 || rateIndex == 3 || (b[3] & 3) == 2)
        {
            return false;
        }
        h.Version = versionBits switch { 3 => NLayer.MpegVersion.Version1, 2 => NLayer.MpegVersion.Version2, _ => NLayer.MpegVersion.Version25 };
        h.Layer = 4 - layerBits;
        h.Crc = (b[1] & 1) == 0;
        h.BitrateIndex = bitrateIndex;
        int table = h.Version == NLayer.MpegVersion.Version1 ? h.Layer - 1 : h.Layer == 1 ? 3 : 4;
        h.BitrateKbps = Bitrates[table][bitrateIndex];
        h.SampleRateIndex = rateIndex;
        h.SampleRate = SampleRates[versionBits == 3 ? 0 : versionBits == 2 ? 1 : 2][rateIndex];
        h.Padding = ((b[2] >> 1) & 1) != 0;
        h.ChannelMode = b[3] >> 6;
        h.ModeExtension = (b[3] >> 4) & 3;
        h.Copyright = ((b[3] >> 3) & 1) != 0;
        int pad = h.Padding ? 1 : 0;
        int bps = h.BitrateKbps * 1000;
        if (h.Layer == 1)
        {
            h.Samples = 384;
            h.Length = ((12 * bps / h.SampleRate) + pad) * 4;
        }
        else
        {
            bool lsf = h.Layer == 3 && h.Version != NLayer.MpegVersion.Version1;
            h.Samples = lsf ? 576 : 1152;
            h.Length = ((lsf ? 72 : 144) * bps / h.SampleRate) + pad;
        }
        return h.Length >= 21;
    }
}