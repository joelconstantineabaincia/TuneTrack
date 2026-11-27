namespace TuneTrack.Services.Providers;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using TuneTrack.Models;
using TuneTrack.Services.Recommendations;

public class AppleMusicPlaylistProvider : IPlaylistProvider
{
    private const int MaxTracksToResolve = 100;
    private const int DefaultDurationMs = 180_000;
    private const string UnknownArtistLabel = "Unknown artist";

    public string PlatformName => "Apple Music";

    private readonly HttpClient _httpClient;
    private readonly ITrackEnricherService _enricher;
    private readonly ILogger<AppleMusicPlaylistProvider> _logger;

    private sealed record ParsedTrack(
        string Title,
        string Artist,
        string? Album,
        int DurationMs,
        string? TrackId
    );

    public AppleMusicPlaylistProvider(
        IHttpClientFactory httpClientFactory,
        ITrackEnricherService enricher,
        ILogger<AppleMusicPlaylistProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("MusicApi");
        _enricher = enricher;
        _logger = logger;
    }

    public bool CanHandle(Uri uri) =>
        uri.Host.Contains("music.apple.com", StringComparison.OrdinalIgnoreCase);

    public Task<PlaylistFetchResult> GetPlaylistAsync(Uri uri, CancellationToken cancellationToken = default)
        => GetPlaylistAsync(uri, progress: null, cancellationToken);

    public async Task<PlaylistFetchResult> GetPlaylistAsync(
        Uri uri,
        IProgress<(int Completed, int Total)>? progress,
        CancellationToken cancellationToken = default)
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

            string? parsedTitle = null;
            string? parsedCover = null;
            List<ParsedTrack> parsedTracks = new();

            try
            {
                (parsedTitle, parsedCover, parsedTracks) = ParseSerializedServerData(html);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Could not parse Apple Music serialized-server-data for {Uri}", uri);
            }

            string title = CleanAppleMusicTitle(parsedTitle ?? ExtractMeta(html, "og:title")) ?? "Apple Music Playlist";
            string? coverUrl = ExtractMeta(html, "og:image") ?? parsedCover;

            if (!uri.AbsolutePath.Contains("/playlist/", StringComparison.OrdinalIgnoreCase))
            {
                return PlaylistFetchResult.Partial(
                    PlatformName,
                    title,
                    coverUrl,
                    "TuneTrack analyzes public Apple Music playlists. Paste your tracks, one 'Artist - Title' per line, and we'll analyze them.");
            }

            string? storefront = ExtractStorefront(uri);
            var fallbackMetaSongs = ExtractMetaSongReferences(html);
            var tracks = await BuildResolvedTracksAsync(parsedTracks, fallbackMetaSongs, storefront, progress, cancellationToken);

            if (tracks.Count == 0)
            {
                return PlaylistFetchResult.Partial(
                    PlatformName,
                    title,
                    coverUrl,
                    "Apple Music doesn't share track lists publicly. Paste your tracks, one 'Artist - Title' per line, and we'll analyze them.");
            }

            int recoveredArtistCount = tracks.Count(HasKnownArtist);
            if (recoveredArtistCount * 2 <= tracks.Count)
            {
                return PlaylistFetchResult.Partial(
                    PlatformName,
                    title,
                    coverUrl,
                    "Apple Music didn't provide artist details for most tracks on this playlist. Paste your tracks, one 'Artist - Title' per line, and we'll analyze them.");
            }

            return PlaylistFetchResult.Ok(PlatformName, title, coverUrl, tracks);
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

    private async Task<List<AnalyzedTrack>> BuildResolvedTracksAsync(
        List<ParsedTrack> parsedTracks,
        List<(string TrackId, int DurationMs)> metaSongs,
        string? storefront,
        IProgress<(int Completed, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        if (parsedTracks.Count > 0)
        {
            var limited = parsedTracks.Take(MaxTracksToResolve).ToList();
            bool alignMetaIds = metaSongs.Count == parsedTracks.Count;

            var enrichedParsed = limited.Select((t, idx) =>
            {
                string? id = !string.IsNullOrEmpty(t.TrackId) ? t.TrackId : (alignMetaIds ? metaSongs[idx].TrackId : null);
                int duration = t.DurationMs > 0 ? t.DurationMs : (alignMetaIds && metaSongs[idx].DurationMs > 0 ? metaSongs[idx].DurationMs : 0);
                return t with { TrackId = id, DurationMs = duration };
            }).ToList();

            var trackIds = enrichedParsed
                .Select(t => t.TrackId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var lookupMap = trackIds.Count > 0
                ? await _enricher.LookupTracksByIdsAsync(trackIds, storefront, progress, cancellationToken)
                : new Dictionary<string, TrackLookupItem>();

            return enrichedParsed.Select(t =>
            {
                TrackLookupItem? resolved = !string.IsNullOrEmpty(t.TrackId) && lookupMap.TryGetValue(t.TrackId, out var hit)
                    ? hit
                    : null;

                string artist = !string.IsNullOrWhiteSpace(t.Artist)
                    ? t.Artist
                    : (!string.IsNullOrWhiteSpace(resolved?.Artist) ? resolved.Artist.Trim() : UnknownArtistLabel);

                int durationMs = t.DurationMs > 0
                    ? t.DurationMs
                    : (resolved?.DurationMs is > 0 ? resolved.DurationMs.Value : DefaultDurationMs);

                string? album = !string.IsNullOrWhiteSpace(resolved?.Metadata.Album)
                    ? resolved.Metadata.Album
                    : t.Album;

                return new AnalyzedTrack
                {
                    Title = t.Title,
                    Artist = artist,
                    Album = album,
                    Genre = resolved?.Metadata.Genre,
                    ReleaseYear = resolved?.Metadata.ReleaseYear,
                    ArtworkUrl = resolved?.Metadata.ArtworkUrl,
                    PreviewUrl = resolved?.Metadata.PreviewUrl,
                    DurationMs = durationMs
                };
            }).ToList();
        }

        // Fallback when structured JSON fails/omits tracks but numeric song IDs exist in meta/JSON-LD
        if (metaSongs.Count == 0)
            return new List<AnalyzedTrack>();

        var fallbackSongs = metaSongs.Take(MaxTracksToResolve).ToList();
        var fallbackLookup = await _enricher.LookupTracksByIdsAsync(
            fallbackSongs.Select(m => m.TrackId),
            storefront,
            progress,
            cancellationToken);

        var built = new List<AnalyzedTrack>();
        foreach (var (trackId, metaDurationMs) in fallbackSongs)
        {
            if (!fallbackLookup.TryGetValue(trackId, out var hit))
                continue;

            string title = !string.IsNullOrWhiteSpace(hit.Title) ? hit.Title.Trim() : $"Track {trackId}";
            string artist = !string.IsNullOrWhiteSpace(hit.Artist) ? hit.Artist.Trim() : UnknownArtistLabel;
            int durationMs = metaDurationMs > 0
                ? metaDurationMs
                : (hit.DurationMs is > 0 ? hit.DurationMs.Value : DefaultDurationMs);

            built.Add(new AnalyzedTrack
            {
                Title = title,
                Artist = artist,
                Album = hit.Metadata.Album,
                Genre = hit.Metadata.Genre,
                ReleaseYear = hit.Metadata.ReleaseYear,
                ArtworkUrl = hit.Metadata.ArtworkUrl,
                PreviewUrl = hit.Metadata.PreviewUrl,
                DurationMs = durationMs
            });
        }

        return built;
    }

    private static (string? Title, string? CoverUrl, List<ParsedTrack> Tracks) ParseSerializedServerData(string html)
    {
        var match = Regex.Match(
            html,
            @"<script\b[^>]*\bid=[""']serialized-server-data[""'][^>]*>([\s\S]*?)</script>",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return (null, null, new List<ParsedTrack>());

        string json = match.Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(json))
            return (null, null, new List<ParsedTrack>());

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var dataArr) ||
            dataArr.ValueKind != JsonValueKind.Array ||
            dataArr.GetArrayLength() == 0)
        {
            return (null, null, new List<ParsedTrack>());
        }

        string? headerTitle = null;
        string? headerCover = null;
        var tracks = new List<ParsedTrack>();

        foreach (var dataEntry in dataArr.EnumerateArray())
        {
            if (!dataEntry.TryGetProperty("data", out var pageData) ||
                !pageData.TryGetProperty("sections", out var sections) ||
                sections.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var section in sections.EnumerateArray())
            {
                string? kind = section.TryGetProperty("itemKind", out var kindEl) ? kindEl.GetString() : null;
                if (!section.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    continue;

                if (string.Equals(kind, "containerDetailHeaderLockup", StringComparison.OrdinalIgnoreCase) &&
                    items.GetArrayLength() > 0)
                {
                    var header = items[0];
                    if (string.IsNullOrWhiteSpace(headerTitle) &&
                        header.TryGetProperty("title", out var hTitleEl) &&
                        hTitleEl.ValueKind == JsonValueKind.String)
                    {
                        headerTitle = hTitleEl.GetString();
                    }
                    headerCover ??= ExtractArtworkTemplateUrl(header);
                }
                else if (string.Equals(kind, "trackLockup", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var parsed = ParseTrackLockup(item);
                        if (parsed != null)
                        {
                            tracks.Add(parsed);
                        }
                    }
                }
            }
        }

        return (headerTitle, headerCover, tracks);
    }

    private static ParsedTrack? ParseTrackLockup(JsonElement item)
    {
        if (!item.TryGetProperty("title", out var titleEl) || titleEl.ValueKind != JsonValueKind.String)
            return null;

        string title = titleEl.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(title))
            return null;

        string artist = ExtractArtistFromLockup(item);
        string? album = ExtractAlbumFromLockup(item);
        int durationMs = item.TryGetProperty("duration", out var durEl) && durEl.TryGetInt32(out int ms) && ms > 0 ? ms : 0;
        string? trackId = ExtractTrackIdFromLockup(item);

        return new ParsedTrack(title, artist, album, durationMs, trackId);
    }

    private static string ExtractArtistFromLockup(JsonElement item)
    {
        if (item.TryGetProperty("subtitleLinks", out var subLinks) && subLinks.ValueKind == JsonValueKind.Array)
        {
            var names = subLinks.EnumerateArray()
                .Select(link => link.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()?.Trim() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();

            if (names.Count > 0)
                return string.Join(", ", names);
        }

        if (item.TryGetProperty("artistName", out var artistEl) && artistEl.ValueKind == JsonValueKind.String)
        {
            string? raw = artistEl.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(raw))
                return raw;
        }

        return string.Empty;
    }

    private static string? ExtractAlbumFromLockup(JsonElement item)
    {
        if (!item.TryGetProperty("tertiaryLinks", out var links) ||
            links.ValueKind != JsonValueKind.Array ||
            links.GetArrayLength() == 0)
        {
            return null;
        }

        if (links[0].TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
        {
            string? album = titleEl.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(album) ? null : album;
        }

        return null;
    }

    private static string? ExtractTrackIdFromLockup(JsonElement item)
    {
        if (item.TryGetProperty("contentDescriptor", out var cd))
        {
            if (cd.TryGetProperty("identifiers", out var ids) &&
                ids.TryGetProperty("storeAdamID", out var adamId) &&
                adamId.ValueKind == JsonValueKind.String)
            {
                string? val = adamId.GetString()?.Trim();
                if (!string.IsNullOrEmpty(val) && val.All(char.IsDigit))
                    return val;
            }

            if (cd.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
            {
                string? fromUrl = ExtractTrackIdFromUrl(urlEl.GetString());
                if (!string.IsNullOrEmpty(fromUrl))
                    return fromUrl;
            }
        }

        if (item.TryGetProperty("impressionMetrics", out var metrics) &&
            metrics.TryGetProperty("fields", out var fields) &&
            fields.TryGetProperty("id", out var idField) &&
            idField.ValueKind == JsonValueKind.String)
        {
            string? val = idField.GetString()?.Trim();
            if (!string.IsNullOrEmpty(val) && val.All(char.IsDigit))
                return val;
        }

        if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
        {
            var trailingMatch = Regex.Match(idEl.GetString() ?? string.Empty, @"(\d+)$");
            if (trailingMatch.Success)
                return trailingMatch.Groups[1].Value;
        }

        return null;
    }

    private static string? ExtractArtworkTemplateUrl(JsonElement container)
    {
        if (!container.TryGetProperty("artwork", out var artwork) ||
            !artwork.TryGetProperty("dictionary", out var dict) ||
            !dict.TryGetProperty("url", out var urlEl) ||
            urlEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? template = urlEl.GetString();
        if (string.IsNullOrWhiteSpace(template))
            return null;

        return template
            .Replace("{w}", "600", StringComparison.Ordinal)
            .Replace("{h}", "600", StringComparison.Ordinal)
            .Replace("{f}", "jpg", StringComparison.Ordinal);
    }

    private static List<(string TrackId, int DurationMs)> ExtractMetaSongReferences(string html)
    {
        var results = new List<(string TrackId, int DurationMs)>();
        var songMatches = Regex.Matches(html, @"<meta\b[^>]*property=[""']music:song[""'][^>]*content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        var durationMatches = Regex.Matches(html, @"<meta\b[^>]*property=[""']music:song:duration[""'][^>]*content=[""']PT(?:(\d+)M)?(?:(\d+)S)?[""']", RegexOptions.IgnoreCase);

        for (int i = 0; i < songMatches.Count; i++)
        {
            string? id = ExtractTrackIdFromUrl(songMatches[i].Groups[1].Value);
            if (string.IsNullOrEmpty(id))
                continue;

            int durationMs = 0;
            if (i < durationMatches.Count)
            {
                int minutes = int.TryParse(durationMatches[i].Groups[1].Value, out int m) ? m : 0;
                int seconds = int.TryParse(durationMatches[i].Groups[2].Value, out int s) ? s : 0;
                durationMs = ((minutes * 60) + seconds) * 1000;
            }

            results.Add((id, durationMs));
        }

        if (results.Count > 0)
            return results;

        // Secondary fallback: JSON-LD music-playlist URLs
        var ldMatch = Regex.Match(html, @"<script\b[^>]*\bid=[""']?schema:music-playlist[""']?[^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase);
        if (ldMatch.Success)
        {
            foreach (Match m in Regex.Matches(ldMatch.Groups[1].Value, @"/song/[^""'/]+/(\d+)"))
            {
                results.Add((m.Groups[1].Value, 0));
            }
        }

        return results;
    }

    private static string? ExtractTrackIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var queryMatch = Regex.Match(url, @"[?&]i=(\d+)");
        if (queryMatch.Success)
            return queryMatch.Groups[1].Value;

        var pathMatch = Regex.Match(url, @"/song/[^""'?]+/(\d+)");
        return pathMatch.Success ? pathMatch.Groups[1].Value : null;
    }

    private static string? ExtractStorefront(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Length == 2 && segments[0].All(char.IsLetter))
        {
            return segments[0].ToLowerInvariant();
        }
        return null;
    }

    private static bool HasKnownArtist(AnalyzedTrack track) =>
        !string.IsNullOrWhiteSpace(track.Artist) &&
        !track.Artist.Trim().Equals(UnknownArtistLabel, StringComparison.OrdinalIgnoreCase);

    private static string? CleanAppleMusicTitle(string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return null;

        string decoded = WebUtility.HtmlDecode(rawTitle)
            .Replace('\u00A0', ' ')
            .Trim();

        decoded = Regex.Replace(decoded, @"\s+on\s+Apple\s+Music\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }

    private static string? ExtractMeta(string html, string property)
    {
        var match = Regex.Match(html, $@"<meta\b[^>]*property=[""']{Regex.Escape(property)}[""'][^>]*content=[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }
}
