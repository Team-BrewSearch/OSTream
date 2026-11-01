using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using DiscordRPC;
using DiscordRPC.IO;
using Microsoft.Extensions.Logging.Abstractions;
using OSTream.Core;
using OSTream.Core.Audio;
using OSTream.Core.Models;
using OSTream.Core.Player;
using OSTream.Frontend.Controls;
using OSTream.Frontend;
using OSTream.Services;

var downmix = typeof(AudioEngine).Assembly.GetType("OSTream.Core.Audio.Downmix")!
    .GetMethod("ToStereo")!.CreateDelegate<DownmixDelegate>();
const float c = 0.7071f * 0.5f;
// WAV order: FL, FR, FC, LFE, then rear/side channels (and back center in 6.1).
CheckChannels(6, [0.5f, 0.5f, c, 0, c, c]);
CheckChannels(7, [0.5f, 0.5f, c, 0, c, c, c]);
CheckChannels(8, [0.5f, 0.5f, c, 0, c, c, c, c]);

for (int channels = 3; channels <= 8; channels++)
{
    // Exercise both polarities and every combination of full-scale channels.
    for (int mask = 0; mask < 1 << channels; mask++)
    {
        float[] input = Enumerable.Range(0, channels).Select(i => (mask & (1 << i)) == 0 ? -1f : 1f).ToArray();
        float[] output = new float[2];
        downmix(input, channels, output, 1);
        Assert(output.All(sample => sample >= -1f && sample <= 1f), $"{channels}-channel downmix exceeded full scale");
    }
}

float[] quietOutput = new float[2];
downmix(Enumerable.Repeat(0.25f, 8).ToArray(), 8, quietOutput, 1);
Assert(Math.Abs(quietOutput[0] - (0.125f + (3 * c * 0.25f))) < 0.00001f && quietOutput[0] == quietOutput[1],
    "In-range multichannel gain changed");

// Use an offline RPC client to exercise presence updates without Discord or audio hardware.
using (var rpc = new DiscordRpcClient("123456789012345678", -1, null, false, new OfflinePipe()))
{
    Assert(rpc.Initialize(), "Offline RPC client did not initialize");
    var player = (PlayerService)RuntimeHelpers.GetUninitializedObject(typeof(PlayerService));
    var queue = new PlayQueue();
    queue.SetList([new Track { Title = "First" }, new Track { Title = "Second" }], 0, false);
    SetField(player, "<Queue>k__BackingField", queue);
    SetField(player, "<Status>k__BackingField", PlayStatus.Playing);
    var app = (AppServices)RuntimeHelpers.GetUninitializedObject(typeof(AppServices));
    SetField(app, "<Player>k__BackingField", player);
    Type serviceType = typeof(AppServices).Assembly.GetType("OSTream.Frontend.Services.DiscordPresenceService")!;
    object service = RuntimeHelpers.GetUninitializedObject(serviceType);
    object presenceLock = new();
    SetField(service, "_app", app);
    SetField(service, "_log", NullLogger.Instance);
    SetField(service, "_client", rpc);
    SetField(service, "_presenceLock", presenceLock);
    MethodInfo onTime = serviceType.GetMethod("OnTimeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
    MethodInfo update = serviceType.GetMethod("UpdatePresence", BindingFlags.Instance | BindingFlags.NonPublic)!;
    FieldInfo start = serviceType.GetField("_presenceStart", BindingFlags.Instance | BindingFlags.NonPublic)!;

    onTime.Invoke(service, [new PlaybackTime(0, 0, 0)]);
    Assert(start.GetValue(service) is DateTime && rpc.CurrentPresence?.Details == "First", "Missing timestamp did not restore presence");
    DateTime near = DateTime.UtcNow.AddSeconds(-1);
    start.SetValue(service, near);
    onTime.Invoke(service, [new PlaybackTime(1, 0, 0)]);
    Assert((DateTime)start.GetValue(service)! == near, "Ordinary playback caused a presence update");
    onTime.Invoke(service, [new PlaybackTime(10, 0, 0)]);
    Assert((DateTime)start.GetValue(service)! > near, "Playback drift did not refresh presence");

    using var entered = new ManualResetEventSlim();
    Task pending;
    lock (presenceLock)
    {
        pending = Task.Run(() => { entered.Set(); update.Invoke(service, null); });
        Assert(entered.Wait(TimeSpan.FromSeconds(5)) && !pending.Wait(100), "Presence update did not wait for the active update");
        Assert(queue.JumpTo(1), "Could not change tracks");
    }
    Assert(pending.Wait(TimeSpan.FromSeconds(5)), "Presence update did not complete");
    Assert(rpc.CurrentPresence?.Details == "Second" && start.GetValue(service) is DateTime, "Concurrent update submitted stale presence");
}

static void SetField(object target, string name, object value)
{
    target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}

void CheckChannels(int channels, float[] contribution)
{
    for (int channel = 0; channel < channels; channel++)
    {
        float[] input = new float[channels];
        float[] output = new float[2];
        input[channel] = 1;
        downmix(input, channels, output, 1);
        bool left = channel == 0 || channel == 2 || (channels == 6 && channel == 4)
            || (channels == 7 && (channel == 4 || channel == 5)) || (channels == 8 && (channel == 4 || channel == 6));
        bool right = channel == 1 || channel == 2 || (channels == 6 && channel == 5)
            || (channels == 7 && (channel == 4 || channel == 6)) || (channels == 8 && (channel == 5 || channel == 7));
        Assert(Math.Abs(output[0] - (left ? contribution[channel] : 0)) < 0.00001f
            && Math.Abs(output[1] - (right ? contribution[channel] : 0)) < 0.00001f, $"{channels}-channel input {channel} routed incorrectly");
    }
}

foreach (int count in new[] { 1, 2, 5 })
{
    var queue = new PlayQueue();
    queue.SetList(Enumerable.Range(0, count).Select(i => new Track { Id = i.ToString() }).ToArray(), 0, false);
    for (int cycle = 0; cycle < 100; cycle++)
    {
        while (queue.HasNext) Assert(queue.Advance(true, true), "Advance failed");
        int finished = queue.CurrentQueueIndex;
        Assert(queue.Advance(true, true), "Repeat failed");
        Assert(count == 1 || queue.CurrentQueueIndex != finished, "Repeat immediately replayed the finished track");
        Assert(queue.Order.Order().SequenceEqual(Enumerable.Range(0, count)), "Shuffle lost or duplicated a track");
    }
}

var menu = new MenuService();
var anchor = new object();
int actions = 0;
MenuEntry action = new("Action", Run: () => actions++);
MenuEntry nested = new("Nested", Submenu: () => [action]);
MenuEntry root = new("Root", Submenu: () => [nested]);
menu.Open(anchor, [root]);
menu.Choose(root);
menu.Choose(nested);
menu.Choose(menu.Entries![0]);
Assert(menu.IsOpen && ReferenceEquals(menu.Anchor, anchor) && menu.Entries![2] == nested, "Back failed to restore parent menu");
menu.Choose(menu.Entries![0]);
Assert(menu.IsOpen && menu.Entries![0] == root, "Back failed to restore root menu");
menu.Choose(root);
menu.Choose(nested);
menu.Choose(action);
Assert(!menu.IsOpen && actions == 1, "Ordinary menu action did not close and run");

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var iconText = new IconText();
Assert(iconText.Children.All(x => !x.IsVisible), "Unset icon or text was visible");
iconText.Kind = IconKind.Music;
iconText.Text = "Track";
iconText.Suffix = "Album";
Assert(iconText.Children.All(x => x.IsVisible), "Set icon or text remained hidden");
iconText.Kind = IconKind.None;
iconText.Text = null;
iconText.Suffix = null;
Assert(iconText.Children.All(x => !x.IsVisible), "Cleared icon or text remained visible");

string sourceA = "first";
string sourceB = Enumerable.Range(0, 100).Select(i => i.ToString()).First(s => Formatting.HashIndex(s) != Formatting.HashIndex(sourceA));
var brushA = new SolidColorBrush(Colors.Red);
var brushB = new SolidColorBrush(Colors.Blue);
Application.Current!.Resources[$"Art{Formatting.HashIndex(sourceA)}"] = brushA;
Application.Current.Resources[$"Art{Formatting.HashIndex(sourceB)}"] = brushB;
var art = new ArtView { Source = sourceA };
Assert(ReferenceEquals(art.Background, brushA), "Initial seedless artwork background is incorrect");
art.Source = sourceB;
Assert(ReferenceEquals(art.Background, brushB), "Seedless artwork background did not update");
art.Seed = sourceA;
art.Source = sourceA;
art.Source = sourceB;
Assert(ReferenceEquals(art.Background, brushA), "Artwork source overrode its seed");

var slider = new SliderBar { TrackBrush = Brushes.Gray };
var window = new Window { Width = 200, Height = 50, Content = slider };
window.Show();
window.UpdateLayout();
IPointer? pointer = null;
slider.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Bubble, true);
double lastInput = -1, committed = -1;
int commits = 0;
slider.Input += value => lastInput = value;
slider.Commit += value => { committed = value; commits++; };
window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.None);
window.MouseMove(new Point(120, 25), RawInputModifiers.LeftMouseButton);
Assert(slider.IsDragging && pointer is not null && lastInput > 0, "Slider drag did not start");
pointer!.Capture(null);
Assert(!slider.IsDragging && commits == 1 && slider.Value == lastInput && committed == lastInput, "Capture loss did not commit the drag");
window.MouseUp(new Point(120, 25), MouseButton.Left, RawInputModifiers.None);
Assert(commits == 1, "Capture loss committed twice");
window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.None);
window.MouseUp(new Point(100, 25), MouseButton.Left, RawInputModifiers.None);
Assert(!slider.IsDragging && commits == 2 && slider.Value == committed && Math.Abs(committed - 0.5) < 0.001, "Slider release did not commit exactly once");
window.Close();
Console.WriteLine("Review regression checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

delegate void DownmixDelegate(ReadOnlySpan<float> source, int channels, Span<float> dest, int frames);

sealed class OfflinePipe : INamedPipeClient
{
    public DiscordRPC.Logging.ILogger Logger { get; set; } = new DiscordRPC.Logging.NullLogger();
    public bool IsConnected => false;
    public int ConnectedPipe => -1;
    public bool Connect(int pipe) => false;
    public bool ReadFrame(out PipeFrame frame) { frame = default; return false; }
    public bool WriteFrame(PipeFrame frame) => false;
    public void Close() { }
    public void Dispose() { }
}
