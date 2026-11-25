namespace TuneTrack.Services.Providers;

using System.Text.RegularExpressions;
using TuneTrack.Models;

public static partial class FallbackTrackListParser
{
    public static List<AnalyzedTrack> ParseTrackList(string rawText)
    {
        var tracks = new List<AnalyzedTrack>();
        if (string.IsNullOrWhiteSpace(rawText))
            return tracks;

        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Remove leading list numbering like "1. ", "2) ", or "04 - " while keeping names like "50 Cent"
            trimmed = StripLeadingNumber(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            tracks.Add(ParseSingleLine(trimmed));
        }

        return tracks;
    }

    public static AnalyzedTrack ParseSingleLine(string line)
    {
        string normalized = NormalizeSpaces(line);

        // 1. Prefer spaced separators (" - ", " – ", " — ", " | ") so hyphenated names like "Jay-Z" stay intact
        var spacedMatch = SpacedSeparatorRegex().Match(normalized);
        if (spacedMatch.Success && spacedMatch.Index > 0)
        {
            string artist = NormalizeSpaces(normalized[..spacedMatch.Index]);
            string title = NormalizeSpaces(normalized[(spacedMatch.Index + spacedMatch.Length)..]);
            if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
            {
                return new AnalyzedTrack { Artist = artist, Title = title, DurationMs = 180000 };
            }
        }

        // 2. Fall back to unspaced em-dash, en-dash, or pipe
        foreach (char sep in new[] { '—', '–', '|' })
        {
            int idx = normalized.IndexOf(sep);
            if (idx > 0 && idx < normalized.Length - 1)
            {
                string artist = NormalizeSpaces(normalized[..idx]);
                string title = NormalizeSpaces(normalized[(idx + 1)..]);
                if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
                {
                    return new AnalyzedTrack { Artist = artist, Title = title, DurationMs = 180000 };
                }
            }
        }

        // 3. Check for "Title by Artist"
        int byIdx = normalized.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (byIdx > 0)
        {
            string title = NormalizeSpaces(normalized[..byIdx]);
            string artist = NormalizeSpaces(normalized[(byIdx + 4)..]);
            return new AnalyzedTrack { Artist = artist, Title = title, DurationMs = 180000 };
        }

        // 4. Fallback: Entire line is title with unknown artist
        return new AnalyzedTrack
        {
            Title = normalized,
            Artist = "Various Artists",
            DurationMs = 180000
        };
    }

    private static string StripLeadingNumber(string line)
    {
        var dotOrParenMatch = LeadingDotOrParenRegex().Match(line);
        if (dotOrParenMatch.Success)
        {
            return line[dotOrParenMatch.Length..].Trim();
        }

        var dashMatch = LeadingDashNumberRegex().Match(line);
        if (dashMatch.Success)
        {
            string remainder = line[dashMatch.Length..].Trim();
            if (HasTrackSeparator(remainder))
            {
                return remainder;
            }
        }

        return line;
    }

    private static bool HasTrackSeparator(string text) =>
        SpacedSeparatorRegex().IsMatch(text) ||
        text.IndexOfAny(new[] { '—', '–', '|' }) > 0 ||
        text.Contains(" by ", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSpaces(string value) =>
        MultiWhitespaceRegex().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s+(?:—|–|-|\|)\s+")]
    private static partial Regex SpacedSeparatorRegex();

    [GeneratedRegex(@"^\d+\s*[.)]\s*")]
    private static partial Regex LeadingDotOrParenRegex();

    [GeneratedRegex(@"^\d{1,3}\s*[-–—]\s*")]
    private static partial Regex LeadingDashNumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiWhitespaceRegex();
}
