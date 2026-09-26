using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using OSTream.Core;
using OSTream.Core.Khinsider;
using OSTream.Core.Models;
using OSTream.Core.Player;
using Tmds.DBus.Protocol;
namespace OSTream.Platform;
sealed class MprisSession : IMediaSession, IPathMethodHandler
{
    const string ObjectPathValue = "/org/mpris/MediaPlayer2";
    const string RootInterface = "org.mpris.MediaPlayer2";
    const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    readonly AppServices _app;
    DBusConnection? _connection;
    volatile NowPlaying _now;
    volatile string _artUrl = "";
    long _positionUs;
    long _trackSerial;
    volatile string? _trackId;
    double _lastTime;
    bool _disposed;
    public MprisSession(AppServices app)
    {
        _app = app;
        _now = MediaSessions.Snapshot(app);
        app.Player.Changed += OnPlayerChanged;
        app.Player.TimeChanged += OnTime;
        app.Settings.Changed += OnSettingsChanged;
        _ = StartAsync();
    }
    public string Path => ObjectPathValue;
    public bool HandlesChildPaths => false;
    async Task StartAsync()
    {
        DBusConnection? connection = null;
        try
        {
            var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
            if (string.IsNullOrEmpty(address)) return;
            connection = new DBusConnection(address);
            await connection.ConnectAsync();
            connection.AddMethodHandler(this);
            if (!await connection.TryRequestNameAsync("org.mpris.MediaPlayer2.ostream", RequestNameOptions.None))
                await connection.RequestNameAsync($"org.mpris.MediaPlayer2.ostream.instance{Environment.ProcessId}", RequestNameOptions.None);
            if (_disposed)
            {
                connection.Dispose();
                return;
            }
            _connection = connection;
        }
        catch (Exception)
        {
            _connection = null;
            connection?.Dispose();
        }
    }
    void OnPlayerChanged()
    {
        var previous = _now;
        var now = MediaSessions.Snapshot(_app);
        _now = now;
        if (!ReferenceEquals(previous.Track, now.Track))
        {
            _trackId = $"/org/ostream/track/{++_trackSerial}";
            _artUrl = "";
            _lastTime = 0;
            if (now.Track?.Album.Thumb is { Length: > 0 } thumb) LoadArtAsync(thumb, now.Track).Forget(_app.Log);
        }
        EmitPropertiesChanged(PlayerProperties());
    }
    void OnSettingsChanged(SettingsData old, SettingsData now)
    {
        if (old.Volume == now.Volume && old.Muted == now.Muted && old.Shuffle == now.Shuffle && old.Repeat == now.Repeat) return;
        _now = MediaSessions.Snapshot(_app);
        var all = PlayerProperties();
        EmitPropertiesChanged(new Dictionary<string, VariantValue>
        {
            ["Volume"] = all["Volume"],
            ["Shuffle"] = all["Shuffle"],
            ["LoopStatus"] = all["LoopStatus"],
            ["CanGoNext"] = all["CanGoNext"],
        });
    }
    async Task LoadArtAsync(string url, Track track)
    {
        var file = await _app.Images.EnsureFileAsync(url);
        if (file is null || !ReferenceEquals(_now.Track, track)) return;
        _artUrl = new Uri(file).AbsoluteUri;
        EmitPropertiesChanged(new Dictionary<string, VariantValue> { ["Metadata"] = Metadata() });
    }
    void OnTime(PlaybackTime time)
    {
        var jumped = Math.Abs(time.Current - _lastTime) > 1.5;
        _lastTime = time.Current;
        System.Threading.Interlocked.Exchange(ref _positionUs, (long)(time.Current * 1_000_000));
        if (jumped) EmitSeeked(System.Threading.Interlocked.Read(ref _positionUs));
    }
    static Dictionary<string, VariantValue> RootProperties() => new()
    {
        ["CanQuit"] = true,
        ["CanRaise"] = true,
        ["HasTrackList"] = false,
        ["Identity"] = Config.AppName,
        ["DesktopEntry"] = "ostream",
        ["SupportedUriSchemes"] = new Array<string>().AsVariantValue(),
        ["SupportedMimeTypes"] = new Array<string>().AsVariantValue(),
    };
    Dictionary<string, VariantValue> PlayerProperties()
    {
        var now = _now;
        return new Dictionary<string, VariantValue>
        {
            ["PlaybackStatus"] = now.Track is null ? "Stopped" : now.IsPlaying ? "Playing" : "Paused",
            ["LoopStatus"] = now.Repeat switch { RepeatMode.One => "Track", RepeatMode.All => "Playlist", _ => "None" },
            ["Rate"] = 1.0,
            ["Shuffle"] = now.Shuffle,
            ["Metadata"] = Metadata(),
            ["Volume"] = now.Volume,
            ["Position"] = System.Threading.Interlocked.Read(ref _positionUs),
            ["MinimumRate"] = 1.0,
            ["MaximumRate"] = 1.0,
            ["CanGoNext"] = now.CanGoNext,
            ["CanGoPrevious"] = now.CanGoPrevious,
            ["CanPlay"] = now.Track is not null,
            ["CanPause"] = now.Track is not null,
            ["CanSeek"] = now.Track is not null,
            ["CanControl"] = true,
        };
    }
    VariantValue Metadata()
    {
        var track = _now.Track;
        var dict = new Dict<string, VariantValue>();
        if (track is null)
        {
            dict.Add("mpris:trackid", new ObjectPath("/org/mpris/MediaPlayer2/TrackList/NoTrack"));
            return dict.AsVariantValue();
        }
        dict.Add("mpris:trackid", new ObjectPath(_trackId ?? "/org/ostream/track/0"));
        dict.Add("xesam:title", track.Title);
        dict.Add("xesam:album", track.Album.Title);
        dict.Add("xesam:artist", new Array<string>([track.Album.Title.Length > 0 ? track.Album.Title : "KHInsider"]).AsVariantValue());
        if (track.DurationSec > 0) dict.Add("mpris:length", (long)track.DurationSec * 1_000_000);
        if (_artUrl.Length > 0) dict.Add("mpris:artUrl", _artUrl);
        else if (KhinsiderClient.SecureMediaUrl(track.Album.Thumb) is { } art) dict.Add("mpris:artUrl", art);
        return dict.AsVariantValue();
    }
    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var iface = request.InterfaceAsString;
        var member = request.MemberAsString;
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([Encoding.UTF8.GetBytes(IntrospectXml)], ReadOnlySpan<string>.Empty);
            return ValueTask.CompletedTask;
        }
        if (iface == PropertiesInterface)
        {
            var reader = request.GetBodyReader();
            switch (member)
            {
                case "Get":
                {
                    var target = reader.ReadString();
                    var name = reader.ReadString();
                    var props = target == RootInterface ? RootProperties() : PlayerProperties();
                    if (!props.TryGetValue(name, out var value))
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"No property {name}");
                        return ValueTask.CompletedTask;
                    }
                    using var writer = context.CreateReplyWriter("v");
                    writer.WriteVariant(value);
                    context.Reply(writer.CreateMessage());
                    return ValueTask.CompletedTask;
                }
                case "GetAll":
                {
                    var target = reader.ReadString();
                    using var writer = context.CreateReplyWriter("a{sv}");
                    writer.WriteDictionary(target == RootInterface ? RootProperties() : target == PlayerInterface ? PlayerProperties() : []);
                    context.Reply(writer.CreateMessage());
                    return ValueTask.CompletedTask;
                }
                case "Set":
                {
                    reader.ReadString();
                    var name = reader.ReadString();
                    var value = reader.ReadVariantValue();
                    Ui(() => SetProperty(name, value));
                    ReplyEmpty(context);
                    return ValueTask.CompletedTask;
                }
            }
        }
        else if (iface == RootInterface)
        {
            if (member == "Raise") Ui(_app.Shell.Raise);
            else if (member == "Quit") Ui(() => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Close());
            ReplyEmpty(context);
            return ValueTask.CompletedTask;
        }
        else if (iface == PlayerInterface)
        {
            var player = _app.Player;
            switch (member)
            {
                case "Next": Ui(() => player.Next()); break;
                case "Previous": Ui(player.Prev); break;
                case "Pause": Ui(() => { if (player.Status == PlayStatus.Playing) player.Toggle(); }); break;
                case "Play": Ui(() => { if (player.Status != PlayStatus.Playing) player.Toggle(); }); break;
                case "PlayPause": Ui(player.Toggle); break;
                case "Stop": Ui(() => { if (player.Status == PlayStatus.Playing) player.Toggle(); player.Seek(0); }); break;
                case "Seek":
                {
                    var offset = request.GetBodyReader().ReadInt64();
                    Ui(() => player.SeekBy(offset / 1_000_000.0));
                    break;
                }
                case "SetPosition":
                {
                    var reader = request.GetBodyReader();
                    var trackId = reader.ReadObjectPathAsString();
                    var position = reader.ReadInt64();
                    if (trackId == _trackId) Ui(() => player.Seek(position / 1_000_000.0));
                    break;
                }
                case "OpenUri": break;
                default:
                    context.ReplyUnknownMethodError();
                    return ValueTask.CompletedTask;
            }
            ReplyEmpty(context);
            return ValueTask.CompletedTask;
        }
        context.ReplyUnknownMethodError();
        return ValueTask.CompletedTask;
    }
    void SetProperty(string name, VariantValue value)
    {
        var player = _app.Player;
        switch (name)
        {
            case "Shuffle" when value.Type == VariantValueType.Bool && value.GetBool() != _app.Settings.Value.Shuffle:
                player.ToggleShuffle();
                break;
            case "LoopStatus" when value.Type == VariantValueType.String:
                var target = value.GetString() switch { "Track" => RepeatMode.One, "Playlist" => RepeatMode.All, _ => RepeatMode.Off };
                for (var i = 0; i < 3 && _app.Settings.Value.Repeat != target; i++) player.CycleRepeat();
                break;
            case "Volume" when value.Type == VariantValueType.Double:
                player.SetVolume(value.GetDouble());
                break;
        }
    }
    static void Ui(Action action) => Dispatcher.UIThread.Post(action);
    static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected) return;
        using var writer = context.CreateReplyWriter(null!);
        context.Reply(writer.CreateMessage());
    }
    void EmitPropertiesChanged(Dictionary<string, VariantValue> changed)
    {
        if (_connection is not { } connection) return;
        try
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(null, ObjectPathValue, PropertiesInterface, "PropertiesChanged", "sa{sv}as");
            writer.WriteString(PlayerInterface);
            writer.WriteDictionary(changed);
            writer.WriteArray(Array.Empty<string>());
            connection.TrySendMessage(writer.CreateMessage());
        }
        catch (Exception)
        {
        }
    }
    void EmitSeeked(long positionUs)
    {
        if (_connection is not { } connection) return;
        try
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(null, ObjectPathValue, PlayerInterface, "Seeked", "x");
            writer.WriteInt64(positionUs);
            connection.TrySendMessage(writer.CreateMessage());
        }
        catch (Exception)
        {
        }
    }
    public void Dispose()
    {
        _disposed = true;
        _app.Player.Changed -= OnPlayerChanged;
        _app.Player.TimeChanged -= OnTime;
        _app.Settings.Changed -= OnSettingsChanged;
        _connection?.Dispose();
    }
    const string IntrospectXml = """
        <interface name="org.mpris.MediaPlayer2">
          <method name="Raise"/><method name="Quit"/>
          <property name="CanQuit" type="b" access="read"/><property name="CanRaise" type="b" access="read"/>
          <property name="HasTrackList" type="b" access="read"/><property name="Identity" type="s" access="read"/>
          <property name="DesktopEntry" type="s" access="read"/>
          <property name="SupportedUriSchemes" type="as" access="read"/><property name="SupportedMimeTypes" type="as" access="read"/>
        </interface>
        <interface name="org.mpris.MediaPlayer2.Player">
          <method name="Next"/><method name="Previous"/><method name="Pause"/><method name="PlayPause"/><method name="Stop"/><method name="Play"/>
          <method name="Seek"><arg direction="in" name="Offset" type="x"/></method>
          <method name="SetPosition"><arg direction="in" name="TrackId" type="o"/><arg direction="in" name="Position" type="x"/></method>
          <method name="OpenUri"><arg direction="in" name="Uri" type="s"/></method>
          <signal name="Seeked"><arg name="Position" type="x"/></signal>
          <property name="PlaybackStatus" type="s" access="read"/><property name="LoopStatus" type="s" access="readwrite"/>
          <property name="Rate" type="d" access="readwrite"/><property name="Shuffle" type="b" access="readwrite"/>
          <property name="Metadata" type="a{sv}" access="read"/><property name="Volume" type="d" access="readwrite"/>
          <property name="Position" type="x" access="read"/><property name="MinimumRate" type="d" access="read"/>
          <property name="MaximumRate" type="d" access="read"/><property name="CanGoNext" type="b" access="read"/>
          <property name="CanGoPrevious" type="b" access="read"/><property name="CanPlay" type="b" access="read"/>
          <property name="CanPause" type="b" access="read"/><property name="CanSeek" type="b" access="read"/>
          <property name="CanControl" type="b" access="read"/>
        </interface>
        """;
}