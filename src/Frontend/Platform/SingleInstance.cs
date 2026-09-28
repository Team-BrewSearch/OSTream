using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Frontend.Platform;

public sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;
    private readonly string _pipe;
    private readonly CancellationTokenSource _stop = new();
    private SingleInstance(FileStream fileLock, string pipe)
    {
        _lock = fileLock;
        _pipe = pipe;
    }
    public static bool TryAcquire(string dataDir, out SingleInstance? instance)
    {
        instance = null;
        string pipe = "ostream-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir))))[..16].ToLowerInvariant();
        try
        {
            _ = Directory.CreateDirectory(dataDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
        try
        {
            instance = new SingleInstance(new FileStream(Path.Combine(dataDir, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose), pipe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            Signal(pipe);
            return false;
        }
    }
    private static void Signal(string pipe)
    {
        try
        {
            using NamedPipeClientStream client = new(".", pipe, PipeDirection.Out);
            client.Connect(2000);
            client.WriteByte(1);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }
    public void Listen(Action activate)
    {
        _ = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using NamedPipeServerStream server = new(_pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                activate();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _lock.Dispose();
    }
}
