namespace TuneTrack.Services.Providers;

using TuneTrack.Models;

public record PlaylistFetchResult(
    bool Success,
    string? ErrorMessage,
    string Platform,
    string Title,
    string? CoverUrl,
    List<AnalyzedTrack> Tracks,
    bool IsPartialResult
)
{
    public static PlaylistFetchResult Fail(string platform, string errorMessage) =>
        new(false, errorMessage, platform, string.Empty, null, new List<AnalyzedTrack>(), false);

    public static PlaylistFetchResult Partial(string platform, string title, string? coverUrl, string message) =>
        new(true, message, platform, title, coverUrl, new List<AnalyzedTrack>(), true);

    public static PlaylistFetchResult Ok(string platform, string title, string? coverUrl, List<AnalyzedTrack> tracks) =>
        new(true, null, platform, title, coverUrl, tracks, false);
}

public interface IPlaylistProvider
{
    string PlatformName { get; }
    bool CanHandle(Uri uri);
    Task<PlaylistFetchResult> GetPlaylistAsync(Uri uri, CancellationToken cancellationToken = default);
}
