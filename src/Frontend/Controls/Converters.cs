using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
namespace OSTream.Frontend.Controls;

public static class Converters
{
    public static readonly IValueConverter Equal = new FuncValueConverter<object?, object?, bool>((value, parameter) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal));
    public static readonly IValueConverter Percent = new FuncValueConverter<double, double>(v => Math.Clamp(v / 100, 0, 1));
    public static readonly IValueConverter LineHeight = new FuncValueConverter<double, object?, double>((size, factor) =>
        Math.Round(size * double.Parse(factor?.ToString() ?? "1.5", CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero));
    public static readonly IValueConverter BreakAll = new FuncValueConverter<string?, string>(BreakEverywhere);
    public static string BreakEverywhere(string? text)
    {
        List<string> parts = [];
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text ?? "");
        while (elements.MoveNext())
        {
            parts.Add(elements.GetTextElement());
        }

        return string.Join('\u200B', parts);
    }
}