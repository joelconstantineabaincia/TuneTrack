namespace TuneTrack.Services.Recommendations;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using TuneTrack.Models;

public record TrackEnrichmentResult(
    string? Genre,
    int? ReleaseYear,
    string? ArtworkUrl,
    string? PreviewUrl,
    string? Album
);

public interface ITrackEnricherService
{
    Task<TrackEnrichmentResult?> LookupTrackAsync(string artist, string title, CancellationToken cancellationToken = default);
    Task EnrichTracksAsync(IList<AnalyzedTrack> tracks, int maxToEnrich = 25, CancellationToken cancellationToken = default);
}

public class TrackEnricherService : ITrackEnricherService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TrackEnricherService> _logger;
    private readonly SemaphoreSlim _throttle = new(2, 2);

    public TrackEnricherService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<TrackEnricherService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("MusicApi");
        _cache = cache;
        _logger = logger;
    }

    public async Task<TrackEnrichmentResult?> LookupTrackAsync(string artist, string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
            return null;

        string cacheKey = $"itunes_{artist.Trim().ToLowerInvariant()}_{title.Trim().ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out TrackEnrichmentResult? cached))
        {
            return cached;
        }

        await _throttle.WaitAsync(cancellationToken);
        try
        {
            // Clean title (remove feats, parentheticals, etc.) for higher search hit rate if needed
            string cleanTitle = CleanTrackTitle(title);
            string query = Uri.EscapeDataString($"{artist} {cleanTitle}");
            string url = $"https://itunes.apple.com/search?term={query}&entity=song&limit=5";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "TuneTrack/1.0 (Educational Music Analytics)");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(6));

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("iTunes Search API returned status {StatusCode} for query: {Query}", response.StatusCode, query);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var itunesResponse = JsonSerializer.Deserialize<ITunesSearchResponse>(json);
            var item = itunesResponse?.Results?.FirstOrDefault(candidate => IsArtistMatch(artist, candidate.ArtistName));

            if (item == null)
            {
                // Cache empty result for 1 hour to avoid repeated failed queries
                _cache.Set(cacheKey, (TrackEnrichmentResult?)null, TimeSpan.FromHours(1));
                return null;
            }

            int? releaseYear = null;
            if (DateTime.TryParse(item.ReleaseDate, out var releaseDate))
            {
                releaseYear = releaseDate.Year;
            }

            // Upgrade artwork from 100x100 to 600x600 if standard mzstatic URL
            string? artwork = item.ArtworkUrl100;
            if (!string.IsNullOrEmpty(artwork) && artwork.Contains("100x100bb.jpg"))
            {
                artwork = artwork.Replace("100x100bb.jpg", "600x600bb.jpg");
            }

            var result = new TrackEnrichmentResult(
                Genre: NormalizeGenre(item.PrimaryGenreName),
                ReleaseYear: releaseYear,
                ArtworkUrl: artwork,
                PreviewUrl: item.PreviewUrl,
                Album: item.CollectionName
            );

            _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("iTunes search timed out for: {Artist} - {Title}", artist, title);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich track from iTunes: {Artist} - {Title}", artist, title);
            return null;
        }
        finally
        {
            // Small polite delay between iTunes queries to respect ~20 req/min rate limit
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            _throttle.Release();
        }
    }

    public async Task EnrichTracksAsync(IList<AnalyzedTrack> tracks, int maxToEnrich = 25, CancellationToken cancellationToken = default)
    {
        if (tracks == null || tracks.Count == 0)
            return;

        // Take up to maxToEnrich tracks to respect rate limits
        var targetTracks = tracks.Take(maxToEnrich).ToList();

        var tasks = targetTracks.Select(async track =>
        {
            // Skip if track already has full metadata
            if (!string.IsNullOrEmpty(track.Genre) && track.ReleaseYear.HasValue && !string.IsNullOrEmpty(track.ArtworkUrl))
                return;

            var enrichment = await LookupTrackAsync(track.Artist, track.Title, cancellationToken);
            if (enrichment != null)
            {
                if (string.IsNullOrEmpty(track.Genre) && !string.IsNullOrEmpty(enrichment.Genre))
                    track.Genre = NormalizeGenre(enrichment.Genre);

                if (!track.ReleaseYear.HasValue && enrichment.ReleaseYear.HasValue)
                    track.ReleaseYear = enrichment.ReleaseYear;

                if (string.IsNullOrEmpty(track.ArtworkUrl) && !string.IsNullOrEmpty(enrichment.ArtworkUrl))
                    track.ArtworkUrl = enrichment.ArtworkUrl;

                if (string.IsNullOrEmpty(track.PreviewUrl) && !string.IsNullOrEmpty(enrichment.PreviewUrl))
                    track.PreviewUrl = enrichment.PreviewUrl;

                if (string.IsNullOrEmpty(track.Album) && !string.IsNullOrEmpty(enrichment.Album))
                    track.Album = enrichment.Album;
            }
            else if (!string.IsNullOrWhiteSpace(track.Genre))
            {
                track.Genre = NormalizeGenre(track.Genre);
            }
        });

        await Task.WhenAll(tasks);
    }

    private static readonly Dictionary<string, string> GenreNormalizationMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hip-Hop"] = "Hip-Hop/Rap",
        ["Hip Hop"] = "Hip-Hop/Rap",
        ["Hip-Hop/Rap"] = "Hip-Hop/Rap",
        ["Hip Hop/Rap"] = "Hip-Hop/Rap",
        ["Rap"] = "Hip-Hop/Rap",
        ["R&B"] = "R&B/Soul",
        ["R&B/Soul"] = "R&B/Soul",
        ["Soul"] = "R&B/Soul",
        ["Electronica"] = "Electronic",
        ["Alt-Rock"] = "Alternative",
        ["Alternative Rock"] = "Alternative",
        ["Indie Rock"] = "Alternative"
    };

    public static string? NormalizeGenre(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
            return null;

        string trimmed = genre.Trim();
        return GenreNormalizationMap.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    internal static bool IsArtistMatch(string? expectedArtist, string? candidateArtist)
    {
        string expected = NormalizeArtistForMatch(expectedArtist);
        string candidate = NormalizeArtistForMatch(candidateArtist);

        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(candidate) ||
            expected == "various artists")
        {
            return false;
        }

        if (expected.Contains(candidate, StringComparison.Ordinal) ||
            candidate.Contains(expected, StringComparison.Ordinal))
        {
            return true;
        }

        string primaryExpected = ExtractPrimaryArtist(expected);
        string primaryCandidate = ExtractPrimaryArtist(candidate);
        return primaryExpected.Length >= 2 && primaryCandidate.Length >= 2 &&
               (primaryExpected.Contains(primaryCandidate, StringComparison.Ordinal) ||
                primaryCandidate.Contains(primaryExpected, StringComparison.Ordinal));
    }

    private static string NormalizeArtistForMatch(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return string.Empty;

        string cleaned = artist.Trim().ToLowerInvariant();
        // Strip featured guest suffixes so "Artist ft. Guest" compares cleanly against "Artist"
        int featIdx = System.Text.RegularExpressions.Regex.Match(cleaned, @"\s*(?:\(|\[)?\b(?:feat\.?|ft\.?|featuring)\b").Index;
        if (featIdx > 0 && System.Text.RegularExpressions.Regex.IsMatch(cleaned, @"\s*(?:\(|\[)?\b(?:feat\.?|ft\.?|featuring)\b"))
        {
            cleaned = cleaned[..featIdx];
        }

        return System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    private static string ExtractPrimaryArtist(string normalizedArtist)
    {
        int sepIdx = System.Text.RegularExpressions.Regex.Match(normalizedArtist, @"\s*(?:&|,|\band\b|\bwith\b|\bx\b)\s*").Index;
        if (sepIdx > 0 && System.Text.RegularExpressions.Regex.IsMatch(normalizedArtist, @"\s*(?:&|,|\band\b|\bwith\b|\bx\b)\s*"))
        {
            return normalizedArtist[..sepIdx].Trim();
        }
        return normalizedArtist;
    }

    private static string CleanTrackTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // Strip "feat.", "ft.", "(feat ...)", "(with ...)"
        int featIdx = title.IndexOf(" feat", StringComparison.OrdinalIgnoreCase);
        if (featIdx > 0)
            title = title[..featIdx];

        int ftIdx = title.IndexOf(" ft.", StringComparison.OrdinalIgnoreCase);
        if (ftIdx > 0)
            title = title[..ftIdx];

        int parenIdx = title.IndexOf('(');
        if (parenIdx > 0 && (title.Contains("feat", StringComparison.OrdinalIgnoreCase) || title.Contains("with", StringComparison.OrdinalIgnoreCase) || title.Contains("remaster", StringComparison.OrdinalIgnoreCase) || title.Contains("official", StringComparison.OrdinalIgnoreCase)))
        {
            title = title[..parenIdx];
        }

        int bracketIdx = title.IndexOf('[');
        if (bracketIdx > 0 && title.Contains("official", StringComparison.OrdinalIgnoreCase))
        {
            title = title[..bracketIdx];
        }

        return title.Trim();
    }

    private class ITunesSearchResponse
    {
        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }

        [JsonPropertyName("results")]
        public List<ITunesTrackItem>? Results { get; set; }
    }

    private class ITunesTrackItem
    {
        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("collectionName")]
        public string? CollectionName { get; set; }

        [JsonPropertyName("primaryGenreName")]
        public string? PrimaryGenreName { get; set; }

        [JsonPropertyName("releaseDate")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("artworkUrl100")]
        public string? ArtworkUrl100 { get; set; }

        [JsonPropertyName("previewUrl")]
        public string? PreviewUrl { get; set; }
    }
}
