using System;

namespace DesktopAssist.Util;

public static class JsonExtraction
{
    public static string? ExtractJsonBlock(string input)
    {
        int first = input.IndexOf('{');
        int last = input.LastIndexOf('}');
        if (first < 0 || last < first) return null;
        return input.Substring(first, last - first + 1);
    }
}
