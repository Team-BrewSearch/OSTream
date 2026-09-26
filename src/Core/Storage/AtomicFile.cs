using System;
using System.IO;
using System.Threading.Tasks;
namespace OSTream.Core.Storage;
public static class AtomicFile
{
    public const string TempSuffix = ".part";
    public static void Write(string path, Action<Stream> write)
    {
        var temp = path + TempSuffix;
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) write(stream);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }
    public static async Task WriteAsync(string path, Func<Stream, Task> write)
    {
        var temp = path + TempSuffix;
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 0, useAsync: true))
                await write(stream).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
