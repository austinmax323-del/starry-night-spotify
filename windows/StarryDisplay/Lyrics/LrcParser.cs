using System.Globalization;
using System.Text.RegularExpressions;

namespace StarryDisplay.Lyrics;

/// <summary>
/// Parses LRC text into timed lines. A line may carry several timestamps (a repeated chorus);
/// each becomes its own entry. Supports [mm:ss], [mm:ss.x], [mm:ss.xx], [mm:ss.xxx], [mm:ss:xx]
/// and the [offset:±ms] header.
/// Adapted from The-MuffinDev/LyricsOnTheGo LrcParser.cs (MIT, © 2026 Luis Anchondo).
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^\s*\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetRegex();

    public static List<LyricLine> Parse(string? lrc)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrEmpty(lrc)) return lines;

        var offset = 0;
        foreach (var raw in lrc.Split('\n'))
        {
            var offsetMatch = OffsetRegex().Match(raw);
            if (offsetMatch.Success)
            {
                // Positive offset = lyrics appear sooner.
                offset = int.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            var matches = TagRegex().Matches(raw);
            if (matches.Count == 0) continue;

            var text = TagRegex().Replace(raw, "").Trim();
            foreach (Match m in matches)
            {
                var min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                var frac = 0.0;
                if (m.Groups[3].Success)
                {
                    var f = m.Groups[3].Value;
                    frac = double.Parse(f, CultureInfo.InvariantCulture) / Math.Pow(10, f.Length);
                }
                var ms = (int)Math.Round((min * 60 + sec + frac) * 1000.0);
                lines.Add(new LyricLine(Math.Max(0, ms - offset), text));
            }
        }

        // Stable sort keeps original order for identical timestamps.
        return lines.OrderBy(l => l.TimeMs).ToList();
    }
}
