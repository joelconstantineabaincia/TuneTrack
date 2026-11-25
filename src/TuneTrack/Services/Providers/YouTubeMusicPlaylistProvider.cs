namespace TuneTrack.Services.Providers;

using System.Text.RegularExpressions;
using TuneTrack.Models;
using TuneTrack.Services.Recommendations;

public class YouTubeMusicPlaylistProvider : IPlaylistProvider
{
    public string PlatformName => "YouTube Music";

    private readonly HttpClient _httpClient;
    private readonly ITrackEnricherService _enricher;
    private readonly ILogger<YouTubeMusicPlaylistProvider> _logger;

    public YouTubeMusicPlaylistProvider(
        IHttpClientFactory httpClientFactory,
        ITrackEnricherService enricher,
        ILogger<YouTubeMusicPlaylistProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("MusicApi");
        _enricher = enricher;
        _logger = logger;
    }

    public bool CanHandle(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        return (host.Contains("youtube.com") || host.Contains("youtu.be")) &&
               (uri.Query.Contains("list=", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains("/playlist", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<PlaylistFetchResult> GetPlaylistAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return PlaylistFetchResult.Fail(PlatformName, $"YouTube returned status {response.StatusCode}.");
            }

            string html = await response.Content.ReadAsStringAsync(cts.Token);

            string title = ExtractMeta(html, "og:title") ?? "YouTube Playlist";
            title = title.Replace(" - YouTube", "", StringComparison.OrdinalIgnoreCase)
                         .Replace(" - YouTube Music", "", StringComparison.OrdinalIgnoreCase).Trim();
            string? coverUrl = ExtractMeta(html, "og:image");

            var tracks = ParseYtInitialData(html);
            if (tracks.Count > 0)
            {
                await _enricher.EnrichTracksAsync(tracks, maxToEnrich: 30, cts.Token);
                return PlaylistFetchResult.Ok(PlatformName, title, coverUrl, tracks);
            }

            // Return clean partial result for YouTube so UI offers paste fallback
            return PlaylistFetchResult.Partial(PlatformName, title, coverUrl,
                "YouTube playlist detected. Paste your tracklist below to calculate full diversity metrics!");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("YouTube playlist request timed out for {Uri}", uri);
            return PlaylistFetchResult.Fail(PlatformName, "YouTube request timed out. Please try again or paste tracklist.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse YouTube playlist: {Uri}", uri);
            return PlaylistFetchResult.Fail(PlatformName, $"Unable to parse YouTube playlist: {ex.Message}");
        }
    }

    private static string? ExtractMeta(string html, string property)
    {
        var match = Regex.Match(html, $@"<meta property=""{Regex.Escape(property)}"" content=""([^""]*)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<AnalyzedTrack> ParseYtInitialData(string html)
    {
        var tracks = new List<AnalyzedTrack>();
        // Match video renderer title & channel patterns in initial data
        var titleMatches = Regex.Matches(html, @"""playlistVideoRenderer"":\{""videoId"":""[^""]+"",""thumbnail"":\{[^}]+\},""title"":\{""runs"":\[\{""text"":""([^""]+)""\}\]\}");

        foreach (Match m in titleMatches)
        {
            if (m.Success && m.Groups.Count > 1)
            {
                string raw = m.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(raw) && !raw.Contains("Private video") && !raw.Contains("Deleted video"))
                {
                    var parsed = FallbackTrackListParser.ParseSingleLine(raw);
                    tracks.Add(parsed);
                }
            }
        }

        return tracks;
    }
}
