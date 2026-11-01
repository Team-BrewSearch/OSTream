using System;
using System.Buffers.Binary;
using System.IO;
namespace OSTream.Core.Audio;

public sealed class WavDecoder : IAudioDecoder
{
    private const int FormatPcm = 1;
    private const int FormatFloat = 3;
    private const int FormatExtensible = 0xFFFE;
    private const int FramesPerRead = 16384;
    private readonly ByteSource _src;
    private readonly int _streamChannels;
    private readonly int _bytesPerSample;
    private readonly int _blockAlign;
    private readonly bool _float;
    private readonly long _dataStart;
    private readonly byte[] _raw;
    private readonly float[] _samples;

    public WavDecoder(Stream stream)
    {
        _src = new ByteSource(stream);
        Span<byte> header = stackalloc byte[12];
        if (_src.Read(header) != 12 || !(header[..4].SequenceEqual("RIFF"u8) || header[..4].SequenceEqual("RF64"u8)) || !header[8..].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a WAV stream.");
        }

        Span<byte> chunk = stackalloc byte[8];
        Span<byte> fmt = stackalloc byte[40];
        int format = -1;
        int bits = 0;
        long dataSize;
        while (true)
        {
            if (_src.Read(chunk) != 8)
            {
                throw new InvalidDataException("WAV stream has no audio data.");
            }

            long size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (size < 16)
                {
                    throw new InvalidDataException("Truncated WAV format chunk.");
                }

                int read = (int)Math.Min(size, fmt.Length);
                if (_src.Read(fmt[..read]) != read)
                {
                    throw new InvalidDataException("Truncated WAV format chunk.");
                }

                format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                _streamChannels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
                SampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
                _blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
                if (format == FormatExtensible && read >= 26)
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]);
                }

                _src.SkipLong(size - read + (size & 1));
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                if (format < 0)
                {
                    throw new InvalidDataException("WAV audio data comes before its format.");
                }

                dataSize = size;
                break;
            }
            else
            {
                _src.SkipLong(size + (size & 1));
            }
        }
        if (_streamChannels is 0 or > 8 || SampleRate <= 0 || _blockAlign == 0 || _blockAlign % _streamChannels != 0)
        {
            throw new InvalidDataException("WAV stream has an invalid format.");
        }

        _bytesPerSample = _blockAlign / _streamChannels;
        _float = format == FormatFloat;
        bool supported = format == FormatPcm ? _bytesPerSample is >= 1 and <= 4 : _float && _bytesPerSample is 4 or 8;
        if (!supported)
        {
            throw new InvalidDataException($"Unsupported WAV encoding (format {format}, {bits}-bit).");
        }

        _dataStart = _src.Position;
        long available = _src.Length - _dataStart;
        if (dataSize == 0 || dataSize == uint.MaxValue || dataSize > available)
        {
            dataSize = available;
        }

        TotalFrames = dataSize / _blockAlign;
        Channels = Math.Min(_streamChannels, 2);
        _raw = new byte[FramesPerRead * _blockAlign];
        _samples = _streamChannels > 2 ? new float[FramesPerRead * _streamChannels] : [];
    }
    public int SampleRate { get; }
    public int Channels { get; }
    public long TotalFrames { get; }
    public long Position { get; private set; }
    public long StreamPosition => _src.Position;
    public int Read(Span<float> dest)
    {
        int want = (int)Math.Min(dest.Length / Channels, TotalFrames - Position);
        int written = 0;
        while (written < want)
        {
            int frames = Math.Min(want - written, FramesPerRead);
            int bytes = _src.Read(_raw.AsSpan(0, frames * _blockAlign));
            frames = bytes / _blockAlign;
            if (frames == 0)
            {
                break;
            }

            Span<float> output = dest[(written * Channels)..];
            if (_streamChannels <= 2)
            {
                Convert(_raw, output, frames * _streamChannels);
            }
            else
            {
                Convert(_raw, _samples, frames * _streamChannels);
                Downmix.ToStereo(_samples, _streamChannels, output, frames);
            }
            written += frames;
            Position += frames;
        }
        return written;
    }
    private void Convert(ReadOnlySpan<byte> raw, Span<float> dest, int count)
    {
        switch (_bytesPerSample)
        {
            case 1:
                for (int i = 0; i < count; i++)
                {
                    dest[i] = (raw[i] - 128) / 128f;
                }

                break;
            case 2:
                for (int i = 0; i < count; i++)
                {
                    dest[i] = BinaryPrimitives.ReadInt16LittleEndian(raw[(2 * i)..]) / 32768f;
                }

                break;
            case 3:
                for (int i = 0; i < count; i++)
                {
                    dest[i] = (((raw[3 * i] << 8) | (raw[(3 * i) + 1] << 16) | (raw[(3 * i) + 2] << 24)) >> 8) / 8388608f;
                }

                break;
            case 4 when _float:
                for (int i = 0; i < count; i++)
                {
                    dest[i] = BinaryPrimitives.ReadSingleLittleEndian(raw[(4 * i)..]);
                }

                break;
            case 4:
                for (int i = 0; i < count; i++)
                {
                    dest[i] = BinaryPrimitives.ReadInt32LittleEndian(raw[(4 * i)..]) / 2147483648f;
                }

                break;
            default:
                for (int i = 0; i < count; i++)
                {
                    dest[i] = (float)BinaryPrimitives.ReadDoubleLittleEndian(raw[(8 * i)..]);
                }

                break;
        }
    }
    public void Seek(long frame)
    {
        Position = Math.Clamp(frame, 0, TotalFrames);
        _src.SeekTo(_dataStart + (Position * _blockAlign));
    }
    public void Dispose()
    {
        _src.Stream.Dispose();
    }
}
