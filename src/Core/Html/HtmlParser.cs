using System;
using System.Collections.Generic;
using System.Net;
namespace OSTream.Core.Html;
public static class HtmlParser
{
    static readonly HashSet<string> VoidElements =
        ["area", "base", "br", "col", "embed", "hr", "img", "input", "keygen", "link", "meta", "param", "source", "track", "wbr"];
    static readonly HashSet<string> RawTextElements = ["script", "style", "textarea", "title", "xmp", "noscript", "iframe", "noembed", "noframes"];
    static readonly HashSet<string> StructuralElements = ["html", "head", "body"];
    static readonly HashSet<string> ClosesParagraph =
    [
        "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset",
        "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "li",
        "dd", "dt", "listing", "main", "menu", "nav", "ol", "p", "pre", "section", "summary", "ul", "plaintext",
    ];
    static readonly HashSet<string> ScopeBoundaries = ["applet", "caption", "html", "table", "td", "th", "marquee", "object", "template"];
    static readonly HashSet<string> TableSections = ["tbody", "thead", "tfoot"];
    public static HtmlNode Parse(string html)
    {
        if (html.Length > 16_000_000) throw new ArgumentException("HTML input exceeds maximum length.");
        var builder = new TreeBuilder();
        var i = 0;
        var n = html.Length;
        while (i < n)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                builder.Text(html, i, n - i);
                break;
            }
            if (lt > i) builder.Text(html, i, lt - i);
            i = lt;
            if (i + 1 >= n)
            {
                builder.Text(html, i, n - i);
                break;
            }
            var next = html[i + 1];
            if (next == '!')
            {
                if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
                {
                    var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    var stop = end < 0 ? n : end;
                    builder.Comment(html.Substring(i + 4, stop - (i + 4)));
                    i = end < 0 ? n : end + 3;
                }
                else
                {
                    var end = html.IndexOf('>', i + 2);
                    var stop = end < 0 ? n : end;
                    if (string.Compare(html, i + 2, "doctype", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
                        builder.Doctype(html.AsSpan(i + 9, Math.Max(0, stop - (i + 9))));
                    i = end < 0 ? n : end + 1;
                }
            }
            else if (next == '?')
            {
                var end = html.IndexOf('>', i + 2);
                i = end < 0 ? n : end + 1;
            }
            else if (next == '/')
            {
                if (i + 2 < n && char.IsAsciiLetter(html[i + 2]))
                {
                    var nameEnd = ScanName(html, i + 2);
                    var name = html.Substring(i + 2, nameEnd - (i + 2)).ToLowerInvariant();
                    var end = html.IndexOf('>', nameEnd);
                    i = end < 0 ? n : end + 1;
                    builder.EndTag(name);
                }
                else
                {
                    var end = html.IndexOf('>', i + 2);
                    i = end < 0 ? n : end + 1;
                }
            }
            else if (char.IsAsciiLetter(next))
            {
                i = ParseStartTag(html, i, builder);
            }
            else
            {
                builder.Text(html, i, 1);
                i++;
            }
        }
        return builder.Document;
    }
    static int ScanName(string html, int i)
    {
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '/' && html[i] != '>') i++;
        return i;
    }
    static int ParseStartTag(string html, int start, TreeBuilder builder)
    {
        var n = html.Length;
        var nameEnd = ScanName(html, start + 1);
        var name = html.Substring(start + 1, nameEnd - start - 1).ToLowerInvariant();
        var element = new HtmlNode(name);
        var i = nameEnd;
        var selfClosing = false;
        while (i < n)
        {
            while (i < n && (char.IsWhiteSpace(html[i]) || html[i] == '/'))
            {
                if (html[i] == '/' && i + 1 < n && html[i + 1] == '>') selfClosing = true;
                i++;
            }
            if (i >= n || html[i] == '>') break;
            var attrStart = i;
            while (i < n && !char.IsWhiteSpace(html[i]) && html[i] != '/' && html[i] != '>' && (html[i] != '=' || i == attrStart)) i++;
            var attrName = html.Substring(attrStart, i - attrStart).ToLowerInvariant();
            while (i < n && char.IsWhiteSpace(html[i])) i++;
            var value = "";
            if (i < n && html[i] == '=')
            {
                i++;
                while (i < n && char.IsWhiteSpace(html[i])) i++;
                if (i < n && (html[i] == '"' || html[i] == '\''))
                {
                    var quote = html[i];
                    var close = html.IndexOf(quote, i + 1);
                    var stop = close < 0 ? n : close;
                    value = WebUtility.HtmlDecode(html.Substring(i + 1, stop - i - 1));
                    i = close < 0 ? n : close + 1;
                }
                else
                {
                    var valueStart = i;
                    while (i < n && !char.IsWhiteSpace(html[i]) && html[i] != '>') i++;
                    value = WebUtility.HtmlDecode(html.Substring(valueStart, i - valueStart));
                }
            }
            element.SetAttribute(attrName, value);
        }
        i = i < n ? i + 1 : n; 
        builder.StartTag(element, selfClosing);
        if (RawTextElements.Contains(name) && !VoidElements.Contains(name))
        {
            var close = IndexOfEndTag(html, i, name);
            var stop = close < 0 ? n : close;
            if (stop > i)
            {
                var raw = html.Substring(i, stop - i);
                builder.RawText(name is "textarea" or "title" ? WebUtility.HtmlDecode(raw) : raw);
            }
            builder.EndTag(name);
            if (close < 0) return n;
            var gt = html.IndexOf('>', close);
            return gt < 0 ? n : gt + 1;
        }
        return i;
    }
    static int IndexOfEndTag(string html, int from, string name)
    {
        var i = from;
        while (true)
        {
            i = html.IndexOf("</", i, StringComparison.Ordinal);
            if (i < 0) return -1;
            var after = i + 2 + name.Length;
            if (string.Compare(html, i + 2, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (after >= html.Length || char.IsWhiteSpace(html[after]) || html[after] == '>' || html[after] == '/'))
                return i;
            i += 2;
        }
    }
    sealed class TreeBuilder
    {
        readonly List<HtmlNode> _open = [];
        bool _quirks = true;
        public HtmlNode Document { get; } = new(HtmlNode.DocumentName);
        HtmlNode Current => _open.Count > 0 ? _open[^1] : Document;
        bool InForeignContent
        {
            get
            {
                foreach (var node in _open)
                    if (node.Name is "svg" or "math") return true;
                return false;
            }
        }
        public void Doctype(ReadOnlySpan<char> content)
        {
            var s = content.Trim().ToString().ToLowerInvariant();
            if (!s.StartsWith("html", StringComparison.Ordinal)) _quirks = true;
            else if (!s.Contains("public")) _quirks = false;
            else if (s.Contains("xhtml")) _quirks = false;
            else _quirks = !(s.Contains("html 4.01") && s.Contains("http"));
        }
        public void Text(string html, int start, int length)
        {
            var text = WebUtility.HtmlDecode(html.Substring(start, length));
            if (text.Length > 0) Current.Append(new HtmlNode(HtmlNode.TextName, text));
        }
        public void RawText(string text) => Current.Append(new HtmlNode(HtmlNode.TextName, text));
        public void Comment(string text) => Current.Append(new HtmlNode(HtmlNode.CommentName, text));
        public void StartTag(HtmlNode element, bool selfClosing)
        {
            if (_open.Count > 200) return;
            var name = element.Name;
            if (StructuralElements.Contains(name)) return;
            if (!InForeignContent) CloseImplied(name);
            Current.Append(element);
            if (VoidElements.Contains(name) || (selfClosing && InForeignContentOr(name))) return;
            _open.Add(element);
        }
        bool InForeignContentOr(string name) => name is "svg" or "math" || InForeignContent;
        void CloseImplied(string name)
        {
            if (ClosesParagraph.Contains(name) || (name == "table" && !_quirks)) CloseInScope("p", ScopeBoundaries, alsoStopAt: "button");
            switch (name)
            {
                case "li":
                    CloseInScope("li", ScopeBoundaries, alsoStopAt: "ol", alsoStopAt2: "ul");
                    break;
                case "dd" or "dt":
                    CloseInScope("dd", ScopeBoundaries);
                    CloseInScope("dt", ScopeBoundaries);
                    break;
                case "td" or "th":
                    CloseInTable("td", stopAt: "tr");
                    CloseInTable("th", stopAt: "tr");
                    break;
                case "tr":
                    CloseInTable("td", stopAt: "tr");
                    CloseInTable("th", stopAt: "tr");
                    CloseInTable("tr", stopAt: null);
                    break;
                case "tbody" or "thead" or "tfoot":
                    CloseInTable("td", stopAt: "tr");
                    CloseInTable("th", stopAt: "tr");
                    CloseInTable("tr", stopAt: null);
                    foreach (var section in TableSections) CloseInTable(section, stopAt: null);
                    break;
                case "option":
                    if (Current.Name == "option") _open.RemoveAt(_open.Count - 1);
                    break;
                case "optgroup":
                    if (Current.Name == "option") _open.RemoveAt(_open.Count - 1);
                    if (Current.Name == "optgroup") _open.RemoveAt(_open.Count - 1);
                    break;
                case "a":
                    CloseInScope("a", ScopeBoundaries);
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    if (Current.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6") _open.RemoveAt(_open.Count - 1);
                    break;
            }
        }
        void CloseInScope(string name, HashSet<string> boundaries, string? alsoStopAt = null, string? alsoStopAt2 = null)
        {
            for (var i = _open.Count - 1; i >= 0; i--)
            {
                var open = _open[i].Name;
                if (open == name)
                {
                    _open.RemoveRange(i, _open.Count - i);
                    return;
                }
                if (boundaries.Contains(open) || open == alsoStopAt || open == alsoStopAt2) return;
            }
        }
        void CloseInTable(string name, string? stopAt)
        {
            for (var i = _open.Count - 1; i >= 0; i--)
            {
                var open = _open[i].Name;
                if (open == name)
                {
                    _open.RemoveRange(i, _open.Count - i);
                    return;
                }
                if (open == "table" || open == stopAt || open == "template") return;
            }
        }
        public void EndTag(string name)
        {
            if (StructuralElements.Contains(name)) return;
            if (name == "br")
            {
                Current.Append(new HtmlNode("br"));
                return;
            }
            if (VoidElements.Contains(name)) return;
            var tableBound = name is "td" or "th" or "tr" or "tbody" or "thead" or "tfoot" or "caption" or "table";
            for (var i = _open.Count - 1; i >= 0; i--)
            {
                var open = _open[i].Name;
                if (open == name)
                {
                    _open.RemoveRange(i, _open.Count - i);
                    return;
                }
                if (tableBound ? open == "table" && name != "table" : ScopeBoundaries.Contains(open)) return;
            }
        }
    }
}