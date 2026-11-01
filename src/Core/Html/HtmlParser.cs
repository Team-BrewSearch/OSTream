using System;
using System.Collections.Generic;
using System.Net;
namespace OSTream.Core.Html;

public static class HtmlParser
{
    private static readonly HashSet<string> VoidElements =
        ["area", "base", "br", "col", "embed", "hr", "img", "input", "keygen", "link", "meta", "param", "source", "track", "wbr"];
    private static readonly HashSet<string> RawTextElements = ["script", "style", "textarea", "title", "xmp", "noscript", "iframe", "noembed", "noframes"];
    private static readonly HashSet<string> StructuralElements = ["html", "head", "body"];
    private static readonly HashSet<string> ClosesParagraph =
    [
        "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset",
        "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "li",
        "dd", "dt", "listing", "main", "menu", "nav", "ol", "p", "pre", "section", "summary", "ul", "plaintext",
    ];
    private static readonly HashSet<string> ScopeBoundaries = ["applet", "caption", "html", "table", "td", "th", "marquee", "object", "template"];
    private static readonly HashSet<string> TableSections = ["tbody", "thead", "tfoot"];
    public static HtmlNode Parse(string html)
    {
        if (html.Length > 16_000_000)
        {
            throw new ArgumentException("HTML input exceeds maximum length.");
        }

        TreeBuilder builder = new();
        int i = 0;
        int n = html.Length;
        while (i < n)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                builder.Text(html, i, n - i);
                break;
            }
            if (lt > i)
            {
                builder.Text(html, i, lt - i);
            }

            i = lt;
            if (i + 1 >= n)
            {
                builder.Text(html, i, n - i);
                break;
            }
            char next = html[i + 1];
            if (next == '!')
            {
                if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
                {
                    int end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    int stop = end < 0 ? n : end;
                    builder.Comment(html[(i + 4)..stop]);
                    i = end < 0 ? n : end + 3;
                }
                else
                {
                    int end = html.IndexOf('>', i + 2);
                    int stop = end < 0 ? n : end;
                    if (string.Compare(html, i + 2, "doctype", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        builder.Doctype(html.AsSpan(i + 9, Math.Max(0, stop - (i + 9))));
                    }

                    i = end < 0 ? n : end + 1;
                }
            }
            else if (next == '?')
            {
                int end = html.IndexOf('>', i + 2);
                i = end < 0 ? n : end + 1;
            }
            else if (next == '/')
            {
                if (i + 2 < n && char.IsAsciiLetter(html[i + 2]))
                {
                    int nameEnd = ScanName(html, i + 2);
                    string name = html[(i + 2)..nameEnd].ToLowerInvariant();
                    int end = html.IndexOf('>', nameEnd);
                    i = end < 0 ? n : end + 1;
                    builder.EndTag(name);
                }
                else
                {
                    int end = html.IndexOf('>', i + 2);
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
    private static int ScanName(string html, int i)
    {
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '/' && html[i] != '>')
        {
            i++;
        }

        return i;
    }
    private static int ParseStartTag(string html, int start, TreeBuilder builder)
    {
        int n = html.Length;
        int nameEnd = ScanName(html, start + 1);
        string name = html.Substring(start + 1, nameEnd - start - 1).ToLowerInvariant();
        HtmlNode element = new(name);
        int i = nameEnd;
        bool selfClosing = false;
        while (i < n)
        {
            while (i < n && (char.IsWhiteSpace(html[i]) || html[i] == '/'))
            {
                if (html[i] == '/' && i + 1 < n && html[i + 1] == '>')
                {
                    selfClosing = true;
                }

                i++;
            }
            if (i >= n || html[i] == '>')
            {
                break;
            }

            int attrStart = i;
            while (i < n && !char.IsWhiteSpace(html[i]) && html[i] != '/' && html[i] != '>' && (html[i] != '=' || i == attrStart))
            {
                i++;
            }

            string attrName = html[attrStart..i].ToLowerInvariant();
            while (i < n && char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            string value = "";
            if (i < n && html[i] == '=')
            {
                i++;
                while (i < n && char.IsWhiteSpace(html[i]))
                {
                    i++;
                }

                if (i < n && (html[i] == '"' || html[i] == '\''))
                {
                    char quote = html[i];
                    int close = html.IndexOf(quote, i + 1);
                    int stop = close < 0 ? n : close;
                    value = WebUtility.HtmlDecode(html.Substring(i + 1, stop - i - 1));
                    i = close < 0 ? n : close + 1;
                }
                else
                {
                    int valueStart = i;
                    while (i < n && !char.IsWhiteSpace(html[i]) && html[i] != '>')
                    {
                        i++;
                    }

                    value = WebUtility.HtmlDecode(html[valueStart..i]);
                }
            }
            element.SetAttribute(attrName, value);
        }
        i = i < n ? i + 1 : n;
        builder.StartTag(element, selfClosing);
        if (RawTextElements.Contains(name) && !VoidElements.Contains(name))
        {
            int close = IndexOfEndTag(html, i, name);
            int stop = close < 0 ? n : close;
            if (stop > i)
            {
                string raw = html[i..stop];
                builder.RawText(name is "textarea" or "title" ? WebUtility.HtmlDecode(raw) : raw);
            }
            builder.EndTag(name);
            if (close < 0)
            {
                return n;
            }

            int gt = html.IndexOf('>', close);
            return gt < 0 ? n : gt + 1;
        }
        return i;
    }
    private static int IndexOfEndTag(string html, int from, string name)
    {
        int i = from;
        while (true)
        {
            i = html.IndexOf("</", i, StringComparison.Ordinal);
            if (i < 0)
            {
                return -1;
            }

            int after = i + 2 + name.Length;
            if (string.Compare(html, i + 2, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (after >= html.Length || char.IsWhiteSpace(html[after]) || html[after] == '>' || html[after] == '/'))
            {
                return i;
            }

            i += 2;
        }
    }
    private sealed class TreeBuilder
    {
        private readonly List<HtmlNode> _open = [];
        private bool _quirks = true;
        public HtmlNode Document { get; } = new(HtmlNode.DocumentName);
        private HtmlNode Current => _open.Count > 0 ? _open[^1] : Document;
        private bool InForeignContent
        {
            get
            {
                foreach (HtmlNode node in _open)
                {
                    if (node.Name is "svg" or "math")
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        public void Doctype(ReadOnlySpan<char> content)
        {
            string s = content.Trim().ToString().ToLowerInvariant();
            _quirks = !s.StartsWith("html", StringComparison.Ordinal) || (s.Contains("public") && !s.Contains("xhtml") && !(s.Contains("html 4.01") && s.Contains("http")));
        }
        public void Text(string html, int start, int length)
        {
            string text = WebUtility.HtmlDecode(html.Substring(start, length));
            if (text.Length > 0)
            {
                Current.Append(new HtmlNode(HtmlNode.TextName, text));
            }
        }
        public void RawText(string text)
        {
            Current.Append(new HtmlNode(HtmlNode.TextName, text));
        }

        public void Comment(string text)
        {
            Current.Append(new HtmlNode(HtmlNode.CommentName, text));
        }

        public void StartTag(HtmlNode element, bool selfClosing)
        {
            if (_open.Count > 200)
            {
                return;
            }

            string name = element.Name;
            if (StructuralElements.Contains(name))
            {
                return;
            }

            if (!InForeignContent)
            {
                CloseImplied(name);
            }

            Current.Append(element);
            if (VoidElements.Contains(name) || (selfClosing && InForeignContentOr(name)))
            {
                return;
            }

            _open.Add(element);
        }
        private bool InForeignContentOr(string name)
        {
            return name is "svg" or "math" || InForeignContent;
        }

        private void CloseImplied(string name)
        {
            if (ClosesParagraph.Contains(name) || (name == "table" && !_quirks))
            {
                CloseInScope("p", ScopeBoundaries, alsoStopAt: "button");
            }

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
                    foreach (string section in TableSections)
                    {
                        CloseInTable(section, stopAt: null);
                    }

                    break;
                case "option":
                    if (Current.Name == "option")
                    {
                        _open.RemoveAt(_open.Count - 1);
                    }

                    break;
                case "optgroup":
                    if (Current.Name == "option")
                    {
                        _open.RemoveAt(_open.Count - 1);
                    }

                    if (Current.Name == "optgroup")
                    {
                        _open.RemoveAt(_open.Count - 1);
                    }

                    break;
                case "a":
                    CloseInScope("a", ScopeBoundaries);
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    if (Current.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                    {
                        _open.RemoveAt(_open.Count - 1);
                    }

                    break;
            }
        }
        private void CloseInScope(string name, HashSet<string> boundaries, string? alsoStopAt = null, string? alsoStopAt2 = null)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                string open = _open[i].Name;
                if (open == name)
                {
                    _open.RemoveRange(i, _open.Count - i);
                    return;
                }
                if (boundaries.Contains(open) || open == alsoStopAt || open == alsoStopAt2)
                {
                    return;
                }
            }
        }
        private void CloseInTable(string name, string? stopAt)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                string open = _open[i].Name;
                if (open == name)
                {
                    _open.RemoveRange(i, _open.Count - i);
                    return;
                }
                if (open == "table" || open == stopAt || open == "template")
                {
                    return;
                }
            }
        }
        public void EndTag(string name)
        {
            if (StructuralElements.Contains(name))
            {
                return;
            }

            if (name == "br")
            {
                Current.Append(new HtmlNode("br"));
                return;
            }
            if (VoidElements.Contains(name))
            {
                return;
            }

            bool tableBound = name is "td" or "th" or "tr" or "tbody" or "thead" or "tfoot" or "caption" or "table";
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                string open = _open[i].Name;
                if (open == name)
                {
                    _open.RemoveRange(i, _open.Count - i);
                    return;
                }
                if (tableBound ? open == "table" && name != "table" : ScopeBoundaries.Contains(open))
                {
                    return;
                }
            }
        }
    }
}