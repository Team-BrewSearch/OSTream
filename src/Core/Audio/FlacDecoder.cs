using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
namespace OSTream.Core.Audio;

public sealed class FlacDecoder : IAudioDecoder
{
    private readonly ByteSource _src;
    private readonly BitReader _bits;
    private readonly int _streamChannels;
    private readonly int _bitsPerSample;
    private readonly long _firstFrame;
    private readonly List<(long Sample, long Offset)> _seekPoints = [];
    private readonly int _maxBlockSize;
    private long[][] _channelBuffers = [];
    private float[] _multi = [];
    private readonly float[] _pcm;
    private int _pcmOffset, _pcmCount;
    private int _discard;
    private long _frameSample;
    public FlacDecoder(Stream stream)
    {
        _src = new ByteSource(stream);
        _src.SkipId3v2();
        Span<byte> magic = stackalloc byte[4];
        if (_src.Read(magic) != 4 || !magic.SequenceEqual("fLaC"u8))
        {
            throw new InvalidDataException("Not a FLAC stream.");
        }

        bool sawStreamInfo = false;
        Span<byte> header = stackalloc byte[4];
        Span<byte> info = stackalloc byte[34];
        Span<byte> point = stackalloc byte[18];
        while (true)
        {
            if (_src.Read(header) != 4)
            {
                throw new InvalidDataException("Truncated FLAC metadata.");
            }

            bool last = (header[0] & 0x80) != 0;
            int type = header[0] & 0x7F;
            int length = (header[1] << 16) | (header[2] << 8) | header[3];
            if (type == 0 && length >= 34)
            {
                _ = _src.Read(info);
                _maxBlockSize = (info[2] << 8) | info[3];
                SampleRate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
                _streamChannels = ((info[12] >> 1) & 7) + 1;
                _bitsPerSample = (((info[12] & 1) << 4) | (info[13] >> 4)) + 1;
                TotalFrames = ((long)(info[13] & 0x0F) << 32) | ((long)info[14] << 24) | ((long)info[15] << 16) | ((long)info[16] << 8) | info[17];
                _src.SkipLong(length - 34);
                sawStreamInfo = true;
            }
            else if (type == 3)
            {
                for (int i = 0; i + 18 <= length; i += 18)
                {
                    _ = _src.Read(point);
                    long sample = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(point);
                    long offset = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(point[8..]);
                    if (sample != -1)
                    {
                        _seekPoints.Add((sample, offset));
                    }
                }
                _src.SkipLong(length % 18);
            }
            else
            {
                _src.SkipLong(length);
            }
            if (last)
            {
                break;
            }
        }
        if (!sawStreamInfo || SampleRate == 0)
        {
            throw new InvalidDataException("FLAC stream has no STREAMINFO.");
        }

        _firstFrame = _src.Position;
        _bits = new BitReader(_src);
        Channels = Math.Min(_streamChannels, 2);
        _pcm = new float[Math.Max(_maxBlockSize, 4096) * 2 * 2];
        _seekPoints.Sort();
    }
    public int SampleRate { get; }
    public int Channels { get; }
    public long TotalFrames { get; }
    public long Position { get; private set; }
    public long StreamPosition => _src.Position;
    public int Read(Span<float> dest)
    {
        int want = dest.Length / Channels;
        int written = 0;
        while (written < want)
        {
            if (_pcmCount == 0 && !DecodeFrame())
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
            _pcm.AsSpan(_pcmOffset * Channels, n * Channels).CopyTo(dest[(written * Channels)..]);
            _pcmOffset += n;
            _pcmCount -= n;
            Position += n;
            written += n;
        }
        return written;
    }
    private bool DecodeFrame()
    {
        while (true)
        {
            if (!_bits.AtByteBoundary)
            {
                _bits.AlignToByte();
            }

            long start = _bits.BytePosition;
            if (!_bits.Ensure(2))
            {
                return false;
            }

            if (!TryReadFrameHeader(out FrameHeader frame))
            {
                _bits.SeekToByte(start + 1);
                if (!FindSync())
                {
                    return false;
                }

                continue;
            }
            try
            {
                DecodeSubframes(frame);
            }
            catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException)
            {
                _bits.SeekToByte(start + 2);
                if (!FindSync())
                {
                    return false;
                }

                continue;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
            _bits.AlignToByte();
            _bits.SkipBits(16);
            Interleave(frame);
            _frameSample = frame.FirstSample + frame.BlockSize;
            return true;
        }
    }
    private readonly record struct FrameHeader(int BlockSize, int ChannelAssignment, int BitsPerSample, long FirstSample);
    private static readonly int[] RateCodes = [0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000];
    private bool TryReadFrameHeader(out FrameHeader frame)
    {
        frame = default;
        long start = _bits.BytePosition;
        if (!_bits.Ensure(16) && !_bits.Ensure(6))
        {
            return false;
        }

        byte b0 = _bits.PeekByte(0);
        byte b1 = _bits.PeekByte(1);
        if (b0 != 0xFF || (b1 & 0xFE) != 0xF8)
        {
            return false;
        }

        bool variable = (b1 & 1) != 0;
        byte b2 = _bits.PeekByte(2);
        byte b3 = _bits.PeekByte(3);
        int blockCode = b2 >> 4;
        int rateCode = b2 & 0x0F;
        int channelCode = b3 >> 4;
        int sizeCode = (b3 >> 1) & 7;
        if (blockCode == 0 || rateCode == 15 || channelCode > 10 || sizeCode == 3 || (b3 & 1) != 0)
        {
            return false;
        }

        int at = 4;
        byte lead = _bits.PeekByte(at++);
        long number;
        int extra;
        if (lead < 0x80) { number = lead; extra = 0; }
        else if ((lead & 0xE0) == 0xC0) { number = lead & 0x1F; extra = 1; }
        else if ((lead & 0xF0) == 0xE0) { number = lead & 0x0F; extra = 2; }
        else if ((lead & 0xF8) == 0xF0) { number = lead & 0x07; extra = 3; }
        else if ((lead & 0xFC) == 0xF8) { number = lead & 0x03; extra = 4; }
        else if ((lead & 0xFE) == 0xFC) { number = lead & 0x01; extra = 5; }
        else if (lead == 0xFE) { number = 0; extra = 6; }
        else
        {
            return false;
        }

        for (int i = 0; i < extra; i++)
        {
            byte c = _bits.PeekByte(at++);
            if ((c & 0xC0) != 0x80)
            {
                return false;
            }

            number = (number << 6) | (uint)(c & 0x3F);
        }
        int blockSize = blockCode switch
        {
            1 => 192,
            >= 2 and <= 5 => 576 << (blockCode - 2),
            6 => _bits.PeekByte(at++) + 1,
            7 => ((_bits.PeekByte(at++) << 8) | _bits.PeekByte(at++)) + 1,
            _ => 256 << (blockCode - 8),
        };
        int rate = rateCode switch
        {
            0 => SampleRate,
            12 => _bits.PeekByte(at++) * 1000,
            13 => (_bits.PeekByte(at++) << 8) | _bits.PeekByte(at++),
            14 => ((_bits.PeekByte(at++) << 8) | _bits.PeekByte(at++)) * 10,
            _ => RateCodes[rateCode],
        };
        byte crc = 0;
        for (int i = 0; i < at; i++)
        {
            crc = Crc8(crc, _bits.PeekByte(i));
        }

        if (crc != _bits.PeekByte(at))
        {
            return false;
        }

        at++;
        int channels = channelCode <= 7 ? channelCode + 1 : 2;
        if (rate != SampleRate || channels != _streamChannels)
        {
            return false;
        }

        int bps = sizeCode switch { 0 => _bitsPerSample, 1 => 8, 2 => 12, 4 => 16, 5 => 20, 6 => 24, _ => 32 };
        long firstSample = variable ? number : number * (_maxBlockSize > 0 ? _maxBlockSize : blockSize);
        _bits.SeekToByte(start + at);
        frame = new FrameHeader(blockSize, channelCode, bps, firstSample);
        return true;
    }
    private static byte Crc8(byte crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
        {
            crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
        }

        return crc;
    }
    private bool FindSync()
    {
        while (_bits.Ensure(2))
        {
            long here = _bits.BytePosition;
            if (_bits.PeekByte(0) == 0xFF && (_bits.PeekByte(1) & 0xFE) == 0xF8 && TryReadFrameHeader(out _))
            {
                _bits.SeekToByte(here);
                return true;
            }
            _bits.SeekToByte(here + 1);
        }
        return false;
    }
    private void DecodeSubframes(in FrameHeader frame)
    {
        int n = frame.BlockSize;
        if (n * 2 > _pcm.Length)
        {
            throw new InvalidDataException("Block larger than STREAMINFO allows.");
        }

        if (_channelBuffers.Length < _streamChannels || _channelBuffers[0].Length < n)
        {
            _channelBuffers = new long[_streamChannels][];
            for (int c = 0; c < _streamChannels; c++)
            {
                _channelBuffers[c] = new long[Math.Max(n, _maxBlockSize)];
            }
        }
        for (int c = 0; c < _streamChannels; c++)
        {
            int bps = frame.BitsPerSample;
            if ((frame.ChannelAssignment == 8 && c == 1) || (frame.ChannelAssignment == 9 && c == 0) || (frame.ChannelAssignment == 10 && c == 1))
            {
                bps++;
            }

            DecodeSubframe(_channelBuffers[c].AsSpan(0, n), bps);
        }
        long[] a = _channelBuffers[0];
        long[] b = _streamChannels > 1 ? _channelBuffers[1] : a;
        switch (frame.ChannelAssignment)
        {
            case 8:
                for (int i = 0; i < n; i++)
                {
                    b[i] = a[i] - b[i];
                }

                break;
            case 9:
                for (int i = 0; i < n; i++)
                {
                    a[i] += b[i];
                }

                break;
            case 10:
                for (int i = 0; i < n; i++)
                {
                    long side = b[i];
                    long mid = (a[i] << 1) | (side & 1);
                    a[i] = (mid + side) >> 1;
                    b[i] = (mid - side) >> 1;
                }
                break;
        }
    }
    private void DecodeSubframe(Span<long> output, int bps)
    {
        if (_bits.ReadBits(1) != 0)
        {
            throw new InvalidDataException("Bad subframe padding.");
        }

        int type = (int)_bits.ReadBits(6);
        int wasted = 0;
        if (_bits.ReadBits(1) != 0)
        {
            wasted = _bits.ReadUnary() + 1;
            bps -= wasted;
        }
        if (bps is <= 0 or > 33)
        {
            throw new InvalidDataException("Bad sample size.");
        }

        if (type == 0)
        {
            output.Fill(_bits.ReadSigned(bps));
        }
        else if (type == 1)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = _bits.ReadSigned(bps);
            }
        }
        else if (type is >= 8 and <= 12)
        {
            int order = type - 8;
            if (order > output.Length)
            {
                throw new InvalidDataException("Bad predictor order.");
            }

            for (int i = 0; i < order; i++)
            {
                output[i] = _bits.ReadSigned(bps);
            }

            ReadResidual(output, order);
            RestoreFixed(output, order);
        }
        else if (type >= 32)
        {
            int order = type - 31;
            if (order > output.Length)
            {
                throw new InvalidDataException("Bad predictor order.");
            }

            for (int i = 0; i < order; i++)
            {
                output[i] = _bits.ReadSigned(bps);
            }

            int precision = (int)_bits.ReadBits(4) + 1;
            if (precision == 16)
            {
                throw new InvalidDataException("Bad LPC precision.");
            }

            int shift = (int)_bits.ReadSigned(5);
            if (shift < 0)
            {
                throw new InvalidDataException("Negative LPC shift.");
            }

            Span<long> coefs = stackalloc long[32];
            for (int i = 0; i < order; i++)
            {
                coefs[i] = _bits.ReadSigned(precision);
            }

            ReadResidual(output, order);
            RestoreLpc(output, coefs[..order], shift);
        }
        else
        {
            throw new InvalidDataException("Reserved subframe type.");
        }
        if (wasted > 0)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] <<= wasted;
            }
        }
    }
    private void ReadResidual(Span<long> output, int order)
    {
        int method = (int)_bits.ReadBits(2);
        if (method > 1)
        {
            throw new InvalidDataException("Reserved residual coding method.");
        }

        int paramBits = method == 0 ? 4 : 5;
        int escape = method == 0 ? 15 : 31;
        int partitionOrder = (int)_bits.ReadBits(4);
        int partitions = 1 << partitionOrder;
        int perPartition = output.Length >> partitionOrder;
        if (perPartition << partitionOrder != output.Length || perPartition < order)
        {
            throw new InvalidDataException("Bad partition order.");
        }

        int i = order;
        for (int p = 0; p < partitions; p++)
        {
            int count = p == 0 ? perPartition - order : perPartition;
            int param = (int)_bits.ReadBits(paramBits);
            if (param == escape)
            {
                int raw = (int)_bits.ReadBits(5);
                for (int k = 0; k < count; k++)
                {
                    output[i++] = raw == 0 ? 0 : _bits.ReadSigned(raw);
                }
            }
            else
            {
                for (int k = 0; k < count; k++)
                {
                    output[i++] = _bits.ReadRice(param);
                }
            }
        }
    }
    private static void RestoreFixed(Span<long> s, int order)
    {
        switch (order)
        {
            case 1: for (int i = 1; i < s.Length; i++) { s[i] += s[i - 1]; } break;
            case 2: for (int i = 2; i < s.Length; i++) { s[i] += (2 * s[i - 1]) - s[i - 2]; } break;
            case 3: for (int i = 3; i < s.Length; i++) { s[i] += (3 * s[i - 1]) - (3 * s[i - 2]) + s[i - 3]; } break;
            case 4: for (int i = 4; i < s.Length; i++) { s[i] += (4 * s[i - 1]) - (6 * s[i - 2]) + (4 * s[i - 3]) - s[i - 4]; } break;
        }
    }
    private static void RestoreLpc(Span<long> s, ReadOnlySpan<long> coefs, int shift)
    {
        int order = coefs.Length;
        for (int i = order; i < s.Length; i++)
        {
            long sum = 0;
            for (int j = 0; j < order; j++)
            {
                sum += coefs[j] * s[i - 1 - j];
            }

            s[i] += sum >> shift;
        }
    }
    private void Interleave(in FrameHeader frame)
    {
        int n = frame.BlockSize;
        float scale = 1f / (1L << (frame.BitsPerSample - 1));
        long[] l = _channelBuffers[0];
        if (_streamChannels == 1)
        {
            for (int i = 0; i < n; i++)
            {
                _pcm[i] = l[i] * scale;
            }
        }
        else if (_streamChannels == 2)
        {
            long[] r = _channelBuffers[1];
            for (int i = 0; i < n; i++)
            {
                _pcm[2 * i] = l[i] * scale;
                _pcm[(2 * i) + 1] = r[i] * scale;
            }
        }
        else
        {
            DownmixToStereo(n, scale);
        }
        _pcmOffset = 0;
        _pcmCount = n;
    }
    private void DownmixToStereo(int n, float scale)
    {
        int count = n * _streamChannels;
        if (_multi.Length < count)
        {
            _multi = new float[count];
        }

        for (int i = 0; i < n; i++)
        {
            for (int c = 0; c < _streamChannels; c++)
            {
                _multi[(i * _streamChannels) + c] = _channelBuffers[c][i] * scale;
            }
        }

        Downmix.ToStereo(_multi, _streamChannels, _pcm, n);
    }
    public void Seek(long frame)
    {
        long target = Math.Clamp(frame, 0, TotalFrames > 0 ? TotalFrames : long.MaxValue);
        _pcmCount = 0;
        _discard = 0;
        long offset = _firstFrame;
        if (target > 0)
        {
            int point = -1;
            for (int i = 0; i < _seekPoints.Count && _seekPoints[i].Sample <= target; i++)
            {
                point = i;
            }

            if (point >= 0)
            {
                offset = _firstFrame + _seekPoints[point].Offset;
            }
            else if (TotalFrames > 0)
            {
                offset = SearchOffset(target);
            }
        }
        _bits.SeekToByte(offset);
        if (offset != _firstFrame && !FindSync())
        {
            Position = TotalFrames;
            return;
        }
        while (DecodeFrame())
        {
            long first = _frameSample - _pcmCount;
            if (_frameSample > target)
            {
                _discard = (int)Math.Max(0, target - first);
                Position = first + _discard;
                return;
            }
            _pcmCount = 0;
        }
        Position = TotalFrames;
    }
    private long SearchOffset(long target)
    {
        long lo = _firstFrame;
        long hi = _src.Length;
        long best = _firstFrame;
        long margin = (long)SampleRate * 2;
        for (int step = 0; step < 24 && hi - lo > 64 * 1024; step++)
        {
            long guess = lo + (long)((hi - lo) * Math.Clamp((double)(target - (margin / 2)) / Math.Max(1, TotalFrames), 0.0, 1.0));
            if (step > 0)
            {
                guess = lo + ((hi - lo) / 2);
            }

            _bits.SeekToByte(guess);
            if (!FindSync())
            {
                hi = guess;
                continue;
            }
            long at = _bits.BytePosition;
            if (!TryReadFrameHeader(out FrameHeader header))
            {
                hi = guess;
                continue;
            }
            if (header.FirstSample > target)
            {
                hi = guess;
            }
            else
            {
                best = at;
                lo = at + 1;
                if (target - header.FirstSample < margin)
                {
                    break;
                }
            }
        }
        return best;
    }
    public void Dispose()
    {
        _src.Stream.Dispose();
    }

    private sealed class BitReader(ByteSource src)
    {
        private ulong _cache;
        private int _count;
        public long BytePosition => src.Position - (_count / 8);
        public bool AtByteBoundary => _count % 8 == 0;
        private void Uncache()
        {
            int bytes = _count / 8;
            long position = src.Position - bytes;
            _cache = 0;
            _count = 0;
            src.SeekTo(position);
        }
        public bool Ensure(int bytes)
        {
            Uncache();
            return src.Ensure(bytes);
        }
        public byte PeekByte(int offset)
        {
            return src[offset];
        }

        public void SeekToByte(long position)
        {
            _cache = 0;
            _count = 0;
            src.SeekTo(position);
        }
        public void AlignToByte()
        {
            int drop = _count % 8;
            _cache <<= drop;
            _count -= drop;
        }
        private void Refill()
        {
            int need = (64 - _count) / 8;
            if (!src.Ensure(need))
            {
                while (_count <= 56 && src.Ensure(1))
                {
                    _cache |= (ulong)src[0] << (56 - _count);
                    src.Skip(1);
                    _count += 8;
                }
                if (_count == 0)
                {
                    throw new EndOfStreamException();
                }

                return;
            }
            for (int i = 0; i < need; i++, _count += 8)
            {
                _cache |= (ulong)src[i] << (56 - _count);
            }

            src.Skip(need);
        }
        public uint ReadBits(int n)
        {
            if (n == 0)
            {
                return 0;
            }

            if (_count < n)
            {
                Refill();
            }

            if (_count < n)
            {
                throw new EndOfStreamException();
            }

            uint value = (uint)(_cache >> (64 - n));
            _cache <<= n;
            _count -= n;
            return value;
        }
        public void SkipBits(int n)
        {
            while (n > 0)
            {
                int take = Math.Min(n, 32);
                _ = ReadBits(take);
                n -= take;
            }
        }
        public long ReadSigned(int n)
        {
            if (n <= 32)
            {
                long value = ReadBits(n);
                return value << (64 - n) >> (64 - n);
            }
            long high = ReadBits(n - 32);
            long raw = (high << 32) | ReadBits(32);
            return raw << (64 - n) >> (64 - n);
        }
        public int ReadUnary()
        {
            int zeros = 0;
            while (true)
            {
                if (_count == 0)
                {
                    Refill();
                }

                if (_cache != 0)
                {
                    int lead = BitOperations.LeadingZeroCount(_cache);
                    if (lead < _count)
                    {
                        zeros += lead;
                        _cache <<= lead + 1;
                        _count -= lead + 1;
                        return zeros;
                    }
                }
                zeros += _count;
                _cache = 0;
                _count = 0;
            }
        }
        public long ReadRice(int k)
        {
            ulong q = (ulong)ReadUnary();
            ulong value = (q << k) | ReadBits(k);
            return (long)(value >> 1) ^ -(long)(value & 1);
        }
    }
}