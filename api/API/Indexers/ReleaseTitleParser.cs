using System.Text.RegularExpressions;

namespace API.Indexers;

/// <summary>
/// Decomposition of a torrent/usenet release title into series + issue + year. A pack release
/// ("Invincible 001-144") carries <see cref="IssueRange"/> instead of a single issue.
/// </summary>
public record ParsedRelease(string SeriesTitle, string? IssueNumber, int? Year, (int Start, int End)? IssueRange = null);

/// <summary>
/// Best-effort parser for comic release titles. Indexers return release names like
/// "Saga 060 (2024) (Digital) (Zone-Empire)"; the torrent-backed source needs the series name and
/// issue number out of them. Heuristic and intentionally simple — covers the common cases; the
/// long tail is discovered by use.
/// </summary>
public static class ReleaseTitleParser
{
    private static readonly Regex YearRx = new(@"\((19|20)\d{2}\)", RegexOptions.Compiled);
    private static readonly Regex TagRx = new(@"[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRx = new(@"\s+", RegexOptions.Compiled);

    // Trailing issue token: optional #/vol/v/chapter/ch prefix, optional leading zeros, digits with
    // an optional decimal, anchored to the end of the (tag-stripped) title.
    private static readonly Regex IssueRx = new(
        @"(?:#|vol\.?|v\.?|chapter|ch\.?)?\s*0*(\d{1,5}(?:\.\d+)?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Pack-marker words after the issue range ("The Walking Dead #1-193 Complete") add nothing.
    private static readonly Regex PackTrailerRx = new(
        @"\s*(?:complete|collection|pack)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Volume packs ("Monstress Vol. 1-9") are NOT issue ranges — a volume is ~6 issues, so fanning
    // them out as issues would mislabel the library. Recognised only to be set aside.
    private static readonly Regex VolumeRangeRx = new(
        @"\b(?:vol\.?|v\.?)\s*0*\d{1,5}\s*[-–]\s*0*\d{1,5}\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Issue-range pack: "001-144", "#1-193", "001 - 066", anchored to the end.
    private static readonly Regex IssueRangeRx = new(
        @"(?:#|chapter|ch\.?)?\s*0*(\d{1,5})\s*[-–]\s*#?0*(\d{1,5})\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ParsedRelease Parse(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return new ParsedRelease("", null, null);

        int? year = null;
        Match ym = YearRx.Match(title);
        if (ym.Success)
            year = int.Parse(ym.Value.Trim('(', ')'));

        // Strip all (...) and [...] tag groups, collapse whitespace.
        string cleaned = WhitespaceRx.Replace(TagRx.Replace(title, " "), " ").Trim();

        string working = cleaned;
        while (PackTrailerRx.Match(working) is { Success: true } pm)
            working = working[..pm.Index].Trim();

        Match vm = VolumeRangeRx.Match(working);
        if (vm.Success)
            return new ParsedRelease(SeriesOrFallback(working[..vm.Index], cleaned), null, year);

        Match rm = IssueRangeRx.Match(working);
        if (rm.Success)
        {
            int start = int.Parse(rm.Groups[1].Value);
            int end = int.Parse(rm.Groups[2].Value);
            if (start < end)
                return new ParsedRelease(SeriesOrFallback(working[..rm.Index], cleaned), null, year, (start, end));
        }

        string? issue = null;
        string seriesTitle = cleaned;
        Match im = IssueRx.Match(working);
        if (im.Success && im.Groups[1].Success)
        {
            issue = NormalizeIssue(im.Groups[1].Value);
            seriesTitle = SeriesOrFallback(working[..im.Index], cleaned);
        }

        return new ParsedRelease(seriesTitle, issue, year);
    }

    /// <summary>Never return an empty series — fall back to the whole cleaned title.</summary>
    private static string SeriesOrFallback(string prefix, string cleaned)
    {
        string series = prefix.Trim().TrimEnd('-', ':', '–').Trim();
        return string.IsNullOrWhiteSpace(series) ? cleaned : series;
    }

    private static string NormalizeIssue(string raw)
    {
        // "060" -> "60", "60.5" -> "60.5", "100" -> "100". Matches Chapter's chapter-number contract.
        if (raw.Contains('.'))
        {
            string[] parts = raw.Split('.');
            return $"{int.Parse(parts[0])}.{parts[1]}";
        }
        return int.Parse(raw).ToString();
    }
}
