namespace TuneTrack.Services.Providers;

using System.Text.Json;
using System.Text.Json.Serialization;
using TuneTrack.Models;
using TuneTrack.Services.Recommendations;

public class SpotifyPlaylistProvider : IPlaylistProvider
{
    public string PlatformName => "Spotify";

    private readonly HttpClient _httpClient;
    private readonly ITrackEnricherService _enricher;
    private readonly ILogger<SpotifyPlaylistProvider> _logger;

    public SpotifyPlaylistProvider(
        IHttpClientFactory httpClientFactory,
        ITrackEnricherService enricher,
        ILogger<SpotifyPlaylistProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("MusicApi");
        _enricher = enricher;
        _logger = logger;
    }

    public bool CanHandle(Uri uri)
    {
        return uri.Host.Contains("spotify.com", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.Contains("/playlist/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PlaylistFetchResult> GetPlaylistAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        string playlistId = ExtractPlaylistId(uri);
        if (string.IsNullOrEmpty(playlistId))
        {
            return PlaylistFetchResult.Fail(PlatformName, "Invalid Spotify playlist URL. Could not parse playlist ID.");
        }

        try
        {
            // 1. Fetch public embed page which contains complete __NEXT_DATA__
            string embedUrl = $"https://open.spotify.com/embed/playlist/{playlistId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, embedUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cts.Token);
                var result = ParseNextData(html);
                if (result != null && result.Tracks.Count > 0)
                {
                    // Enrich genres, release years, artwork via iTunes API
                    await _enricher.EnrichTracksAsync(result.Tracks, maxToEnrich: 30, cts.Token);
                    return result;
                }
            }

            // 2. Fallback to public oEmbed if embed page data was blocked/empty
            return await FetchOembedFallbackAsync(uri, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Spotify playlist fetch timed out for {Uri}", uri);
            return PlaylistFetchResult.Fail(PlatformName, "Spotify request timed out. Please try again or paste tracklist.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Spotify playlist for {Uri}", uri);
            return PlaylistFetchResult.Fail(PlatformName, $"Unable to retrieve Spotify playlist: {ex.Message}");
        }
    }

    private static string ExtractPlaylistId(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].Equals("playlist", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                return segments[i + 1].Split('?')[0];
            }
        }
        return string.Empty;
    }

    private static PlaylistFetchResult? ParseNextData(string html)
    {
        const string marker = "<script id=\"__NEXT_DATA__\" type=\"application/json\">";
        int startIdx = html.IndexOf(marker, StringComparison.Ordinal);
        if (startIdx < 0) return null;

        startIdx += marker.Length;
        int endIdx = html.IndexOf("</script>", startIdx, StringComparison.Ordinal);
        if (endIdx < 0) return null;

        string json = html[startIdx..endIdx];
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("props", out var props) ||
            !props.TryGetProperty("pageProps", out var pageProps) ||
            !pageProps.TryGetProperty("state", out var state) ||
            !state.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("entity", out var entity))
        {
            return null;
        }

        string title = entity.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Spotify Playlist" : "Spotify Playlist";

        string? coverUrl = null;
        if (entity.TryGetProperty("coverArt", out var coverArt) &&
            coverArt.TryGetProperty("sources", out var sources) &&
            sources.ValueKind == JsonValueKind.Array &&
            sources.GetArrayLength() > 0)
        {
            coverUrl = sources[0].GetProperty("url").GetString();
        }

        var tracks = new List<AnalyzedTrack>();
        if (entity.TryGetProperty("trackList", out var trackList) && trackList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in trackList.EnumerateArray())
            {
                string trackTitle = item.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? string.Empty : string.Empty;
                string subtitle = item.TryGetProperty("subtitle", out var sProp) ? sProp.GetString() ?? string.Empty : string.Empty;
                int duration = item.TryGetProperty("duration", out var dProp) && dProp.TryGetInt32(out int d) ? d : 180000;

                if (!string.IsNullOrWhiteSpace(trackTitle))
                {
                    tracks.Add(new AnalyzedTrack
                    {
                        Title = trackTitle,
                        Artist = subtitle.Replace('\u00A0', ' ').Trim(),
                        DurationMs = duration,
                        ArtworkUrl = coverUrl
                    });
                }
            }
        }

        return PlaylistFetchResult.Ok("Spotify", title, coverUrl, tracks);
    }

    private async Task<PlaylistFetchResult> FetchOembedFallbackAsync(Uri uri, CancellationToken ct)
    {
        string oembedUrl = $"https://open.spotify.com/oembed?url={Uri.EscapeDataString(uri.ToString())}";
        using var resp = await _httpClient.GetAsync(oembedUrl, ct);
        if (resp.IsSuccessStatusCode)
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            string title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "Spotify Playlist" : "Spotify Playlist";
            string? thumbnail = doc.RootElement.TryGetProperty("thumbnail_url", out var th) ? th.GetString() : null;

            return PlaylistFetchResult.Partial("Spotify", title, thumbnail,
                "Spotify protected full track listing. Paste your tracklist below to complete diversity scoring!");
        }

        return PlaylistFetchResult.Fail(PlatformName, "Could not fetch playlist metadata from Spotify.");
    }
}
