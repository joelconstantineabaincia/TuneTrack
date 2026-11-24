namespace TuneTrack.Services;

using TuneTrack.Models;

public record DiversityScoreResult(
    double TotalScore,
    double GenreVariety,
    double ArtistUniqueness,
    double EraSpread,
    string Summary
);

public interface IDiversityScorer
{
    DiversityScoreResult CalculateScore(IReadOnlyList<AnalyzedTrack> tracks);
}

public class DiversityScorer : IDiversityScorer
{
    public DiversityScoreResult CalculateScore(IReadOnlyList<AnalyzedTrack> tracks)
    {
        if (tracks == null || tracks.Count == 0)
        {
            return new DiversityScoreResult(
                TotalScore: 0,
                GenreVariety: 0,
                ArtistUniqueness: 0,
                EraSpread: 0,
                Summary: "Empty playlist: Add tracks to calculate diversity metrics."
            );
        }

        int trackCount = tracks.Count;

        // 1. Genre Variety (40%): Normalized Shannon Entropy over genre distribution
        double genreVarietyScore = CalculateGenreVariety(tracks);

        // 2. Artist Uniqueness (35%): Distinct artists / total tracks
        double artistUniquenessScore = CalculateArtistUniqueness(tracks, trackCount);

        // 3. Era Spread (25%): Distinct decades and year span
        double eraSpreadScore = CalculateEraSpread(tracks);

        // Total weighted score
        double totalScore = (genreVarietyScore * 0.40) +
                            (artistUniquenessScore * 0.35) +
                            (eraSpreadScore * 0.25);

        totalScore = Math.Clamp(Math.Round(totalScore, 1), 0.0, 100.0);
        genreVarietyScore = Math.Clamp(Math.Round(genreVarietyScore, 1), 0.0, 100.0);
        artistUniquenessScore = Math.Clamp(Math.Round(artistUniquenessScore, 1), 0.0, 100.0);
        eraSpreadScore = Math.Clamp(Math.Round(eraSpreadScore, 1), 0.0, 100.0);

        string summary = GetSummary(totalScore);

        return new DiversityScoreResult(
            TotalScore: totalScore,
            GenreVariety: genreVarietyScore,
            ArtistUniqueness: artistUniquenessScore,
            EraSpread: eraSpreadScore,
            Summary: summary
        );
    }

    private static double CalculateGenreVariety(IReadOnlyList<AnalyzedTrack> tracks)
    {
        var genres = tracks
            .Select(t => t.Genre?.Trim())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!.ToLowerInvariant())
            .ToList();

        if (genres.Count == 0)
            return 0.0;

        var genreCounts = genres
            .GroupBy(g => g)
            .Select(group => (double)group.Count())
            .ToList();

        int distinctCount = genreCounts.Count;
        if (distinctCount <= 1)
            return 0.0;

        double total = genres.Count;
        double entropy = 0.0;
        foreach (var count in genreCounts)
        {
            double p = count / total;
            if (p > 0)
            {
                entropy -= p * Math.Log(p);
            }
        }

        // Max possible entropy for min(total, 8) theoretical buckets
        double maxEntropy = Math.Log(Math.Min(total, 8.0));
        if (maxEntropy <= 0)
            return 0.0;

        double normalizedEntropy = Math.Clamp(entropy / maxEntropy, 0.0, 1.0);
        return normalizedEntropy * 100.0;
    }

    private static double CalculateArtistUniqueness(IReadOnlyList<AnalyzedTrack> tracks, int trackCount)
    {
        var distinctArtists = tracks
            .Select(t => t.Artist.Trim().ToLowerInvariant())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct()
            .Count();

        if (trackCount <= 0 || distinctArtists <= 0)
            return 0.0;

        double ratio = (double)distinctArtists / trackCount;
        return Math.Clamp(ratio * 100.0, 0.0, 100.0);
    }

    private static double CalculateEraSpread(IReadOnlyList<AnalyzedTrack> tracks)
    {
        var years = tracks
            .Where(t => t.ReleaseYear.HasValue && t.ReleaseYear.Value >= 1920 && t.ReleaseYear.Value <= DateTime.UtcNow.Year + 1)
            .Select(t => t.ReleaseYear!.Value)
            .ToList();

        if (years.Count == 0)
            return 0.0;

        int minYear = years.Min();
        int maxYear = years.Max();
        int yearSpan = Math.Max(0, maxYear - minYear);

        int distinctDecades = years
            .Select(y => y / 10)
            .Distinct()
            .Count();

        // Up to 5 decades covers 60 points
        double decadeScore = Math.Min(1.0, distinctDecades / 5.0) * 60.0;

        // Year span up to 40 years covers 40 points
        double spanScore = Math.Min(1.0, yearSpan / 40.0) * 40.0;

        return Math.Clamp(decadeScore + spanScore, 0.0, 100.0);
    }

    private static string GetSummary(double score)
    {
        return score switch
        {
            >= 80 => "Exceptional Diversity: An eclectic mix spanning multiple genres, artists, and musical eras.",
            >= 60 => "Well-Rounded: Solid balance of distinct artists and styles with good variety.",
            >= 40 => "Focused Vibe: Centered around a core genre with moderate artist exploration.",
            _ => "Hyper-Focused: Very cohesive, dedicated to a single sound or artist."
        };
    }
}
