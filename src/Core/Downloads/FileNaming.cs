using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace OSTream.Core.Downloads;

public static partial class FileNaming
{
    private static readonly HashSet<string> WindowsReserved = BuildReserved();
    private static HashSet<string> BuildReserved()
    {
        HashSet<string> set = ["CON", "PRN", "AUX", "NUL", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"];
        for (int i = 1; i <= 9; i++)
        {
            _ = set.Add($"COM{i}");
            _ = set.Add($"LPT{i}");
        }
        return set;
    }
    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1f]")]
    private static partial Regex InvalidChars();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
    private const int MaxNameBytes = 200;
    /// <summary>Creates a file name component by replacing invalid characters and normalizing whitespace.</summary>
    /// <remarks>The result is limited to 200 UTF-8 bytes and avoids reserved Windows device names. If it is empty, the fallback is returned unchanged.</remarks>
    public static string SafeName(string? name, string fallback = "Untitled")
    {
        string s = InvalidChars().Replace(name ?? "", "-");
        s = Whitespace().Replace(s, " ").Trim().TrimStart('.', ' ').TrimEnd('.', ' ');
        if (WindowsReserved.Contains(s.Split('.')[0].TrimEnd().ToUpperInvariant()))
        {
            s = "_" + s;
        }
        s = TakeUtf8Bytes(s, MaxNameBytes).TrimEnd('.', ' ');
        return s.Length > 0 ? s : fallback;
    }
    private static string TakeUtf8Bytes(string s, int maxBytes)
    {
        int bytes = 0;
        StringBuilder sb = new(s.Length);
        foreach (Rune rune in s.EnumerateRunes())
        {
            if ((bytes += rune.Utf8SequenceLength) > maxBytes)
            {
                break;
            }
            _ = sb.Append(rune.ToString());
        }
        return sb.ToString();
    }
    /// <summary>Creates a track file name with an optional disc prefix and extension.</summary>
    /// <param name="pad">Minimum width for a numeric track number; when nonpositive, the width is based on <paramref name="total"/>. Numeric numbers are padded to at least two digits.</param>
    /// <param name="index">Zero-based fallback index used when <paramref name="number"/> is blank.</param>
    /// <param name="ext">Extension appended after the sanitized name, without a leading period.</param>
    public static string TrackFileName(string title, string number, string cd, int pad, int index, int total, string ext)
    {
        string n = number.Trim().TrimEnd('.').Trim();
        if (n.Length == 0)
        {
            n = (index + 1).ToString(CultureInfo.InvariantCulture);
        }
        if (IsDigits(n))
        {
            n = n.PadLeft(Math.Max(2, pad > 0 ? pad : total.ToString(CultureInfo.InvariantCulture).Length), '0');
        }
        string prefix = cd.Length > 0 ? $"{cd}-{n}" : n;
        return SafeName($"{prefix} {title}") + "." + ext;
    }
    private static bool IsDigits(string s)
    {
        foreach (char c in s)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }
        return s.Length > 0;
    }
}