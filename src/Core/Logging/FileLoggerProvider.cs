using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
namespace OSTream.Core.Logging;

public sealed class FileLoggerProvider(string directory, long maxBytes = 1024 * 1024, int keep = 3, TextWriter? echo = null) : ILoggerProvider
{
    public const string FileName = "ostream.log";
    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private bool _closed;
    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    private void Write(string entry)
    {
        lock (_lock)
        {
            echo?.WriteLine(entry);
            if (_closed)
            {
                return;
            }

            try
            {
                if (_writer is null || _writer.BaseStream.Length > maxBytes)
                {
                    Open();
                }

                _writer!.WriteLine(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _closed = true;
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
    private void Open()
    {
        _writer?.Dispose();
        _writer = null;
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
        {
            for (int i = keep; i >= 1; i--)
            {
                string from = i == 1 ? path : Path.Combine(directory, $"ostream.{i - 1}.log");
                if (File.Exists(from))
                {
                    File.Move(from, Path.Combine(directory, $"ostream.{i}.log"), overwrite: true);
                }
            }
        }
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
    }
    public void Dispose()
    {
        lock (_lock)
        {
            _closed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel is >= LogLevel.Information and not LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string entry = $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fff} {logLevel} {category}: {formatter(state, exception)}";
            owner.Write(exception is null ? entry : entry + Environment.NewLine + exception);
        }
    }
}
