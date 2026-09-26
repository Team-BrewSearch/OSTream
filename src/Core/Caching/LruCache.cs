using System;
using System.Collections.Generic;
using System.Threading;
namespace OSTream.Core.Caching;
public sealed class LruCache<TKey, TValue>(long capacity, Func<TValue, long>? weigh = null, TimeSpan? ttl = null) where TKey : notnull
{
    readonly Dictionary<TKey, LinkedListNode<Entry>> _map = [];
    readonly LinkedList<Entry> _order = new();
    readonly Lock _lock = new();
    long _weight;
    public int Count
    {
        get
        {
            lock (_lock) return _map.Count;
        }
    }
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                if (node.Value.Expires > Environment.TickCount64)
                {
                    _order.Remove(node);
                    _order.AddLast(node);
                    value = node.Value.Value;
                    return true;
                }
                Unlink(node);
            }
            value = default!;
            return false;
        }
    }
    public void Set(TKey key, TValue value)
    {
        var weight = weigh?.Invoke(value) ?? 1;
        var expires = ttl is { } t ? Environment.TickCount64 + (long)t.TotalMilliseconds : long.MaxValue;
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing)) Unlink(existing);
            if (weight > capacity) return;
            _map[key] = _order.AddLast(new Entry(key, value, weight, expires));
            _weight += weight;
            while (_weight > capacity && _order.First is { } oldest) Unlink(oldest);
        }
    }
    public void Remove(TKey key)
    {
        lock (_lock)
            if (_map.TryGetValue(key, out var node)) Unlink(node);
    }
    void Unlink(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _map.Remove(node.Value.Key);
        _weight -= node.Value.Weight;
    }
    readonly record struct Entry(TKey Key, TValue Value, long Weight, long Expires);
}
