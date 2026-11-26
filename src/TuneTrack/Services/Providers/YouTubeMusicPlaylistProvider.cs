namespace TuneTrack.Services.Providers;

using System.Text.Json;
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

    public Task<PlaylistFetchResult> GetPlaylistAsync(Uri uri, CancellationToken cancellationToken = default)
        => GetPlaylistAsync(uri, progress: null, cancellationToken);

    public async Task<PlaylistFetchResult> GetPlaylistAsync(
        Uri uri,
        IProgress<(int Completed, int Total)>? progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Uri targetUri = BuildWebPlaylistUri(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAESEwgDEgk0ODE3Nzk3MjQaAmVuIAEaBgiA_LyaBg; CONSENT=YES+cb");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return PlaylistFetchResult.Partial(PlatformName, "YouTube Music Playlist", null,
                    "YouTube Music doesn't share track lists publicly. Paste your tracks, one 'Artist - Title' per line, and we'll analyze them.");
            }

            string html = await response.Content.ReadAsStringAsync(cts.Token);

            string title = ExtractMeta(html, "og:title") ?? "YouTube Music Playlist";
            title = CleanPlaylistTitle(title);
            string? coverUrl = ExtractMeta(html, "og:image");

            var (ytTitle, ytCover, tracks) = ParseYtInitialData(html);
            if (!string.IsNullOrWhiteSpace(ytTitle))
                title = CleanPlaylistTitle(ytTitle);
            if (!string.IsNullOrWhiteSpace(ytCover))
                coverUrl = ytCover;

            if (tracks.Count > 0)
            {
                await _enricher.EnrichTracksAsync(tracks, progress, maxToEnrich: 100, cancellationToken);
                return PlaylistFetchResult.Ok(PlatformName, title, coverUrl, tracks);
            }

            return PlaylistFetchResult.Partial(PlatformName, title, coverUrl,
                "YouTube Music doesn't share track lists publicly. Paste your tracks, one 'Artist - Title' per line, and we'll analyze them.");
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

    private static Uri BuildWebPlaylistUri(Uri uri)
    {
        var listMatch = Regex.Match(uri.Query, @"[?&]list=([^&]+)", RegexOptions.IgnoreCase);
        if (listMatch.Success)
        {
            string listId = listMatch.Groups[1].Value;
            return new Uri($"https://www.youtube.com/playlist?list={listId}");
        }
        return uri;
    }

    private static string CleanPlaylistTitle(string raw) =>
        raw.Replace(" - YouTube Music", "", StringComparison.OrdinalIgnoreCase)
           .Replace(" - YouTube", "", StringComparison.OrdinalIgnoreCase)
           .Trim();

    private static string? ExtractMeta(string html, string property)
    {
        var match = Regex.Match(html, $@"<meta property=""{Regex.Escape(property)}"" content=""([^""]*)""");
        return match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)
            ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value)
            : null;
    }

    private static (string? Title, string? CoverUrl, List<AnalyzedTrack> Tracks) ParseYtInitialData(string html)
    {
        var tracks = new List<AnalyzedTrack>();
        string? json = ExtractJsonObjectAfter(html, "var ytInitialData =", "window[\"ytInitialData\"] =");
        if (string.IsNullOrWhiteSpace(json))
            return (null, null, tracks);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? title = null;
            if (root.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("playlistMetadataRenderer", out var pmr) &&
                pmr.TryGetProperty("title", out var tProp))
            {
                title = tProp.GetString();
            }

            string? coverUrl = null;
            if (root.TryGetProperty("microformat", out var micro) &&
                micro.TryGetProperty("microformatDataRenderer", out var mdr) &&
                mdr.TryGetProperty("thumbnail", out var thumb) &&
                thumb.TryGetProperty("thumbnails", out var thumbs) &&
                thumbs.ValueKind == JsonValueKind.Array &&
                thumbs.GetArrayLength() > 0)
            {
                coverUrl = thumbs[thumbs.GetArrayLength() - 1].TryGetProperty("url", out var u) ? u.GetString() : null;
            }

            CollectPlaylistVideoRenderers(root, tracks);
            return (title, coverUrl, tracks);
        }
        catch (JsonException)
        {
            return (null, null, tracks);
        }
    }

    private static void CollectPlaylistVideoRenderers(JsonElement element, List<AnalyzedTrack> tracks)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("playlistVideoRenderer", out var renderer) &&
                renderer.ValueKind == JsonValueKind.Object)
            {
                var track = ParseVideoRenderer(renderer);
                if (track != null)
                {
                    tracks.Add(track);
                }
                return;
            }

            foreach (var prop in element.EnumerateObject())
            {
                CollectPlaylistVideoRenderers(prop.Value, tracks);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectPlaylistVideoRenderers(item, tracks);
            }
        }
    }

    private static AnalyzedTrack? ParseVideoRenderer(JsonElement renderer)
    {
        string rawTitle = ReadRunsText(renderer, "title");
        if (string.IsNullOrWhiteSpace(rawTitle) ||
            rawTitle.Contains("[Private video]", StringComparison.OrdinalIgnoreCase) ||
            rawTitle.Contains("[Deleted video]", StringComparison.OrdinalIgnoreCase) ||
            rawTitle.Equals("Private video", StringComparison.OrdinalIgnoreCase) ||
            rawTitle.Equals("Deleted video", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string channel = ReadRunsText(renderer, "shortBylineText");
        if (string.IsNullOrWhiteSpace(channel))
            channel = ReadRunsText(renderer, "longBylineText");

        int durationMs = 180000;
        if (renderer.TryGetProperty("lengthSeconds", out var lenProp))
        {
            string? secStr = lenProp.ValueKind == JsonValueKind.String ? lenProp.GetString() : lenProp.ToString();
            if (int.TryParse(secStr, out int seconds) && seconds > 0)
            {
                durationMs = seconds * 1000;
            }
        }

        if (channel.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
        {
            string topicArtist = channel[..^" - Topic".Length].Trim();
            if (!string.IsNullOrWhiteSpace(topicArtist))
            {
                return new AnalyzedTrack
                {
                    Artist = topicArtist,
                    Title = rawTitle.Trim(),
                    DurationMs = durationMs
                };
            }
        }

        var parsed = FallbackTrackListParser.ParseSingleLine(rawTitle);
        if (!parsed.Artist.Equals("Various Artists", StringComparison.OrdinalIgnoreCase))
        {
            parsed.DurationMs = durationMs;
            return parsed;
        }

        return new AnalyzedTrack
        {
            Artist = string.IsNullOrWhiteSpace(channel) ? "Various Artists" : channel.Trim(),
            Title = rawTitle.Trim(),
            DurationMs = durationMs
        };
    }

    private static string ReadRunsText(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el))
            return string.Empty;

        if (el.TryGetProperty("simpleText", out var simple))
            return simple.GetString() ?? string.Empty;

        if (el.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array && runs.GetArrayLength() > 0)
        {
            return string.Concat(runs.EnumerateArray()
                .Select(r => r.TryGetProperty("text", out var t) ? t.GetString() : null)
                .Where(s => !string.IsNullOrEmpty(s)));
        }

        return string.Empty;
    }

    private static string? ExtractJsonObjectAfter(string html, params string[] markers)
    {
        int startObj = -1;
        foreach (var marker in markers)
        {
            int idx = html.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                startObj = html.IndexOf('{', idx + marker.Length);
                if (startObj >= 0)
                    break;
            }
        }

        if (startObj < 0)
            return null;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = startObj; i < html.Length; i++)
        {
            char c = html[i];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
                inString = true;
            else if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return html[startObj..(i + 1)];
            }
        }

        return null;
    }
}
