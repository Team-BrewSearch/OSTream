using System;
using System.IO;
using NVorbis;
namespace OSTream.Core.Audio;
public sealed class VorbisDecoder : IAudioDecoder
{
    static readonly int[][] ToWavOrder =
    [
        [], [0], [0, 1], [0, 2, 1], [0, 1, 2, 3], [0, 2, 1, 3, 4], [0, 2, 1, 5, 3, 4], [0, 2, 1, 6, 5, 3, 4], [0, 2, 1, 7, 5, 6, 3, 4],
    ];
    readonly Stream _stream;
    readonly VorbisReader _reader;
    readonly int _streamChannels;
    readonly float[] _samples = [];
    readonly float[] _ordered = [];
    long _position;
    bool _atEnd;
    public VorbisDecoder(Stream stream)
    {
        _stream = stream;
        try
        {
            _reader = new VorbisReader(stream, closeOnDispose: true);
            _streamChannels = _reader.Channels;
            SampleRate = _reader.SampleRate;
            TotalFrames = Math.Max(0, _reader.TotalSamples);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException or EndOfStreamException)
        {
            stream.Dispose();
            throw new InvalidDataException("Not an Ogg Vorbis stream.", ex);
        }
        if (_streamChannels is < 1 or > 8 || SampleRate <= 0)
        {
            _reader.Dispose();
            throw new InvalidDataException("Unsupported Ogg Vorbis channel layout.");
        }
        Channels = Math.Min(_streamChannels, 2);
        _samples = new float[4096 * _streamChannels];
        if (_streamChannels > 2) _ordered = new float[4096 * _streamChannels];
    }
    public int SampleRate { get; }
    public int Channels { get; }
    public long TotalFrames { get; }
    public long Position => _position;
    public long StreamPosition => _stream.Position;
    public int Read(Span<float> dest)
    {
        if (_atEnd) return 0;
        var want = dest.Length / Channels;
        if (TotalFrames > 0) want = (int)Math.Min(want, TotalFrames - _position);
        var written = 0;
        while (written < want)
        {
            int frames;
            if (_streamChannels <= 2)
            {
                frames = _reader.ReadSamples(dest.Slice(written * Channels, (want - written) * Channels)) / Channels;
            }
            else
            {
                var chunk = Math.Min(want - written, _samples.Length / _streamChannels);
                frames = _reader.ReadSamples(_samples.AsSpan(0, chunk * _streamChannels)) / _streamChannels;
                var order = ToWavOrder[_streamChannels];
                for (var i = 0; i < frames; i++)
                    for (var c = 0; c < _streamChannels; c++)
                        _ordered[i * _streamChannels + c] = _samples[i * _streamChannels + order[c]];
                Downmix.ToStereo(_ordered, _streamChannels, dest[(written * 2)..], frames);
            }
            if (frames == 0) break;
            written += frames;
            _position += frames;
        }
        return written;
    }
    public void Seek(long frame)
    {
        var target = Math.Max(0, frame);
        _atEnd = TotalFrames > 0 && target >= TotalFrames;
        _position = _atEnd ? TotalFrames : target;
        if (_atEnd) return;
        var start = Math.Max(0, target - SampleRate);
        _reader.SeekTo(start);
        for (var skip = target - start; skip > 0;)
        {
            var frames = _reader.ReadSamples(_samples.AsSpan(0, (int)Math.Min(skip, _samples.Length / _streamChannels) * _streamChannels)) / _streamChannels;
            if (frames == 0) break;
            skip -= frames;
        }
    }
    public void Dispose() => _reader.Dispose();
}
