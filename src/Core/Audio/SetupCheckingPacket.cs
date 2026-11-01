using NVorbis.Contracts;
using System;
using System.IO;

namespace OSTream.Core.Audio;

// Tracks the reads made by NVorbis 1.0.0-rc.2 while it initializes codebooks.
// Checking the same reads avoids rereading mutable Ogg packet data after validation.
internal sealed class SetupCheckingPacket(IPacket inner) : IPacket
{
    private enum State { Signature, BookCount, Sync, Dimensions, Entries, Ordered, InitialLength, OrderedCount, Sparse, Present, Length, Map, Minimum, Delta, ValueBits, Sequence, Values, Done }
    private State _state = State.Signature;
    private int _signatureBytes, _books, _dimensions, _entries, _remaining, _length, _values, _map;
    private bool _sparse;

    public bool IsResync => inner.IsResync;
    public bool IsShort => inner.IsShort;
    public long? GranulePosition => inner.GranulePosition;
    public bool IsEndOfStream => inner.IsEndOfStream;
    public int BitsRead => inner.BitsRead;
    public int BitsRemaining => inner.BitsRemaining;
    public int ContainerOverheadBits => inner.ContainerOverheadBits;
    public ulong TryPeekBits(int count, out int bitsRead) => inner.TryPeekBits(count, out bitsRead);
    public void SkipBits(int count) => inner.SkipBits(count);
    public void Done() => inner.Done();
    public void Reset()
    {
        inner.Reset();
        _state = State.Signature;
        _signatureBytes = 0;
    }

    public ulong ReadBits(int count)
    {
        if (_state == State.Done) return inner.ReadBits(count);
        if (_state == State.OrderedCount && _length > 32)
            throw new InvalidDataException("Invalid Ogg Vorbis ordered codebook.");
        ulong value = inner.TryPeekBits(count, out int available);
        if (available != count) throw new InvalidDataException("Truncated Ogg Vorbis codebook.");
        inner.SkipBits(count);
        switch (_state)
        {
            case State.Signature:
                if (++_signatureBytes == 7) _state = State.BookCount;
                break;
            case State.BookCount:
                _books = (int)value + 1;
                _state = State.Sync;
                break;
            case State.Sync: _state = State.Dimensions; break;
            case State.Dimensions: _dimensions = (int)value; _state = State.Entries; break;
            case State.Entries: _entries = _remaining = (int)value; _state = State.Ordered; break;
            case State.Ordered: _state = value != 0 ? State.InitialLength : State.Sparse; break;
            case State.InitialLength:
                _length = (int)value + 1;
                _state = _remaining == 0 ? State.Map : State.OrderedCount;
                break;
            case State.OrderedCount:
                if (value > (ulong)_remaining)
                    throw new InvalidDataException("Invalid Ogg Vorbis ordered codebook.");
                _remaining -= (int)value;
                _length++;
                if (_remaining == 0) _state = State.Map;
                break;
            case State.Sparse:
                _sparse = value != 0;
                _state = _remaining == 0 ? State.Map : _sparse ? State.Present : State.Length;
                break;
            case State.Present:
                if (value != 0) _state = State.Length;
                else NextEntry();
                break;
            case State.Length: NextEntry(); break;
            case State.Map:
                _map = (int)value;
                if (_map == 0) NextBook();
                else _state = State.Minimum;
                break;
            case State.Minimum: _state = State.Delta; break;
            case State.Delta: _state = State.ValueBits; break;
            case State.ValueBits: _state = State.Sequence; break;
            case State.Sequence:
                // Match the pinned decoder's lookup1_values and integer arithmetic.
                _values = unchecked(_entries * _dimensions);
                if (_map == 1)
                {
                    _values = (int)MathF.Floor(MathF.Exp(MathF.Log(_entries) / _dimensions));
                    if (MathF.Floor(MathF.Pow(_values + 1, _dimensions)) <= _entries) _values++;
                }
                if (_values <= 0) NextBook();
                else _state = State.Values;
                break;
            case State.Values:
                if (--_values == 0) NextBook();
                break;
        }
        return value;
    }

    private void NextEntry()
    {
        _remaining--;
        _state = _remaining == 0 ? State.Map : _sparse ? State.Present : State.Length;
    }

    private void NextBook() => _state = --_books == 0 ? State.Done : State.Sync;
}
