using System;
using System.Collections.Generic;
using System.Linq;
using OSTream.Core;
namespace OSTream.Navigation;
public sealed record Route(IReadOnlyList<string> Segments, IReadOnlyDictionary<string, string> Params)
{
    public static readonly Route Home = new([], new Dictionary<string, string>());
    public string Name => Segments.Count > 0 ? Segments[0] : "discover";
    public string? Segment(int index) => index < Segments.Count ? Segments[index] : null;
    public string? Param(string key) => Params.TryGetValue(key, out var value) ? value : null;
    public static Route Parse(string location)
    {
        var raw = location.TrimStart('#');
        if (raw.Length == 0) raw = "/";
        var q = raw.IndexOf('?');
        var path = q < 0 ? raw : raw[..q];
        var query = q < 0 ? "" : raw[(q + 1)..];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Unescape).ToArray();
        var parameters = new Dictionary<string, string>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Unescape(eq < 0 ? pair : pair[..eq]);
            if (!parameters.ContainsKey(key)) parameters[key] = eq < 0 ? "" : Unescape(pair[(eq + 1)..]);
        }
        return new Route(segments, parameters);
    }
    static string Unescape(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s.Replace('+', ' '); }
    }
    public static string Build(string path, IEnumerable<KeyValuePair<string, string?>>? parameters = null)
    {
        var query = string.Join("&", (parameters ?? [])
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{Formatting.EncodeUriComponent(p.Key)}={Formatting.EncodeUriComponent(p.Value!)}"));
        return query.Length > 0 ? $"{path}?{query}" : path;
    }
    public string PathString => "/" + string.Join("/", Segments.Select(Formatting.EncodeUriComponent));
    public override string ToString() => Build(PathString, Params.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
}
public sealed class Navigator
{
    readonly List<string> _history = ["/"];
    int _index;
    public Route Current { get; private set; } = Route.Home;
    public event Action<Route>? Changed;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index < _history.Count - 1;
    public void Navigate(string path, IEnumerable<KeyValuePair<string, string?>>? parameters = null, bool replace = false) =>
        Go(Route.Build(path, parameters), replace);
    public void Navigate(string path, string key, string? value, bool replace = false) =>
        Navigate(path, [new KeyValuePair<string, string?>(key, value)], replace);
    public void SetParams(IEnumerable<KeyValuePair<string, string?>> patch, bool replace = false)
    {
        var merged = Current.Params.ToDictionary(p => p.Key, p => (string?)p.Value);
        foreach (var (key, value) in patch) merged[key] = value;
        Navigate(Current.PathString, merged, replace);
    }
    public void SetParam(string key, string? value, bool replace = false) => SetParams([new(key, value)], replace);
    public string With(string key, string? value)
    {
        var merged = Current.Params.ToDictionary(p => p.Key, p => (string?)p.Value);
        merged[key] = value;
        return Route.Build(Current.PathString, merged);
    }
    void Go(string location, bool replace)
    {
        var route = Route.Parse(location);
        var canonical = route.ToString();
        if (canonical != Current.ToString())
        {
            if (replace)
            {
                _history[_index] = canonical;
            }
            else
            {
                _history.RemoveRange(_index + 1, _history.Count - _index - 1);
                _history.Add(canonical);
                _index++;
            }
        }
        Current = route;
        Changed?.Invoke(route);
    }
    public void Open(string location) => Go(location, replace: false);
    public void Back()
    {
        if (!CanGoBack) return;
        Current = Route.Parse(_history[--_index]);
        Changed?.Invoke(Current);
    }
    public void Forward()
    {
        if (!CanGoForward) return;
        Current = Route.Parse(_history[++_index]);
        Changed?.Invoke(Current);
    }
    public void Start() => Changed?.Invoke(Current);
}