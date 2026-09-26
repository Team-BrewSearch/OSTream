using OSTream.Core.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
namespace OSTream.Core.Storage;

public sealed class LowercaseEnumConverter<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.SnakeCaseLower) where T : struct, Enum;
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    Converters = [typeof(LowercaseEnumConverter<ThemeChoice>), typeof(LowercaseEnumConverter<AudioFormat>),
                  typeof(LowercaseEnumConverter<RepeatMode>), typeof(LowercaseEnumConverter<RailTab>)])]
[JsonSerializable(typeof(SettingsData))]
[JsonSerializable(typeof(LibraryData))]
internal sealed partial class JsonContext : JsonSerializerContext;
public sealed class JsonStore<T> : IDisposable where T : class
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
    private readonly string _path;
    private readonly JsonTypeInfo<T> _type;
    private readonly Timer _timer;
    private readonly Lock _lock = new();
    private readonly Lock _writeLock = new();
    private T? _pending;
    private bool _failing;
    public event Action<Exception>? FlushFailed;
    /// <summary>Loads a JSON value, or uses the fallback if the file is missing, unreadable, invalid, or contains null.</summary>
    /// <remarks>Invalid JSON is copied to a backup when possible. A caught file-access error or failed backup makes the store read-only. Normalization applies only to a loaded value, and its exceptions propagate.</remarks>
    public JsonStore(string path, JsonTypeInfo<T> type, T fallback, Func<T, T>? normalize = null)
    {
        _path = path;
        _type = type;
        _timer = new Timer(_ => Flush());
        Value = Load() is { } loaded ? normalize?.Invoke(loaded) ?? loaded : fallback;
    }
    public T Value { get; private set; }
    public Exception? LoadError { get; private set; }
    public string? BackupPath { get; private set; }
    public bool IsReadOnly { get; private set; }
    private T? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }
        try
        {
            using FileStream stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, _type);
        }
        catch (JsonException ex)
        {
            LoadError = ex;
            try
            {
                string stem = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                string backup = stem;
                for (int n = 2; File.Exists(backup); n++)
                {
                    backup = $"{stem}-{n}";
                }
                File.Copy(_path, backup);
                BackupPath = backup;
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
                IsReadOnly = true;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LoadError = ex;
            IsReadOnly = true;
            return null;
        }
    }
    /// <summary>Updates the value and schedules a save 300 milliseconds after the latest set.</summary>
    /// <remarks>A read-only store updates its value in memory without scheduling a save. After disposal, a writable store flushes the value immediately.</remarks>
    public void Set(T value)
    {
        Value = value;
        if (IsReadOnly)
        {
            return;
        }
        lock (_lock)
        {
            _pending = value;
            if (!_disposed)
            {
                _ = _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
                return;
            }
        }
        Flush();
    }
    /// <summary>Attempts to save the pending value immediately.</summary>
    /// <remarks>I/O, access, unsupported-operation, and JSON errors leave the value pending and raise <see cref="FlushFailed"/> once until a save succeeds. Other errors, including errors from event handlers, propagate.</remarks>
    public void Flush()
    {
        lock (_writeLock)
        {
            T? value;
            lock (_lock)
            {
                value = _pending;
                _pending = null;
            }
            if (value is null || IsReadOnly)
            {
                return;
            }
            try
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.Write(_path, stream => JsonSerializer.Serialize(stream, value, _type));
                _failing = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
            {
                lock (_lock)
                {
                    _pending ??= value;
                }
                if (_failing)
                {
                    return;
                }
                _failing = true;
                FlushFailed?.Invoke(ex);
            }
        }
    }
    private bool _disposed;
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _timer.Dispose();
        }
        try { Flush(); } catch { }
    }
}
