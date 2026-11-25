namespace TuneTrack.Services.Recommendations;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using TuneTrack.Models;
using TuneTrack.Services.Analysis;

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
    private static readonly Dictionary<string, string[]> AdjacentGenreMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Synth-Pop"] = new[] { "New Wave", "Dream Pop", "Electronic", "Indie Pop", "Alternative" },
        ["Pop"] = new[] { "Indie Pop", "Synth-Pop", "R&B", "Funk", "Electronic" },
        ["Rock"] = new[] { "Indie Rock", "Alternative", "Post-Punk", "Classic Rock", "Blues Rock" },
        ["Progressive Rock"] = new[] { "Art Rock", "Jazz Fusion", "Psychedelic Rock", "Ambient", "Electronic" },
        ["Classic Rock"] = new[] { "Blues Rock", "Folk Rock", "Soul", "Indie Rock", "Funk" },
        ["Alternative"] = new[] { "Indie Rock", "Post-Punk", "Dream Pop", "Electronic", "Indie Folk" },
        ["Indie Rock"] = new[] { "Post-Punk", "Dream Pop", "Garage Rock", "Indie Folk", "Alternative" },
        ["Indie Pop"] = new[] { "Dream Pop", "Synth-Pop", "Jangle Pop", "Indie Folk", "Electronic" },
        ["Indie"] = new[] { "Dream Pop", "Post-Punk", "Indie Folk", "Alternative", "Electronic" },
        ["Hip-Hop"] = new[] { "R&B", "Neo-Soul", "Jazz", "Funk", "Electronic" },
        ["Electronic"] = new[] { "Synthwave", "Ambient", "House", "Trip-Hop", "Indie Pop" },
        ["Synthwave"] = new[] { "Synth-Pop", "Electronic", "New Wave", "Ambient", "Indie Pop" },
        ["Jazz"] = new[] { "Bossa Nova", "Neo-Soul", "Funk", "Ambient", "Blues" },
        ["Funk"] = new[] { "Soul", "Disco", "Jazz", "R&B", "Afrobeat" },
        ["R&B"] = new[] { "Neo-Soul", "Funk", "Hip-Hop", "Jazz", "Pop" }
    };

    private static readonly string[] DefaultExploreGenres = { "New Wave", "Dream Pop", "Electronic", "Indie Pop", "Alternative", "Jazz" };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RecommendationEngine> _logger;
    private readonly IDiversityScorer _scorer;

    public RecommendationEngine(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<RecommendationEngine> logger,
        IDiversityScorer? scorer = null)
    {
        _httpClient = httpClientFactory.CreateClient("MusicApi");
        _cache = cache;
        _logger = logger;
        _scorer = scorer ?? new DiversityScorer();
    }

    public async Task<List<TrackSuggestion>> GenerateRecommendationsAsync(
        IReadOnlyList<AnalyzedTrack> existingTracks,
        int count = 8,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            return new List<TrackSuggestion>();

        var existingArtists = existingTracks
            .Select(t => t.Artist.Trim().ToLowerInvariant())
            .Where(a => !string.IsNullOrEmpty(a))
            .ToHashSet();

        var existingTitles = existingTracks
            .Select(t => t.Title.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToHashSet();

        var existingGenreSet = existingTracks
            .Select(t => t.Genre?.Trim())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var topGenres = existingTracks
            .Select(t => t.Genre?.Trim())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .GroupBy(g => g!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(5)
            .ToList();

        if (topGenres.Count == 0)
        {
            topGenres = new List<string> { "Indie", "Alternative", "Electronic", "Jazz" };
        }

        var suggestions = new List<TrackSuggestion>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var diversity = _scorer.CalculateScore(existingTracks);
        bool isLowDiversity = existingTracks.Count > 0 && diversity.TotalScore < 40.0;

        if (isLowDiversity)
        {
            var adjacentGenres = ResolveAdjacentGenres(topGenres, existingGenreSet);
            int adjacentTarget = (int)Math.Ceiling(count / 2.0);

            await SampleRoundRobinAsync(
                adjacentGenres,
                suggestions,
                targetTotal: Math.Min(count, adjacentTarget),
                existingArtists,
                existingTitles,
                seenKeys,
                genre => $"Broadens your mix into {genre}",
                cancellationToken);

            await SampleRoundRobinAsync(
                topGenres,
                suggestions,
                targetTotal: count,
                existingArtists,
                existingTitles,
                seenKeys,
                genre => $"Matches {genre} with fresh artists",
                cancellationToken);

            // Top up from remaining adjacent genres if primary genre ran out of fresh artists
            if (suggestions.Count < count)
            {
                await SampleRoundRobinAsync(
                    adjacentGenres,
                    suggestions,
                    targetTotal: count,
                    existingArtists,
                    existingTitles,
                    seenKeys,
                    genre => $"Broadens your mix into {genre}",
                    cancellationToken);
            }
        }
        else
        {
            await SampleRoundRobinAsync(
                topGenres,
                suggestions,
                targetTotal: count,
                existingArtists,
                existingTitles,
                seenKeys,
                genre => $"Expands your {genre} palette with new artists",
                cancellationToken);

            if (suggestions.Count < count)
            {
                var fallbackGenres = ResolveAdjacentGenres(topGenres, existingGenreSet);
                await SampleRoundRobinAsync(
                    fallbackGenres,
                    suggestions,
                    targetTotal: count,
                    existingArtists,
                    existingTitles,
                    seenKeys,
                    genre => $"Introduces complementary {genre} sound",
                    cancellationToken);
            }
        }

        return suggestions;
    }

    private static List<string> ResolveAdjacentGenres(IReadOnlyList<string> sourceGenres, HashSet<string> existingGenreSet)
    {
        var adjacent = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in sourceGenres)
        {
            if (AdjacentGenreMap.TryGetValue(g, out var mapped))
            {
                foreach (var candidate in mapped)
                {
                    if (!existingGenreSet.Contains(candidate) && seen.Add(candidate))
                    {
                        adjacent.Add(candidate);
                    }
                }
            }
        }

        foreach (var candidate in DefaultExploreGenres)
        {
            if (!existingGenreSet.Contains(candidate) && seen.Add(candidate))
            {
                adjacent.Add(candidate);
            }
        }

        return adjacent.Take(5).ToList();
    }

    private async Task SampleRoundRobinAsync(
        IReadOnlyList<string> genres,
        List<TrackSuggestion> suggestions,
        int targetTotal,
        HashSet<string> existingArtists,
        HashSet<string> existingTitles,
        HashSet<string> seenKeys,
        Func<string, string> reasonFactory,
        CancellationToken cancellationToken)
    {
        if (genres.Count == 0 || suggestions.Count >= targetTotal)
            return;

        var fetchedLists = await Task.WhenAll(
            genres.Select(async g => (Genre: g, Items: await FetchTracksForGenreAsync(g, cancellationToken))));

        var pools = fetchedLists
            .Select(x => (x.Genre, x.Items, Index: 0))
            .ToList();

        bool addedAny = true;
        while (suggestions.Count < targetTotal && addedAny)
        {
            addedAny = false;
            for (int p = 0; p < pools.Count && suggestions.Count < targetTotal; p++)
            {
                var (genre, items, idx) = pools[p];
                while (idx < items.Count)
                {
                    var candidate = items[idx++];
                    if (IsEligibleCandidate(candidate, existingArtists, existingTitles, seenKeys))
                    {
                        suggestions.Add(candidate with { MatchReason = reasonFactory(genre) });
                        addedAny = true;
                        break;
                    }
                }
                pools[p] = (genre, items, idx);
            }
        }
    }

    private static bool IsEligibleCandidate(
        TrackSuggestion candidate,
        HashSet<string> existingArtists,
        HashSet<string> existingTitles,
        HashSet<string> seenKeys)
    {
        if (string.IsNullOrWhiteSpace(candidate.Artist) || string.IsNullOrWhiteSpace(candidate.Title))
            return false;

        string artistNorm = candidate.Artist.Trim().ToLowerInvariant();
        string titleNorm = candidate.Title.Trim().ToLowerInvariant();
        string dedupeKey = $"{artistNorm} - {titleNorm}";

        if (existingArtists.Contains(artistNorm) || existingTitles.Contains(titleNorm))
            return false;

        return seenKeys.Add(dedupeKey);
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
