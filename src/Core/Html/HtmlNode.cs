using System;
using System.Collections.Generic;
using System.Text;
namespace OSTream.Core.Html;

public sealed class HtmlNode
{
    public const string TextName = "#text";
    public const string CommentName = "#comment";
    public const string DocumentName = "#document";
    private Dictionary<string, string>? _attributes;
    private List<HtmlNode>? _childNodes;
    internal HtmlNode(string name, string? data = null)
    {
        Name = name;
        Data = data;
    }
    public string Name { get; }
    public string? Data { get; }
    public HtmlNode? Parent { get; private set; }
    public bool IsElement => Name[0] != '#';
    public bool IsText => ReferenceEquals(Name, TextName);
    public IReadOnlyList<HtmlNode> ChildNodes => (IReadOnlyList<HtmlNode>?)_childNodes ?? [];
    internal void Append(HtmlNode child)
    {
        child.Parent = this;
        (_childNodes ??= []).Add(child);
    }
    internal void SetAttribute(string name, string value)
    {
        _ = (_attributes ??= new(StringComparer.Ordinal)).TryAdd(name, value);
    }
    public string? Attr(string name)
    {
        return name is not null && _attributes is not null && _attributes.TryGetValue(name, out string? value) ? value : null;
    }
    public string Id => Attr("id") ?? "";
    public bool HasClass(string className)
    {
        ReadOnlySpan<char> value = Attr("class").AsSpan();
        foreach (Range range in value.SplitAny(" \t\n\r\f"))
        {
            if (value[range].SequenceEqual(className))
            {
                return true;
            }
        }
        return false;
    }
    public IEnumerable<HtmlNode> Children
    {
        get
        {
            if (_childNodes is null)
            {
                yield break;
            }
            foreach (HtmlNode child in _childNodes)
            {
                if (child.IsElement)
                {
                    yield return child;
                }
            }
        }
    }
    public IEnumerable<HtmlNode> Descendants()
    {
        if (_childNodes is null)
        {
            yield break;
        }
        Stack<(HtmlNode Node, int Index)> stack = new();
        stack.Push((this, 0));
        while (stack.Count > 0)
        {
            (HtmlNode? node, int index) = stack.Pop();
            List<HtmlNode> children = node._childNodes!;
            for (int i = index; i < children.Count; i++)
            {
                HtmlNode child = children[i];
                if (!child.IsElement)
                {
                    continue;
                }
                yield return child;
                if (child._childNodes is { Count: > 0 })
                {
                    stack.Push((node, i + 1));
                    stack.Push((child, 0));
                    break;
                }
            }
        }
    }
    public IEnumerable<HtmlNode> Descendants(string name)
    {
        foreach (HtmlNode node in Descendants())
        {
            if (node.Name == name)
            {
                yield return node;
            }
        }
    }
    public HtmlNode? First(Func<HtmlNode, bool> match)
    {
        foreach (HtmlNode node in Descendants())
        {
            if (match(node))
            {
                return node;
            }
        }
        return null;
    }
    public HtmlNode? First(string name)
    {
        foreach (HtmlNode node in Descendants())
        {
            if (node.Name == name)
            {
                return node;
            }
        }
        return null;
    }
    public HtmlNode? ById(string id)
    {
        return First(n => n.Id == id);
    }
    public IEnumerable<HtmlNode> Ancestors()
    {
        for (HtmlNode? node = Parent; node is not null; node = node.Parent)
        {
            yield return node;
        }
    }
    public string TextContent
    {
        get
        {
            if (!IsElement && !ReferenceEquals(Name, DocumentName))
            {
                return Data ?? "";
            }
            StringBuilder sb = new();
            AppendText(sb);
            return sb.ToString();
        }
    }
    private void AppendText(StringBuilder sb)
    {
        if (_childNodes is null)
        {
            return;
        }
        foreach (HtmlNode child in _childNodes)
        {
            if (child.IsText)
            {
                _ = sb.Append(child.Data);
            }
            else if (child.IsElement)
            {
                child.AppendText(sb);
            }
        }
    }
    public string Text => Collapse(TextContent);
    public static string Collapse(string text)
    {
        StringBuilder sb = new(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || c == '﻿')
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                _ = sb.Append(' ');
            }
            pendingSpace = false;
            _ = sb.Append(c);
        }
        return sb.ToString();
    }
    public override string ToString()
    {
        return IsElement ? $"<{Name}>" : Data ?? Name;
    }
}
