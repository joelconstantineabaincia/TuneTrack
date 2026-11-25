namespace TuneTrack.Models;

public class AnalyzedTrack
{
    public int Id { get; set; }

    public int PlaylistId { get; set; }

    public AnalyzedPlaylist? Playlist { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string? Album { get; set; }

    public string? Genre { get; set; }

    public int? ReleaseYear { get; set; }

    public int DurationMs { get; set; }

    public string? ArtworkUrl { get; set; }

    public string? PreviewUrl { get; set; }
}
