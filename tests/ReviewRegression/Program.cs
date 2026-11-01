using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using OSTream.Core;
using OSTream.Core.Audio;
using OSTream.Core.Models;
using OSTream.Core.Player;
using OSTream.Frontend.Controls;
using OSTream.Services;

var downmix = typeof(AudioEngine).Assembly.GetType("OSTream.Core.Audio.Downmix")!
    .GetMethod("ToStereo")!.CreateDelegate<DownmixDelegate>();
const float c = 0.7071f * 0.5f;
// WAV order: FL, FR, FC, LFE, then rear/side channels (and back center in 6.1).
CheckChannels(6, [0.5f, 0.5f, c, 0, c, c]);
CheckChannels(7, [0.5f, 0.5f, c, 0, c, c, c]);
CheckChannels(8, [0.5f, 0.5f, c, 0, c, c, c, c]);

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
