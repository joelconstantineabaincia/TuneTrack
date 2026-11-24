namespace TuneTrack.Services;

using TuneTrack.Models;

public static class FallbackTrackListParser
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

            // Remove leading track numbers like "1. ", "01 - ", etc.
            trimmed = StripLeadingNumber(trimmed);

            var track = ParseSingleLine(trimmed);
            tracks.Add(track);
        }

        return tracks;
    }

    public static AnalyzedTrack ParseSingleLine(string line)
    {
        // 1. Check for "Artist - Title" or "Artist — Title"
        string[] separators = { " - ", " — ", " – ", " | " };
        foreach (var sep in separators)
        {
            int idx = line.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                string artist = line[..idx].Trim();
                string title = line[(idx + sep.Length)..].Trim();
                return new AnalyzedTrack
                {
                    Artist = artist,
                    Title = title,
                    DurationMs = 180000
                };
            }
        }

        // 2. Check for "Title by Artist"
        int byIdx = line.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (byIdx > 0)
        {
            string title = line[..byIdx].Trim();
            string artist = line[(byIdx + 4)..].Trim();
            return new AnalyzedTrack
            {
                Artist = artist,
                Title = title,
                DurationMs = 180000
            };
        }

        // 3. Fallback: Entire line is title with unknown artist
        return new AnalyzedTrack
        {
            Title = line.Trim(),
            Artist = "Various Artists",
            DurationMs = 180000
        };
    }

    private static string StripLeadingNumber(string line)
    {
        int i = 0;
        while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == '-' || line[i] == ' ' || line[i] == ')'))
        {
            i++;
        }

        if (i > 0 && i < line.Length && (line[i - 1] == '.' || line[i - 1] == ' ' || line[i - 1] == '-'))
        {
            return line[i..].Trim();
        }

        return line;
    }
}
