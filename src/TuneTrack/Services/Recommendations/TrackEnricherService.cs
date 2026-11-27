namespace TuneTrack.Services.Recommendations;

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using TuneTrack.Models;

public record TrackEnrichmentResult(
    string? Genre,
    int? ReleaseYear,
    string? ArtworkUrl,
    string? PreviewUrl,
    string? Album
);

public record TrackLookupItem(
    string TrackId,
    string? Title,
    string? Artist,
    int? DurationMs,
    TrackEnrichmentResult Metadata
);

public interface ITrackEnricherService
{
    Task<TrackEnrichmentResult?> LookupTrackAsync(string artist, string title, CancellationToken cancellationToken = default);
    Task EnrichTracksAsync(IList<AnalyzedTrack> tracks, int maxToEnrich = 100, CancellationToken cancellationToken = default);
    Task EnrichTracksAsync(
        IList<AnalyzedTrack> tracks,
        IProgress<(int Completed, int Total)>? progress,
        int maxToEnrich = 100,
        CancellationToken cancellationToken = default) =>
        EnrichTracksAsync(tracks, maxToEnrich, cancellationToken);
    Task<IReadOnlyDictionary<string, TrackLookupItem>> LookupTracksByIdsAsync(
        IEnumerable<string> trackIds,
        string? country,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, TrackLookupItem>>(new Dictionary<string, TrackLookupItem>());
}

public class TrackEnricherService : ITrackEnricherService
{
    private const int MaxSearchAttempts = 3;
    private const int LookupBatchSize = 50;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(750), TimeSpan.FromMilliseconds(1500)];
    private static readonly TimeSpan PoliteThrottleDelay = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan ElevatedThrottleDelay = TimeSpan.FromMilliseconds(1100);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TrackEnricherService> _logger;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTimeOffset _elevatedThrottleUntil = DateTimeOffset.MinValue;

    private enum MatchFailureReason
    {
        None,
        TransientRateLimit,
        TransientTimeout,
        ZeroResults,
        ArtistMismatch,
        HttpError
    }

    private sealed record LookupOutcome(TrackEnrichmentResult? Result, MatchFailureReason Reason, string? Detail = null);
    private sealed record SearchAttemptResult(TrackEnrichmentResult? Match, bool SawCandidates, MatchFailureReason FailureReason, string? Detail = null);

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
        var outcome = await LookupTrackInternalAsync(artist, title, cancellationToken);
        return outcome.Result;
    }

    private async Task<LookupOutcome> LookupTrackInternalAsync(string artist, string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
            return new LookupOutcome(null, MatchFailureReason.ZeroResults);

        if (artist.Trim().Equals("Various Artists", StringComparison.OrdinalIgnoreCase))
            return new LookupOutcome(null, MatchFailureReason.ArtistMismatch, "Placeholder artist");

        string cacheKey = $"itunes_{artist.Trim().ToLowerInvariant()}_{title.Trim().ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out TrackEnrichmentResult? cached))
        {
            return new LookupOutcome(cached, cached != null ? MatchFailureReason.None : MatchFailureReason.ZeroResults, "cache");
        }

        string cleanTitle = CleanTrackTitle(title);
        if (string.IsNullOrWhiteSpace(cleanTitle))
            cleanTitle = title.Trim();

        var passes = new List<(string Term, int Limit)>();
        string primaryQuery = string.IsNullOrWhiteSpace(artist) ? cleanTitle : $"{artist.Trim()} {cleanTitle}";
        passes.Add((primaryQuery, 5));

        string firstArtist = ExtractFirstListedArtist(artist);
        if (!string.IsNullOrWhiteSpace(firstArtist) &&
            !firstArtist.Equals(artist.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            passes.Add(($"{firstArtist} {cleanTitle}", 5));
        }

        if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(cleanTitle))
        {
            passes.Add((cleanTitle, 10));
        }

        string phArtist = !string.IsNullOrWhiteSpace(firstArtist) ? firstArtist : artist.Trim();
        string phQuery = string.IsNullOrWhiteSpace(phArtist) ? cleanTitle : $"{phArtist} {cleanTitle}";

        bool sawAnyCandidates = false;
        MatchFailureReason lastFailure = MatchFailureReason.ZeroResults;
        string? lastDetail = null;

        (string? Country, IReadOnlyList<(string Term, int Limit)> Passes)[] storefronts =
        [
            (null, passes),
            ("PH", [(phQuery, 10)])
        ];

        foreach (var (country, storefrontPasses) in storefronts)
        {
            foreach (var (term, limit) in storefrontPasses)
            {
                var attempt = await SearchWithRetryAsync(artist, term, limit, country, cancellationToken);
                if (attempt.Match != null)
                {
                    _cache.Set(cacheKey, attempt.Match, TimeSpan.FromHours(24));
                    return new LookupOutcome(attempt.Match, MatchFailureReason.None);
                }

                if (attempt.FailureReason is MatchFailureReason.TransientRateLimit or MatchFailureReason.TransientTimeout)
                {
                    // Transient errors must not poison the negative cache or fire redundant fallback passes
                    return new LookupOutcome(null, attempt.FailureReason, attempt.Detail);
                }

                if (attempt.FailureReason == MatchFailureReason.HttpError)
                {
                    lastFailure = MatchFailureReason.HttpError;
                    lastDetail = attempt.Detail;
                    continue;
                }

                if (attempt.SawCandidates)
                {
                    sawAnyCandidates = true;
                    lastDetail = attempt.Detail;
                }
            }
        }

        var finalReason = sawAnyCandidates
            ? MatchFailureReason.ArtistMismatch
            : lastFailure == MatchFailureReason.HttpError ? MatchFailureReason.HttpError : MatchFailureReason.ZeroResults;

        if (finalReason != MatchFailureReason.HttpError)
        {
            _cache.Set(cacheKey, (TrackEnrichmentResult?)null, TimeSpan.FromHours(1));
        }

        return new LookupOutcome(null, finalReason, lastDetail);
    }

    public async Task<IReadOnlyDictionary<string, TrackLookupItem>> LookupTracksByIdsAsync(
        IEnumerable<string> trackIds,
        string? country,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var orderedIds = trackIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => id.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new List<string>();

        var resolved = new Dictionary<string, TrackLookupItem>(StringComparer.Ordinal);
        if (orderedIds.Count == 0)
            return resolved;

        string normalizedCountry = string.IsNullOrWhiteSpace(country) ? string.Empty : country.Trim().ToLowerInvariant();
        string countryParam = string.IsNullOrEmpty(normalizedCountry) ? string.Empty : $"&country={Uri.EscapeDataString(normalizedCountry)}";

        var uncachedIds = new List<string>();
        foreach (string id in orderedIds)
        {
            if (_cache.TryGetValue(GetIdCacheKey(normalizedCountry, id), out TrackLookupItem? cached) && cached != null)
            {
                resolved[id] = cached;
            }
            else
            {
                uncachedIds.Add(id);
            }
        }

        int total = orderedIds.Count;
        int completed = resolved.Count;
        progress?.Report((completed, total));

        foreach (var batch in uncachedIds.Chunk(LookupBatchSize))
        {
            string joinedIds = string.Join(",", batch);
            string url = $"https://itunes.apple.com/lookup?id={Uri.EscapeDataString(joinedIds)}&entity=song{countryParam}";
            var response = await FetchITunesJsonWithRetryAsync(url, $"lookup:{batch.Length}:{normalizedCountry}", cancellationToken);

            if (response.Results != null)
            {
                foreach (var item in response.Results)
                {
                    if (!item.TrackId.HasValue)
                        continue;

                    string trackId = item.TrackId.Value.ToString();
                    var lookupItem = new TrackLookupItem(
                        TrackId: trackId,
                        Title: item.TrackName,
                        Artist: item.ArtistName,
                        DurationMs: item.TrackTimeMillis is > 0 ? item.TrackTimeMillis : null,
                        Metadata: ToEnrichmentResult(item));

                    resolved[trackId] = lookupItem;
                    _cache.Set(GetIdCacheKey(normalizedCountry, trackId), lookupItem, TimeSpan.FromHours(24));
                }
            }

            completed = Math.Min(total, completed + batch.Length);
            progress?.Report((completed, total));
        }

        return resolved;
    }

    private static string GetIdCacheKey(string normalizedCountry, string trackId) =>
        $"itunes_id_{normalizedCountry}_{trackId}";

    private async Task<SearchAttemptResult> SearchWithRetryAsync(
        string expectedArtist,
        string term,
        int limit,
        string? country,
        CancellationToken cancellationToken)
    {
        string escapedTerm = Uri.EscapeDataString(term);
        string countryParam = string.IsNullOrWhiteSpace(country) ? string.Empty : $"&country={Uri.EscapeDataString(country)}";
        string url = $"https://itunes.apple.com/search?term={escapedTerm}&entity=song&limit={limit}{countryParam}";

        var fetch = await FetchITunesJsonWithRetryAsync(url, term, cancellationToken);
        if (fetch.FailureReason != MatchFailureReason.None)
        {
            return new SearchAttemptResult(null, false, fetch.FailureReason, fetch.Detail);
        }

        var results = fetch.Results;
        if (results == null || results.Count == 0)
        {
            return new SearchAttemptResult(null, false, MatchFailureReason.ZeroResults);
        }

        var item = results.FirstOrDefault(candidate => IsArtistMatch(expectedArtist, candidate.ArtistName));
        if (item == null)
        {
            string topCandidate = $"{results[0].ArtistName} - {results[0].TrackName}";
            return new SearchAttemptResult(null, true, MatchFailureReason.ArtistMismatch, topCandidate);
        }

        return new SearchAttemptResult(ToEnrichmentResult(item), true, MatchFailureReason.None);
    }

    private async Task<(List<ITunesTrackItem>? Results, MatchFailureReason FailureReason, string? Detail)> FetchITunesJsonWithRetryAsync(
        string url,
        string logLabel,
        CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            for (int attempt = 0; attempt < MaxSearchAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("User-Agent", "TuneTrack/1.0 (Educational Music Analytics)");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(6));

                    using var response = await _httpClient.SendAsync(request, cts.Token);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                        response.StatusCode == HttpStatusCode.Forbidden ||
                        (int)response.StatusCode >= 500)
                    {
                        bool isRateLimit = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden;
                        if (isRateLimit)
                        {
                            // Slow down subsequent calls briefly so we stop draining Apple's token bucket
                            _elevatedThrottleUntil = DateTimeOffset.UtcNow.AddSeconds(45);
                        }
                        if (attempt < MaxSearchAttempts - 1)
                        {
                            var delay = GetRetryDelay(response, attempt);
                            _logger.LogDebug("iTunes returned {StatusCode} for '{Label}', backing off {DelayMs}ms (attempt {Attempt}/{Max})",
                                (int)response.StatusCode, logLabel, (int)delay.TotalMilliseconds, attempt + 1, MaxSearchAttempts);
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        _logger.LogWarning("iTunes returned {StatusCode} after {Max} attempts for '{Label}'",
                            (int)response.StatusCode, MaxSearchAttempts, logLabel);
                        return (null,
                            isRateLimit ? MatchFailureReason.TransientRateLimit : MatchFailureReason.HttpError,
                            $"HTTP {(int)response.StatusCode}");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("iTunes API returned status {StatusCode} for query: {Label}", response.StatusCode, logLabel);
                        return (null, MatchFailureReason.HttpError, $"HTTP {(int)response.StatusCode}");
                    }

                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    var itunesResponse = JsonSerializer.Deserialize<ITunesSearchResponse>(json);
                    return (itunesResponse?.Results, MatchFailureReason.None, null);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt < MaxSearchAttempts - 1)
                    {
                        var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                        _logger.LogDebug("iTunes query timed out for '{Label}', retrying in {DelayMs}ms (attempt {Attempt}/{Max})",
                            logLabel, (int)delay.TotalMilliseconds, attempt + 1, MaxSearchAttempts);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    _logger.LogWarning("iTunes query timed out after {Max} attempts for '{Label}'", MaxSearchAttempts, logLabel);
                    return (null, MatchFailureReason.TransientTimeout, "Timeout");
                }
                catch (HttpRequestException ex) when (attempt < MaxSearchAttempts - 1)
                {
                    var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                    _logger.LogDebug(ex, "iTunes HTTP error for '{Label}', retrying in {DelayMs}ms", logLabel, (int)delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query iTunes for: {Label}", logLabel);
                    return (null, MatchFailureReason.HttpError, ex.Message);
                }
            }

            return (null, MatchFailureReason.TransientRateLimit, null);
        }
        finally
        {
            try
            {
                // Respect iTunes rate limits, stepping up spacing briefly after a 429/403
                var delay = DateTimeOffset.UtcNow < _elevatedThrottleUntil ? ElevatedThrottleDelay : PoliteThrottleDelay;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _throttle.Release();
            }
        }
    }

    private static TrackEnrichmentResult ToEnrichmentResult(ITunesTrackItem item)
    {
        int? releaseYear = null;
        if (DateTime.TryParse(item.ReleaseDate, out var releaseDate))
        {
            releaseYear = releaseDate.Year;
        }

        string? artwork = item.ArtworkUrl100;
        if (!string.IsNullOrEmpty(artwork) && artwork.Contains("100x100bb.jpg"))
        {
            artwork = artwork.Replace("100x100bb.jpg", "600x600bb.jpg");
        }

        return new TrackEnrichmentResult(
            Genre: NormalizeGenre(item.PrimaryGenreName),
            ReleaseYear: releaseYear,
            ArtworkUrl: artwork,
            PreviewUrl: item.PreviewUrl,
            Album: item.CollectionName
        );
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta <= TimeSpan.FromSeconds(10) ? delta : TimeSpan.FromSeconds(10);
        }

        return RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
    }

    public Task EnrichTracksAsync(IList<AnalyzedTrack> tracks, int maxToEnrich = 100, CancellationToken cancellationToken = default) =>
        EnrichTracksAsync(tracks, progress: null, maxToEnrich, cancellationToken);

    public async Task EnrichTracksAsync(
        IList<AnalyzedTrack> tracks,
        IProgress<(int Completed, int Total)>? progress,
        int maxToEnrich = 100,
        CancellationToken cancellationToken = default)
    {
        if (tracks == null || tracks.Count == 0)
            return;

        var targetTracks = tracks.Take(maxToEnrich).ToList();
        int total = targetTracks.Count;
        progress?.Report((0, total));

        int completed = 0;
        int matched = 0;
        int alreadyComplete = 0;
        int rateLimited = 0;
        int timedOut = 0;
        int zeroResults = 0;
        int artistMismatch = 0;
        int httpError = 0;

        var tasks = targetTracks.Select(async track =>
        {
            try
            {
                if (!string.IsNullOrEmpty(track.Genre) && track.ReleaseYear.HasValue && !string.IsNullOrEmpty(track.ArtworkUrl))
                {
                    Interlocked.Increment(ref alreadyComplete);
                    return;
                }

                var outcome = await LookupTrackInternalAsync(track.Artist, track.Title, cancellationToken);
                if (outcome.Result != null)
                {
                    var enrichment = outcome.Result;
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

                    Interlocked.Increment(ref matched);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(track.Genre))
                    {
                        track.Genre = NormalizeGenre(track.Genre);
                    }

                    switch (outcome.Reason)
                    {
                        case MatchFailureReason.TransientRateLimit:
                            Interlocked.Increment(ref rateLimited);
                            break;
                        case MatchFailureReason.TransientTimeout:
                            Interlocked.Increment(ref timedOut);
                            break;
                        case MatchFailureReason.ArtistMismatch:
                            Interlocked.Increment(ref artistMismatch);
                            break;
                        case MatchFailureReason.HttpError:
                            Interlocked.Increment(ref httpError);
                            break;
                        default:
                            Interlocked.Increment(ref zeroResults);
                            break;
                    }

                    _logger.LogInformation("iTunes unmatched: {Artist} - {Title} | Reason={Reason}{Detail}",
                        track.Artist,
                        track.Title,
                        outcome.Reason,
                        string.IsNullOrWhiteSpace(outcome.Detail) ? "" : $" ({outcome.Detail})");
                }
            }
            finally
            {
                int done = Interlocked.Increment(ref completed);
                progress?.Report((done, total));
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation(
            "iTunes enrichment completed ({Total} tracks): matched={Matched}, alreadyComplete={AlreadyComplete}, zeroResults={ZeroResults}, artistMismatch={ArtistMismatch}, http429={RateLimited}, timeout={TimedOut}, httpError={HttpError}",
            total, matched, alreadyComplete, zeroResults, artistMismatch, rateLimited, timedOut, httpError);
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

        var expectedList = SplitListedArtists(expected);
        var candidateList = SplitListedArtists(candidate);

        string primaryExpected = expectedList.FirstOrDefault() ?? expected;
        string primaryCandidate = candidateList.FirstOrDefault() ?? candidate;
        if (primaryExpected.Length >= 2 && primaryCandidate.Length >= 2 &&
            (primaryExpected.Contains(primaryCandidate, StringComparison.Ordinal) ||
             primaryCandidate.Contains(primaryExpected, StringComparison.Ordinal)))
        {
            return true;
        }

        // Handle multi-artist collaborations where Spotify and iTunes list co-primary artists in different order
        if (expectedList.Length >= 2 && candidateList.Length >= 2)
        {
            int sharedArtists = expectedList.Count(e =>
                candidateList.Any(c => e == c || (Math.Min(e.Length, c.Length) >= 4 &&
                    (e.Contains(c, StringComparison.Ordinal) || c.Contains(e, StringComparison.Ordinal)))));
            if (sharedArtists >= 2)
            {
                return true;
            }
        }

        return false;
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

        // Treat hyphens/dashes equivalently to spaces so "Flow-G" matches "Flow G"
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[-–—]+", " ");
        return System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    private static string[] SplitListedArtists(string normalizedArtist) =>
        System.Text.RegularExpressions.Regex.Split(normalizedArtist, @"\s*(?:,|&|/|\band\b|\bwith\b|\bx\b)\s*")
            .Select(a => a.Trim())
            .Where(a => a.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string ExtractFirstListedArtist(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return string.Empty;

        string baseArtist = NormalizeArtistForMatch(artist);
        var collabMatch = Regex.Match(artist.Trim(), @"\s*(?:,|\s&\s|\s/\s|\sx\s)\s*", RegexOptions.IgnoreCase);
        if (collabMatch.Success && collabMatch.Index > 0)
        {
            string first = artist.Trim()[..collabMatch.Index].Trim();
            int featIdx = Regex.Match(first, @"\s*(?:\(|\[)?\b(?:feat\.?|ft\.?|featuring)\b", RegexOptions.IgnoreCase).Index;
            if (featIdx > 0 && Regex.IsMatch(first, @"\s*(?:\(|\[)?\b(?:feat\.?|ft\.?|featuring)\b", RegexOptions.IgnoreCase))
            {
                first = first[..featIdx].Trim();
            }
            return first;
        }

        int featInFull = Regex.Match(artist.Trim(), @"\s*(?:\(|\[)?\b(?:feat\.?|ft\.?|featuring)\b", RegexOptions.IgnoreCase).Index;
        if (featInFull > 0 && Regex.IsMatch(artist.Trim(), @"\s*(?:\(|\[)?\b(?:feat\.?|ft\.?|featuring)\b", RegexOptions.IgnoreCase))
        {
            return artist.Trim()[..featInFull].Trim();
        }

        return baseArtist.Length > 0 ? artist.Trim() : string.Empty;
    }

    private static string CleanTrackTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        string cleaned = title;

        // Strip parenthetical/bracketed edition, live, feature, and official video tags without touching real titles
        cleaned = Regex.Replace(
            cleaned,
            @"\s*(?:\([^)]*\b(?:feat\.?|ft\.?|featuring|with|remaster(?:ed)?|live|single\s+version|album\s+version|radio\s+edit|acoustic|deluxe|bonus|explicit|mono|stereo|official|spotify\s+singles|version|edit|mix|demo|instrumental)\b[^)]*\)|\[[^\]]*\b(?:feat\.?|ft\.?|featuring|with|remaster(?:ed)?|live|single\s+version|album\s+version|radio\s+edit|acoustic|deluxe|bonus|explicit|mono|stereo|official|spotify\s+singles|version|edit|mix|demo|instrumental)\b[^\]]*\])",
            string.Empty,
            RegexOptions.IgnoreCase);

        // Strip trailing dash suffixes such as "- Remastered 2011", "- Single Version", "- Live at ...", "- Spotify Singles"
        cleaned = Regex.Replace(
            cleaned,
            @"\s+[-–—]\s*(?:\d{4}\s+remaster(?:ed)?|remaster(?:ed)?(?:\s+\d{4})?|single\s+version|album\s+version|radio\s+edit|live(?:\s+.*)?|acoustic(?:\s+version)?|deluxe(?:\s+edition)?|bonus\s+track|spotify\s+singles|mono|stereo|explicit|extended(?:\s+mix)?|instrumental).*$",
            string.Empty,
            RegexOptions.IgnoreCase);

        // Strip inline "feat." / "ft." / "featuring"
        cleaned = Regex.Replace(
            cleaned,
            @"\s+\b(?:feat\.?|ft\.?|featuring)\b.*$",
            string.Empty,
            RegexOptions.IgnoreCase);

        return Regex.Replace(cleaned, @"\s+", " ").Trim();
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
        [JsonPropertyName("trackId")]
        public long? TrackId { get; set; }

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

        [JsonPropertyName("trackTimeMillis")]
        public int? TrackTimeMillis { get; set; }
    }
}
