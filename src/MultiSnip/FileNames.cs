using System.Text.RegularExpressions;

namespace MultiSnip;

/// <summary>File-name helpers: the "_yyyy-MM_dd-HH-mm-ss" timestamp suffix and the recent-items lists.</summary>
public static partial class FileNames
{
    public const string DefaultBaseName = "MultiSnip";

    /// <summary>Suffix in the requested format, e.g. "_2026-01_11-18-20-00".</summary>
    public static string TimestampSuffix(DateTime t) => "_" + t.ToString("yyyy-MM_dd-HH-mm-ss");

    [GeneratedRegex(@"_\d{4}-\d{2}_\d{2}-\d{2}-\d{2}-\d{2}$")]
    private static partial Regex SuffixPattern();

    /// <summary>Removes a timestamp suffix added earlier, so re-saving doesn't stack suffixes.</summary>
    public static string StripTimestamp(string baseName) => SuffixPattern().Replace(baseName, "");

    /// <summary>"Report_2026-01_11-18-20-00" from "Report" or "Report_2025-12_01-09-00-00".</summary>
    public static string WithTimestamp(string? baseName, DateTime t)
    {
        string b = string.IsNullOrWhiteSpace(baseName) ? DefaultBaseName : StripTimestamp(baseName.Trim());
        if (b.Length == 0) b = DefaultBaseName;
        return b + TimestampSuffix(t);
    }

    /// <summary>Moves <paramref name="item"/> to the front of a most-recently-used list (case-insensitive, capped).</summary>
    public static void PushRecent(List<string> list, string item, int max = 12)
    {
        list.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, item);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
    }
}
