using OSTream.Core;
using System;
using System.Collections.Generic;
using System.Linq;
namespace OSTream.Frontend.Navigation;

public sealed record Route(IReadOnlyList<string> Segments, IReadOnlyDictionary<string, string> Params)
{
    public static readonly Route Home = new([], new Dictionary<string, string>());
    public string Name => Segments.Count > 0 ? Segments[0] : "discover";
    public string? Segment(int index)
    {
        return index < Segments.Count ? Segments[index] : null;
    }

    public string? Param(string key)
    {
        return Params.TryGetValue(key, out string? value) ? value : null;
    }

    public static Route Parse(string location)
    {
        string raw = location.TrimStart('#');
        if (raw.Length == 0)
        {
            raw = "/";
        }

        int q = raw.IndexOf('?');
        string path = q < 0 ? raw : raw[..q];
        string query = q < 0 ? "" : raw[(q + 1)..];
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Unescape).ToArray();
        Dictionary<string, string> parameters = [];
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = Unescape(eq < 0 ? pair : pair[..eq]);
            if (!parameters.ContainsKey(key))
            {
                parameters[key] = eq < 0 ? "" : Unescape(pair[(eq + 1)..]);
            }
        }
        return new Route(segments, parameters);
    }
    private static string Unescape(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s.Replace('+', ' '); }
    }
    public static string Build(string path, IEnumerable<KeyValuePair<string, string?>>? parameters = null)
    {
        string query = string.Join("&", (parameters ?? [])
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{Formatting.EncodeUriComponent(p.Key)}={Formatting.EncodeUriComponent(p.Value!)}"));
        return query.Length > 0 ? $"{path}?{query}" : path;
    }
    public string PathString => "/" + string.Join("/", Segments.Select(Formatting.EncodeUriComponent));
    public override string ToString()
    {
        return Build(PathString, Params.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
    }
}
public sealed class Navigator
{
    private readonly List<string> _history = ["/"];
    private int _index;
    public Route Current { get; private set; } = Route.Home;
    public event Action<Route>? Changed;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index < _history.Count - 1;
    public void Navigate(string path, IEnumerable<KeyValuePair<string, string?>>? parameters = null, bool replace = false)
    {
        Go(Route.Build(path, parameters), replace);
    }

    public void Navigate(string path, string key, string? value, bool replace = false)
    {
        Navigate(path, [new KeyValuePair<string, string?>(key, value)], replace);
    }

    public void SetParams(IEnumerable<KeyValuePair<string, string?>> patch, bool replace = false)
    {
        Dictionary<string, string?> merged = Current.Params.ToDictionary(p => p.Key, p => (string?)p.Value);
        foreach ((string? key, string? value) in patch)
        {
            merged[key] = value;
        }

        Navigate(Current.PathString, merged, replace);
    }
    public void SetParam(string key, string? value, bool replace = false)
    {
        SetParams([new(key, value)], replace);
    }

    public string With(string key, string? value)
    {
        Dictionary<string, string?> merged = Current.Params.ToDictionary(p => p.Key, p => (string?)p.Value);
        merged[key] = value;
        return Route.Build(Current.PathString, merged);
    }
    private void Go(string location, bool replace)
    {
        Route route = Route.Parse(location);
        string canonical = route.ToString();
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
    public void Open(string location)
    {
        Go(location, replace: false);
    }

    public void Back()
    {
        if (!CanGoBack)
        {
            return;
        }

        Current = Route.Parse(_history[--_index]);
        Changed?.Invoke(Current);
    }
    public void Forward()
    {
        if (!CanGoForward)
        {
            return;
        }

        Current = Route.Parse(_history[++_index]);
        Changed?.Invoke(Current);
    }
    public void Start()
    {
        Changed?.Invoke(Current);
    }
}