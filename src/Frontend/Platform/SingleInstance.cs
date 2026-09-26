using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace OSTream.Platform;
public sealed class SingleInstance : IDisposable
{
    readonly FileStream _lock;
    readonly string _pipe;
    readonly CancellationTokenSource _stop = new();
    SingleInstance(FileStream fileLock, string pipe)
    {
        _lock = fileLock;
        _pipe = pipe;
    }
    public static bool TryAcquire(string dataDir, out SingleInstance? instance)
    {
        instance = null;
        var pipe = "ostream-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir))))[..16].ToLowerInvariant();
        try
        {
            Directory.CreateDirectory(dataDir);
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
    static void Signal(string pipe)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
            client.Connect(2000);
            client.WriteByte(1);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }
    public void Listen(Action activate) => _ = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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
    public void Dispose()
    {
        _stop.Cancel();
        _lock.Dispose();
    }
}
