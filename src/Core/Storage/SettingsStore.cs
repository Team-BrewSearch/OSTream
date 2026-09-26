using OSTream.Core.Models;
using System;
using System.IO;
namespace OSTream.Core.Storage;

public sealed class SettingsStore : IDisposable
{
    public SettingsStore() : this(Path.Combine(AppPaths.ConfigDir, "settings.json")) { }
    public SettingsStore(string path)
    {
        Store = new(path, JsonContext.Default.SettingsData, new SettingsData());
    }
    internal JsonStore<SettingsData> Store { get; }
    public SettingsData Value => Store.Value;
    public event Action<SettingsData, SettingsData>? Changed;
    public void Update(Func<SettingsData, SettingsData> patch)
    {
        SettingsData old = Value;
        SettingsData next = patch(old);
        if (next == old)
        {
            return;
        }
        Store.Set(next);
        Changed?.Invoke(old, next);
    }
    public string DownloadDir =>
        AppPaths.DownloadDirOverride ?? (Value.DownloadDir is { Length: > 0 } dir ? dir : AppPaths.DefaultDownloadDir);
    public void Dispose()
    {
        Store.Dispose();
    }
}