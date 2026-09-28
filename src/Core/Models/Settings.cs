using System.Text.Json.Serialization;
namespace OSTream.Core.Models;

public enum ThemeChoice { System, Light, Dark }
public enum AudioFormat { Flac, Mp3, Ogg, Wav }
public enum RepeatMode { Off, All, One }
public enum RailTab
{
    Downloads,
    [JsonStringEnumMemberName("upnext")] UpNext,
}
public sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized);
public sealed record SettingsData
{
    public ThemeChoice Theme { get; init; } = ThemeChoice.System;
    public AudioFormat Format { get; init; } = AudioFormat.Flac;
    public double Volume { get; init; } = 0.7;
    public bool Muted { get; init; }
    public bool Shuffle { get; init; }
    public RepeatMode Repeat { get; init; } = RepeatMode.Off;
    public bool RailCollapsed { get; init; }
    public RailTab RailTab { get; init; } = RailTab.Downloads;
    public string DownloadDir { get; init; } = "";
    public WindowBounds? Window { get; init; }
}
public static class AudioFormatExtensions
{
    private static readonly AudioFormat[] FlacFirst = [AudioFormat.Flac, AudioFormat.Wav, AudioFormat.Mp3, AudioFormat.Ogg];
    private static readonly AudioFormat[] WavFirst = [AudioFormat.Wav, AudioFormat.Flac, AudioFormat.Mp3, AudioFormat.Ogg];
    private static readonly AudioFormat[] Mp3First = [AudioFormat.Mp3, AudioFormat.Ogg, AudioFormat.Flac, AudioFormat.Wav];
    private static readonly AudioFormat[] OggFirst = [AudioFormat.Ogg, AudioFormat.Mp3, AudioFormat.Flac, AudioFormat.Wav];
    public static string Key(this AudioFormat format)
    {
        return format switch
        {
            AudioFormat.Mp3 => "mp3",
            AudioFormat.Ogg => "ogg",
            AudioFormat.Wav => "wav",
            _ => "flac",
        };
    }

    public static string Label(this AudioFormat format)
    {
        return format.Key().ToUpperInvariant();
    }

    public static AudioFormat[] Preference(this AudioFormat format)
    {
        return format switch
        {
            AudioFormat.Mp3 => Mp3First,
            AudioFormat.Ogg => OggFirst,
            AudioFormat.Wav => WavFirst,
            _ => FlacFirst,
        };
    }
}
