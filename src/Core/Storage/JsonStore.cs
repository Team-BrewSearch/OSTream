using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using OSTream.Core.Models;
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
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
    readonly string _path;
    readonly JsonTypeInfo<T> _type;
    readonly Timer _timer;
    readonly Lock _lock = new();
    readonly Lock _writeLock = new();
    T? _pending;
    bool _readOnly;
    bool _failing;
    public event Action<Exception>? FlushFailed;
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
    public bool IsReadOnly => _readOnly;
    T? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, _type);
        }
        catch (JsonException ex)
        {
            LoadError = ex;
            try
            {
                var stem = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                var backup = stem;
                for (var n = 2; File.Exists(backup); n++) backup = $"{stem}-{n}";
                File.Copy(_path, backup);
                BackupPath = backup;
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
                _readOnly = true;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LoadError = ex;
            _readOnly = true;
            return null;
        }
    }
    public void Set(T value)
    {
        Value = value;
        if (_readOnly) return;
        lock (_lock)
        {
            _pending = value;
            if (!_disposed)
            {
                _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
                return;
            }
        }
        Flush();
    }
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
            if (value is null || _readOnly) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.Write(_path, stream => JsonSerializer.Serialize(stream, value, _type));
                _failing = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
            {
                lock (_lock) _pending ??= value;
                if (_failing) return;
                _failing = true;
                FlushFailed?.Invoke(ex);
            }
        }
    }
    bool _disposed;
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
        try { Flush(); } catch { }
    }
}
