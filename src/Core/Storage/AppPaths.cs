using System;
using System.IO;
namespace OSTream.Core.Storage;
public static class AppPaths
{
    public static string ConfigDir { get; } = 
        Environment.GetEnvironmentVariable("KHI_CONFIG_DIR") is { Length: > 0 } env 
            ? Normalize(env) 
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Config.AppName);
    public static string CacheDir { get; } = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Config.AppName, "Cache")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), Config.AppName);
    public static string? DownloadDirOverride { get; set; }
    public static string DefaultDownloadDir
    {
        get
        {
            if (Environment.GetEnvironmentVariable("KHI_DOWNLOAD_DIR") is { Length: > 0 } env) return Normalize(env);
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrEmpty(music)) music = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
            return Path.Combine(music, "KHInsider");
        }
    }
    public static string Normalize(string path)
    {
        path = path.Trim();
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path[2..] : "");
        try { return Path.GetFullPath(path); }
        catch (ArgumentException) { return path; }
    }
}