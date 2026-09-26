using System;
using System.Collections.Generic;
using System.Text;
namespace OSTream.Core.Html;
public sealed class HtmlNode
{
    public const string TextName = "#text";
    public const string CommentName = "#comment";
    public const string DocumentName = "#document";
    List<KeyValuePair<string, string>>? _attributes;
    List<HtmlNode>? _childNodes;
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
        _attributes ??= [];
        foreach (var pair in _attributes)
            if (pair.Key == name) return; 
        _attributes.Add(new(name, value));
    }
    public string? Attr(string name)
    {
        if (_attributes is null) return null;
        foreach (var pair in _attributes)
            if (pair.Key == name) return pair.Value;
        return null;
    }
    public string Id => Attr("id") ?? "";
    public bool HasClass(string className)
    {
        var value = Attr("class").AsSpan();
        foreach (var range in value.SplitAny(" \t\n\r\f"))
            if (value[range].SequenceEqual(className)) return true;
        return false;
    }
    public IEnumerable<HtmlNode> Children
    {
        get
        {
            if (_childNodes is null) yield break;
            foreach (var child in _childNodes)
                if (child.IsElement) yield return child;
        }
    }
    public IEnumerable<HtmlNode> Descendants()
    {
        if (_childNodes is null) yield break;
        var stack = new Stack<(HtmlNode Node, int Index)>();
        stack.Push((this, 0));
        while (stack.Count > 0)
        {
            var (node, index) = stack.Pop();
            var children = node._childNodes!;
            for (var i = index; i < children.Count; i++)
            {
                var child = children[i];
                if (!child.IsElement) continue;
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
        foreach (var node in Descendants())
            if (node.Name == name) yield return node;
    }
    public HtmlNode? First(Func<HtmlNode, bool> match)
    {
        foreach (var node in Descendants())
            if (match(node)) return node;
        return null;
    }
    public HtmlNode? First(string name)
    {
        foreach (var node in Descendants())
            if (node.Name == name) return node;
        return null;
    }
    public HtmlNode? ById(string id) => First(n => n.Id == id);
    public IEnumerable<HtmlNode> Ancestors()
    {
        for (var node = Parent; node is not null; node = node.Parent) yield return node;
    }
    public string TextContent
    {
        get
        {
            if (!IsElement && !ReferenceEquals(Name, DocumentName)) return Data ?? "";
            var sb = new StringBuilder();
            AppendText(sb);
            return sb.ToString();
        }
    }
    void AppendText(StringBuilder sb)
    {
        if (_childNodes is null) return;
        foreach (var child in _childNodes)
        {
            if (child.IsText) sb.Append(child.Data);
            else if (child.IsElement) child.AppendText(sb);
        }
    }
    public string Text => Collapse(TextContent);
    public static string Collapse(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == '﻿')
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;
            sb.Append(c);
        }
        return sb.ToString();
    }
    public override string ToString() => IsElement ? $"<{Name}>" : Data ?? Name;
}