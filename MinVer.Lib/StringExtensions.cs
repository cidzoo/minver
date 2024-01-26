using System;

namespace MinVer.Lib;

public static class StringExtensions
{
    public static string RemoveFromEnd(this string text, string value) =>
        text.EndsWith(value, StringComparison.OrdinalIgnoreCase) ? text[..^value.Length] : text;
}
