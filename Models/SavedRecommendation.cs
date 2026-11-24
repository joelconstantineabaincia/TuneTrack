namespace TuneTrack.Models;

public class SavedRecommendation
{
    public int Id { get; set; }

    /// <summary>
    /// Anonymous per-browser client identifier.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string? Genre { get; set; }

    public string? ArtworkUrl { get; set; }

    public int? SourcePlaylistId { get; set; }

    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}
