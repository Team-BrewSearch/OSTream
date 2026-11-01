using OSTream.Core.Models;
using System.Collections.Generic;
using System.Linq;
namespace OSTream.Core.Player;

public sealed class PlayQueue
{
    private readonly List<Track> _tracks = [];
    private List<int> _order = [];
    public IReadOnlyList<Track> Tracks => _tracks;
    public IReadOnlyList<int> Order => _order;
    public int Position { get; private set; } = -1;
    public Track? Current => Position >= 0 && Position < _order.Count ? _tracks[_order[Position]] : null;
    public int CurrentQueueIndex => Position >= 0 && Position < _order.Count ? _order[Position] : -1;
    public bool HasNext => Position + 1 < _order.Count;
    public int Count => _order.Count;
    public int Version { get; private set; }
    public IEnumerable<(int QueueIndex, Track Track)> UpNext()
    {
        for (int i = Position + 1; i < _order.Count; i++)
        {
            yield return (_order[i], _tracks[_order[i]]);
        }
    }
    public Track? PeekNext()
    {
        return HasNext ? _tracks[_order[Position + 1]] : null;
    }

    private static List<int> BuildOrder(int count, int startQueueIndex, bool shuffle)
    {
        List<int> all = new(count);
        for (int i = 0; i < count; i++)
        {
            all.Add(i);
        }

        if (!shuffle)
        {
            return all;
        }

        int[] rest = Formatting.Shuffled(all.Where(i => i != startQueueIndex).ToArray());
        return startQueueIndex >= 0 ? [startQueueIndex, .. rest] : [.. rest];
    }
    public void SetList(IReadOnlyList<Track> tracks, int start, bool shuffle)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);
        _order = BuildOrder(tracks.Count, start, shuffle);
        Position = shuffle ? 0 : start;
        Version++;
    }
    public void InsertNext(IReadOnlyList<Track> tracks)
    {
        int at = _tracks.Count;
        _tracks.AddRange(tracks);
        _order.InsertRange(Position + 1, Enumerable.Range(at, tracks.Count));
        Version++;
    }
    public void Append(IReadOnlyList<Track> tracks)
    {
        int at = _tracks.Count;
        _tracks.AddRange(tracks);
        _order.AddRange(Enumerable.Range(at, tracks.Count));
        Version++;
    }
    public bool JumpTo(int queueIndex)
    {
        int pos = _order.IndexOf(queueIndex);
        if (pos < 0)
        {
            return false;
        }

        Position = pos;
        Version++;
        return true;
    }
    public bool Remove(int queueIndex)
    {
        int pos = _order.IndexOf(queueIndex);
        if (pos < 0 || pos == Position)
        {
            return false;
        }

        _order.RemoveAt(pos);
        if (pos < Position)
        {
            Position--;
        }

        Version++;
        return true;
    }
    public void ClearUpNext()
    {
        if (Position + 1 < _order.Count)
        {
            _order.RemoveRange(Position + 1, _order.Count - Position - 1);
        }

        Version++;
    }
    public bool Advance(bool repeatAll, bool shuffle)
    {
        if (Position + 1 < _order.Count)
        {
            Position++;
        }
        else if (repeatAll && _order.Count > 0)
        {
            if (shuffle)
            {
                int finished = _order[Position];
                _order = [.. Formatting.Shuffled(_order)];
                if (_order.Count > 1 && _order[0] == finished)
                {
                    (_order[0], _order[1]) = (_order[1], _order[0]);
                }
            }

            Position = 0;
        }
        else
        {
            return false;
        }

        Version++;
        return true;
    }
    public bool Back()
    {
        if (Position <= 0)
        {
            return false;
        }

        Position--;
        Version++;
        return true;
    }
    public void Reorder(bool shuffle)
    {
        if (Position < 0)
        {
            return;
        }

        int[] ahead = _order.Skip(Position + 1).Order().ToArray();
        _order = [.. _order.Take(Position + 1), .. shuffle ? Formatting.Shuffled(ahead) : ahead];
        Version++;
    }
}
