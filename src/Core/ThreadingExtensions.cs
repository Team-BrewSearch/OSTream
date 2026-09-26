using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
namespace OSTream.Core;
public static class ThreadingExtensions
{
    public static void PostOrRun(this SynchronizationContext? context, Action action)
    {
        if (context is null) action();
        else context.Post(_ => action(), null);
    }
    public static void Forget(this Task task, ILogger log) =>
        task.ContinueWith(t => log.LogError(t.Exception, "Background task failed"), CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
