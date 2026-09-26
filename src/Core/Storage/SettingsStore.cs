using System;
using System.IO;
using OSTream.Core.Models;
namespace OSTream.Core.Storage;
public sealed class SettingsStore : IDisposable
{
    readonly JsonStore<SettingsData> _store;
    public SettingsStore() : this(Path.Combine(AppPaths.ConfigDir, "settings.json")) { }
    public SettingsStore(string path) => _store = new(path, JsonContext.Default.SettingsData, new SettingsData());
    internal JsonStore<SettingsData> Store => _store;
    public SettingsData Value => _store.Value;
    public event Action<SettingsData, SettingsData>? Changed;
    public void Update(Func<SettingsData, SettingsData> patch)
    {
        var old = Value;
        var next = patch(old);
        if (next == old) return;
        _store.Set(next);
        Changed?.Invoke(old, next);
    }
    public string DownloadDir =>
        AppPaths.DownloadDirOverride ?? (Value.DownloadDir is { Length: > 0 } dir ? dir : AppPaths.DefaultDownloadDir);
    public void Dispose() => _store.Dispose();
}