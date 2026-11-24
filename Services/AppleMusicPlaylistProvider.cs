namespace TuneTrack.Services;

using System.Text.RegularExpressions;
using TuneTrack.Models;

public class AppleMusicPlaylistProvider : IPlaylistProvider
{
    public string PlatformName => "Apple Music";

    private readonly HttpClient _httpClient;
    private readonly ITrackEnricherService _enricher;
    private readonly ILogger<AppleMusicPlaylistProvider> _logger;

    public AppleMusicPlaylistProvider(
        IHttpClientFactory httpClientFactory,
        ITrackEnricherService enricher,
        ILogger<AppleMusicPlaylistProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("MusicApi");
        _enricher = enricher;
        _logger = logger;
    }

    public bool CanHandle(Uri uri)
    {
        return uri.Host.Contains("music.apple.com", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.Contains("/playlist/", StringComparison.OrdinalIgnoreCase);
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
                return PlaylistFetchResult.Fail(PlatformName, $"Apple Music returned HTTP {response.StatusCode}.");
            }

            string html = await response.Content.ReadAsStringAsync(cts.Token);

            // Extract title and cover
            string title = ExtractMeta(html, "og:title") ?? "Apple Music Playlist";
            title = title.Replace(" on Apple Music", "", StringComparison.OrdinalIgnoreCase).Trim();
            string? coverUrl = ExtractMeta(html, "og:image");

            // Extract music:song meta tags (publicly rendered track references)
            var songMatches = Regex.Matches(html, @"<meta property=""music:song"" content=""https://music\.apple\.com/[^""]*/song/([^/]+)/\d+""");
            var durationMatches = Regex.Matches(html, @"<meta property=""music:song:duration"" content=""PT(?:(\d+)M)?(?:(\d+)S)?""");

            var tracks = new List<AnalyzedTrack>();
            for (int i = 0; i < songMatches.Count; i++)
            {
                string slug = songMatches[i].Groups[1].Value;
                string trackTitle = CleanSlug(slug);

                int durationMs = 180000;
                if (i < durationMatches.Count)
                {
                    int minutes = int.TryParse(durationMatches[i].Groups[1].Value, out int m) ? m : 0;
                    int seconds = int.TryParse(durationMatches[i].Groups[2].Value, out int s) ? s : 0;
                    durationMs = ((minutes * 60) + seconds) * 1000;
                }

                tracks.Add(new AnalyzedTrack
                {
                    Title = trackTitle,
                    Artist = "Apple Music Artist",
                    DurationMs = durationMs > 0 ? durationMs : 180000,
                    ArtworkUrl = coverUrl
                });
            }

            if (tracks.Count > 0)
            {
                // Enrich using iTunes API
                await _enricher.EnrichTracksAsync(tracks, maxToEnrich: 30, cts.Token);
                return PlaylistFetchResult.Ok(PlatformName, title, coverUrl, tracks);
            }

            return PlaylistFetchResult.Partial(PlatformName, title, coverUrl,
                "Retrieved playlist header. Paste your tracklist below to enrich and calculate diversity.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Apple Music fetch timed out for {Uri}", uri);
            return PlaylistFetchResult.Fail(PlatformName, "Apple Music request timed out. Please try again or paste tracklist.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Apple Music playlist: {Uri}", uri);
            return PlaylistFetchResult.Fail(PlatformName, $"Unable to parse Apple Music playlist: {ex.Message}");
        }
    }

    private static string? ExtractMeta(string html, string property)
    {
        var match = Regex.Match(html, $@"<meta property=""{Regex.Escape(property)}"" content=""([^""]*)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string CleanSlug(string slug)
    {
        string readable = slug.Replace('-', ' ');
        return Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(readable);
    }
}
