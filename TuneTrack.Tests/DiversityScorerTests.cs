namespace TuneTrack.Tests;

using TuneTrack.Models;
using TuneTrack.Services;
using Xunit;

public class DiversityScorerTests
{
    private readonly DiversityScorer _scorer = new();

    [Fact]
    public void EmptyPlaylist_ReturnsZeroAndSafeSummary()
    {
        var result = _scorer.CalculateScore(new List<AnalyzedTrack>());

        Assert.Equal(0, result.TotalScore);
        Assert.Equal(0, result.GenreVariety);
        Assert.Equal(0, result.ArtistUniqueness);
        Assert.Equal(0, result.EraSpread);
        Assert.Contains("Empty playlist", result.Summary);
    }

    [Fact]
    public void SingleTrack_DoesNotDivideByZeroOrNaN()
    {
        var tracks = new List<AnalyzedTrack>
        {
            new() { Title = "Song A", Artist = "Artist 1", Genre = "Pop", ReleaseYear = 2024 }
        };

        var result = _scorer.CalculateScore(tracks);

        Assert.False(double.IsNaN(result.TotalScore));
        Assert.False(double.IsInfinity(result.TotalScore));
        Assert.True(result.TotalScore >= 0 && result.TotalScore <= 100);
        Assert.Equal(100.0, result.ArtistUniqueness);
    }

    [Fact]
    public void IdenticalArtistAndGenre_ScoresLow()
    {
        // 10 songs by the same artist, same genre, same year
        var tracks = Enumerable.Range(1, 10).Select(i => new AnalyzedTrack
        {
            Title = $"Song {i}",
            Artist = "Taylor Swift",
            Genre = "Pop",
            ReleaseYear = 2024
        }).ToList();

        var result = _scorer.CalculateScore(tracks);

        Assert.True(result.ArtistUniqueness <= 15.0);
        Assert.Equal(0.0, result.GenreVariety);
        Assert.True(result.TotalScore < 30.0);
        Assert.Contains("Hyper-Focused", result.Summary);
    }

    [Fact]
    public void EclecticVariedPlaylist_ScoresHigh()
    {
        // 10 songs by 10 distinct artists across 8 genres and multiple decades (1970s to 2020s)
        var tracks = new List<AnalyzedTrack>
        {
            new() { Title = "Track 1", Artist = "David Bowie", Genre = "Classic Rock", ReleaseYear = 1972 },
            new() { Title = "Track 2", Artist = "Michael Jackson", Genre = "Pop", ReleaseYear = 1982 },
            new() { Title = "Track 3", Artist = "Nirvana", Genre = "Grunge", ReleaseYear = 1991 },
            new() { Title = "Track 4", Artist = "Daft Punk", Genre = "Electronic", ReleaseYear = 2001 },
            new() { Title = "Track 5", Artist = "Kendrick Lamar", Genre = "Hip-Hop", ReleaseYear = 2015 },
            new() { Title = "Track 6", Artist = "Miles Davis", Genre = "Jazz", ReleaseYear = 1959 },
            new() { Title = "Track 7", Artist = "Billie Eilish", Genre = "Indie Pop", ReleaseYear = 2024 },
            new() { Title = "Track 8", Artist = "Fela Kuti", Genre = "Afrobeat", ReleaseYear = 1976 },
            new() { Title = "Track 9", Artist = "Aphex Twin", Genre = "IDM", ReleaseYear = 1994 },
            new() { Title = "Track 10", Artist = "Rosalía", Genre = "Flamenco Pop", ReleaseYear = 2022 }
        };

        var result = _scorer.CalculateScore(tracks);

        Assert.Equal(100.0, result.ArtistUniqueness);
        Assert.True(result.GenreVariety >= 80.0);
        Assert.True(result.EraSpread >= 80.0);
        Assert.True(result.TotalScore >= 80.0);
        Assert.Contains("Exceptional Diversity", result.Summary);
    }

    [Fact]
    public void MissingGenresOrReleaseYears_HandledSafely()
    {
        var tracks = new List<AnalyzedTrack>
        {
            new() { Title = "Track 1", Artist = "Unknown A", Genre = null, ReleaseYear = null },
            new() { Title = "Track 2", Artist = "Unknown B", Genre = "", ReleaseYear = null },
            new() { Title = "Track 3", Artist = "Unknown C", Genre = "   ", ReleaseYear = 0 }
        };

        var result = _scorer.CalculateScore(tracks);

        Assert.False(double.IsNaN(result.TotalScore));
        Assert.False(double.IsInfinity(result.TotalScore));
        Assert.Equal(0.0, result.GenreVariety);
        Assert.Equal(0.0, result.EraSpread);
        Assert.Equal(100.0, result.ArtistUniqueness);
        Assert.Equal(35.0, result.TotalScore); // 100 * 0.35
    }
}
