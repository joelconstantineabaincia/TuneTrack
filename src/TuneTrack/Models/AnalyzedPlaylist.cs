namespace TuneTrack.Models;

public class AnalyzedPlaylist
{
    public int Id { get; set; }

    /// <summary>
    /// Anonymous per-browser client identifier.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? CoverUrl { get; set; }

    public double DiversityScore { get; set; }

    public double GenreVariety { get; set; }

    public double ArtistUniqueness { get; set; }

    public double EraSpread { get; set; }

    public int TrackCount { get; set; }

    public string TotalDuration { get; set; } = string.Empty;

    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AnalyzedTrack> Tracks { get; set; } = new List<AnalyzedTrack>();
}
