using NVorbis;
using NVorbis.Contracts;
using NVorbis.Ogg;
using System;
using System.Collections.Generic;
using System.IO;
namespace OSTream.Core.Audio;

// Check comments and setup codebooks in every logical stream, including streams
// discovered during seeking or decoding.
internal sealed class SafeVorbisReader : IDisposable
{
    private readonly ContainerReader _container;
    private readonly List<StreamDecoder> _decoders = [];
    private readonly StreamDecoder _decoder;

    public SafeVorbisReader(Stream stream)
    {
        _container = new ContainerReader(stream, closeOnDispose: true);
        _container.NewStreamCallback = provider =>
        {
            _decoders.Add(new StreamDecoder(new HeaderCheckingProvider(provider)));
            return true;
        };
        try
        {
            if (!_container.TryInit() || _decoders.Count == 0)
            {
                throw new InvalidDataException("Not an Ogg Vorbis stream.");
            }
            _decoder = _decoders[0];
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Channels => _decoder.Channels;
    public int SampleRate => _decoder.SampleRate;
    public long TotalSamples => _decoder.TotalSamples;
    public int ReadSamples(Span<float> buffer) => _decoder.Read(buffer);
    public void SeekTo(long sample) => _decoder.SeekTo(sample);

    public void Dispose()
    {
        _container.NewStreamCallback = null!;
        foreach (StreamDecoder decoder in _decoders)
        {
            decoder.Dispose();
        }
        _decoders.Clear();
        _container.Dispose();
    }

    private sealed class HeaderCheckingProvider(IPacketProvider inner) : IPacketProvider, IDisposable
    {
        // Covers the reference array, per-string overhead, UTF-16 strings, temporary UTF-8
        // buffers, and the bounded copy used to prevent changes after validation.
        private const ulong MetadataBudget = 1024 * 1024;
        private const ulong StringOverhead = 48;
        private int _headerPackets;

        public bool CanSeek => inner.CanSeek;
        public int StreamSerial => inner.StreamSerial;
        public IPacket PeekNextPacket() => inner.PeekNextPacket();
        public long SeekTo(long granulePos, int preRoll, GetPacketGranuleCount getPacketGranuleCount) => inner.SeekTo(granulePos, preRoll, getPacketGranuleCount);
        public long GetGranuleCount() => inner.GetGranuleCount();
        public void Dispose() => (inner as IDisposable)?.Dispose();

        public IPacket GetNextPacket()
        {
            IPacket packet = inner.GetNextPacket();
            if (_headerPackets < 3 && packet is not null)
            {
                _headerPackets++;
                try
                {
                    return _headerPackets switch
                    {
                        2 => ValidateComments(packet),
                        3 => new SetupCheckingPacket(packet),
                        _ => packet,
                    };
                }
                catch
                {
                    packet.Done();
                    throw;
                }
            }
            return packet!; // NVorbis uses null to signal end of stream.
        }

        private static IPacket ValidateComments(IPacket packet)
        {
            // Check availability by reading the packet: BitsRemaining only covers the current
            // segment in NVorbis and is not the total length of a continued Ogg packet.
            using MemoryStream copy = new();
            ReadBits(packet, copy, 56); // signature is checked by NVorbis
            ulong budget = MetadataBudget - StringOverhead;
            ReadString(packet, copy, ref budget);
            ulong count = ReadBits(packet, copy, 32);
            ulong perComment = StringOverhead + (ulong)IntPtr.Size;
            if (count > budget / perComment)
            {
                throw new InvalidDataException("Ogg Vorbis comments exceed the metadata budget.");
            }
            budget -= count * perComment;
            for (ulong i = 0; i < count; i++)
            {
                ReadString(packet, copy, ref budget);
            }
            return new CommentPacket(packet, copy.ToArray());
        }

        private static void ReadString(IPacket packet, MemoryStream copy, ref ulong budget)
        {
            ulong length = ReadBits(packet, copy, 32);
            if (length > budget / 8)
            {
                throw new InvalidDataException("Ogg Vorbis comments exceed the metadata budget.");
            }
            budget -= length * 8;
            while (length > 0)
            {
                int bytes = (int)Math.Min(length, 8);
                ReadBits(packet, copy, bytes * 8);
                length -= (uint)bytes;
            }
        }

        private static ulong ReadBits(IPacket packet, MemoryStream copy, int bits)
        {
            ulong value = packet.TryPeekBits(bits, out int available);
            if (available != bits)
            {
                throw new InvalidDataException("Truncated Ogg Vorbis comments.");
            }
            packet.SkipBits(bits);
            for (int i = 0; i < bits; i += 8)
            {
                copy.WriteByte((byte)(value >> i));
            }
            return value;
        }

        private sealed class CommentPacket : DataPacket
        {
            private readonly IPacket _original;
            private readonly byte[] _data;
            private int _position;

            public CommentPacket(IPacket original, byte[] data)
            {
                _original = original;
                _data = data;
                IsResync = original.IsResync;
                IsEndOfStream = original.IsEndOfStream;
                GranulePosition = original.GranulePosition;
                ContainerOverheadBits = original.ContainerOverheadBits;
            }

            protected override int TotalBits => _data.Length * 8;
            protected override int ReadNextByte() => _position < _data.Length ? _data[_position++] : -1;
            public override void Reset()
            {
                _position = 0;
                base.Reset();
            }
            public override void Done() => _original.Done();
        }
    }
}
