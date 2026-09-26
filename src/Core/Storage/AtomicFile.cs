using System;
using System.IO;
using System.Threading.Tasks;
namespace OSTream.Core.Storage;

public static class AtomicFile
{
    public const string TempSuffix = ".part";
    /// <summary>Writes to a temporary file beside the destination, then moves it over the destination.</summary>
    /// <remarks>Write and move errors propagate. Temporary-file deletion is attempted even if writing or moving fails.</remarks>
    public static void Write(string path, Action<Stream> write)
    {
        string temp = path + TempSuffix;
        try
        {
            using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }
    /// <summary>Asynchronously writes to a temporary file beside the destination, then moves it over the destination.</summary>
    /// <remarks>Write and move errors propagate. Temporary-file deletion is attempted even if writing or moving fails.</remarks>
    public static async Task WriteAsync(string path, Func<Stream, Task> write)
    {
        string temp = path + TempSuffix;
        try
        {
            await using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 0, useAsync: true))
            {
                await write(stream).ConfigureAwait(false);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }
    /// <summary>Deletes a file, ignoring I/O and access errors.</summary>
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
