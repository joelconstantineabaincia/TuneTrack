namespace TuneTrack.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using TuneTrack.Models;

public record TrackSuggestion(
    string Title,
    string Artist,
    string? Genre,
    string? ArtworkUrl,
    string? PreviewUrl,
    string MatchReason
);

public interface IRecommendationEngine
{
    Task<List<TrackSuggestion>> GenerateRecommendationsAsync(
        IReadOnlyList<AnalyzedTrack> existingTracks,
        int count = 8,
        CancellationToken cancellationToken = default);
}

public class RecommendationEngine : IRecommendationEngine
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RecommendationEngine> _logger;

    public RecommendationEngine(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<RecommendationEngine> logger)
    {
        _httpClient = httpClientFactory.CreateClient("MusicApi");
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<TrackSuggestion>> GenerateRecommendationsAsync(
        IReadOnlyList<AnalyzedTrack> existingTracks,
        int count = 8,
        CancellationToken cancellationToken = default)
    {
        var existingArtists = existingTracks
            .Select(t => t.Artist.Trim().ToLowerInvariant())
            .Where(a => !string.IsNullOrEmpty(a))
            .ToHashSet();

        var existingTitles = existingTracks
            .Select(t => t.Title.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToHashSet();

        // Detect top genres
        var topGenres = existingTracks
            .Select(t => t.Genre?.Trim())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .GroupBy(g => g!)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(3)
            .ToList();

        if (topGenres.Count == 0)
        {
            topGenres = new List<string> { "Indie", "Alternative", "Electronic" };
        }

        var suggestions = new List<TrackSuggestion>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var genre in topGenres)
        {
            if (suggestions.Count >= count)
                break;

            var candidates = await FetchTracksForGenreAsync(genre, cancellationToken);
            foreach (var candidate in candidates)
            {
                if (suggestions.Count >= count)
                    break;

                string artistNorm = candidate.Artist.Trim().ToLowerInvariant();
                string titleNorm = candidate.Title.Trim().ToLowerInvariant();
                string dedupeKey = $"{artistNorm} - {titleNorm}";

                // Filter out existing artists & existing titles to enhance diversity
                if (existingArtists.Contains(artistNorm) ||
                    existingTitles.Contains(titleNorm) ||
                    seenKeys.Contains(dedupeKey))
                {
                    continue;
                }

                seenKeys.Add(dedupeKey);
                suggestions.Add(candidate with
                {
                    MatchReason = $"Expands your {genre} sonic palette with fresh artist variety"
                });
            }
        }

        // If we still need more tracks, add complementary tracks from key genre blends
        if (suggestions.Count < count)
        {
            var fallbackGenre = "Alternative";
            var more = await FetchTracksForGenreAsync(fallbackGenre, cancellationToken);
            foreach (var candidate in more)
            {
                if (suggestions.Count >= count)
                    break;

                string artistNorm = candidate.Artist.Trim().ToLowerInvariant();
                string titleNorm = candidate.Title.Trim().ToLowerInvariant();
                string dedupeKey = $"{artistNorm} - {titleNorm}";

                if (!existingArtists.Contains(artistNorm) && !seenKeys.Contains(dedupeKey))
                {
                    seenKeys.Add(dedupeKey);
                    suggestions.Add(candidate with
                    {
                        MatchReason = "Introduces complementary crossover sound"
                    });
                }
            }
        }

        return suggestions;
    }

    private async Task<List<TrackSuggestion>> FetchTracksForGenreAsync(string genre, CancellationToken cancellationToken)
    {
        string cacheKey = $"recs_genre_{genre.ToLowerInvariant().Trim()}";
        if (_cache.TryGetValue(cacheKey, out List<TrackSuggestion>? cached) && cached != null)
        {
            return cached;
        }

        var results = new List<TrackSuggestion>();
        try
        {
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(genre)}&entity=song&limit=25";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "TuneTrack/1.0 (Educational Music Analytics)");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(6));

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var searchResp = JsonSerializer.Deserialize<ITunesSearchResponse>(json);

                if (searchResp?.Results != null)
                {
                    foreach (var item in searchResp.Results)
                    {
                        if (string.IsNullOrWhiteSpace(item.TrackName) || string.IsNullOrWhiteSpace(item.ArtistName))
                            continue;

                        string? artwork = item.ArtworkUrl100;
                        if (!string.IsNullOrEmpty(artwork) && artwork.Contains("100x100bb.jpg"))
                        {
                            artwork = artwork.Replace("100x100bb.jpg", "600x600bb.jpg");
                        }

                        results.Add(new TrackSuggestion(
                            Title: item.TrackName,
                            Artist: item.ArtistName,
                            Genre: item.PrimaryGenreName ?? genre,
                            ArtworkUrl: artwork,
                            PreviewUrl: item.PreviewUrl,
                            MatchReason: $"Matches {genre} vibe"
                        ));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch recommendations from iTunes for genre {Genre}", genre);
        }

        _cache.Set(cacheKey, results, TimeSpan.FromHours(12));
        return results;
    }

    private class ITunesSearchResponse
    {
        [JsonPropertyName("results")]
        public List<ITunesItem>? Results { get; set; }
    }

    private class ITunesItem
    {
        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("primaryGenreName")]
        public string? PrimaryGenreName { get; set; }

        [JsonPropertyName("artworkUrl100")]
        public string? ArtworkUrl100 { get; set; }

        [JsonPropertyName("previewUrl")]
        public string? PreviewUrl { get; set; }
    }
}
