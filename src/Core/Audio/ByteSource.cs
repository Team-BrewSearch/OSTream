using System;
namespace OSTream.Core.Audio;
internal sealed class ByteSource(System.IO.Stream stream)
{
    readonly byte[] _buffer = new byte[64 * 1024];
    long _bufferStart; 
    int _length;
    int _pos;
    public System.IO.Stream Stream { get; } = stream;
    public long Length => Stream.Length;
    public long Position => _bufferStart + _pos;
    public bool Ensure(int count)
    {
        if (_length - _pos >= count) return true;
        if (_pos > 0)
        {
            Buffer.BlockCopy(_buffer, _pos, _buffer, 0, _length - _pos);
            _bufferStart += _pos;
            _length -= _pos;
            _pos = 0;
        }
        while (_length < count)
        {
            var read = Stream.Read(_buffer, _length, _buffer.Length - _length);
            if (read == 0) return false;
            _length += read;
        }
        return true;
    }
    public ReadOnlySpan<byte> Peek(int count) => _buffer.AsSpan(_pos, count);
    public byte this[int offset] => _buffer[_pos + offset];
    public void Skip(int count) => _pos += count;
    public void SkipLong(long count)
    {
        if (count <= _length - _pos)
        {
            _pos += (int)count;
            return;
        }
        SeekTo(Position + count);
    }
    public void SeekTo(long offset)
    {
        if (offset >= _bufferStart && offset <= _bufferStart + _length)
        {
            _pos = (int)(offset - _bufferStart);
            return;
        }
        Stream.Position = Math.Clamp(offset, 0, Stream.Length);
        _bufferStart = Stream.Position;
        _length = 0;
        _pos = 0;
    }
    public int Read(Span<byte> dest)
    {
        var total = 0;
        while (total < dest.Length)
        {
            if (_pos == _length && !Ensure(1)) break;
            var n = Math.Min(dest.Length - total, _length - _pos);
            _buffer.AsSpan(_pos, n).CopyTo(dest[total..]);
            _pos += n;
            total += n;
        }
        return total;
    }
    public void SkipId3v2()
    {
        while (Ensure(10))
        {
            var h = Peek(10);
            if (h[0] != 'I' || h[1] != 'D' || h[2] != '3') return;
            var size = (h[6] & 0x7F) << 21 | (h[7] & 0x7F) << 14 | (h[8] & 0x7F) << 7 | (h[9] & 0x7F);
            SkipLong(10L + size + ((h[5] & 0x10) != 0 ? 10 : 0));
        }
    }
}