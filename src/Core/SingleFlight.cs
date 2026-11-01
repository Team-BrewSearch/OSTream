using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace OSTream.Core;

public sealed class SingleFlight<T>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<T>>> _inFlight = new();
    public Task<T> Run(string key, Func<string, Task<T>> start)
    {
        Lazy<Task<T>>? mine = null;
        Lazy<Task<T>> entry = _inFlight.GetOrAdd(key, k => mine = new Lazy<Task<T>>(() => Start(k, start)));
        if (ReferenceEquals(entry, mine))
        {
            _ = entry.Value.ContinueWith(_ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<T>>>(key, entry)),
                TaskContinuationOptions.ExecuteSynchronously);
        }

        return entry.Value;
    }
    private static async Task<T> Start(string key, Func<string, Task<T>> start)
    {
        return await start(key).ConfigureAwait(false);
    }
}
