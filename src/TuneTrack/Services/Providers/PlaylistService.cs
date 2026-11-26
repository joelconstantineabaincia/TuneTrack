namespace TuneTrack.Services.Providers;

using Microsoft.EntityFrameworkCore;
using TuneTrack.Data;
using TuneTrack.Models;
using TuneTrack.Services.Analysis;
using TuneTrack.Services.Recommendations;
using TuneTrack.Services.Samples;

public record AnalysisDashboardData(
    AnalyzedPlaylist Playlist,
    DiversityScoreResult Diversity,
    List<TrackSuggestion> Recommendations,
    bool IsPartialResult,
    string? WarningMessage
);

public interface IPlaylistService
{
    Task<AnalysisDashboardData> AnalyzeUrlAsync(string url, string clientId, CancellationToken cancellationToken = default);
    Task<AnalysisDashboardData> AnalyzeUrlAsync(string url, string clientId, IProgress<(int Completed, int Total)>? progress, CancellationToken cancellationToken = default)
        => AnalyzeUrlAsync(url, clientId, cancellationToken);
    Task<AnalysisDashboardData> AnalyzeSampleAsync(string sampleId, string clientId, CancellationToken cancellationToken = default);
    Task<AnalysisDashboardData> AnalyzeManualTrackListAsync(string title, string platform, string? coverUrl, string rawTrackList, string clientId, CancellationToken cancellationToken = default);
    Task<AnalysisDashboardData> AnalyzeManualTrackListAsync(string title, string platform, string? coverUrl, string rawTrackList, string clientId, IProgress<(int Completed, int Total)>? progress, CancellationToken cancellationToken = default)
        => AnalyzeManualTrackListAsync(title, platform, coverUrl, rawTrackList, clientId, cancellationToken);
    Task<List<AnalyzedPlaylist>> GetHistoryAsync(string clientId, CancellationToken cancellationToken = default);
    Task<AnalyzedPlaylist?> GetPlaylistByIdAsync(int id, string clientId, CancellationToken cancellationToken = default);
    Task<bool> DeleteHistoryItemAsync(int id, string clientId, CancellationToken cancellationToken = default);
    Task<List<SavedRecommendation>> GetSavedRecommendationsAsync(string clientId, CancellationToken cancellationToken = default);
    Task<bool> SaveRecommendationAsync(string clientId, TrackSuggestion suggestion, int? sourcePlaylistId, CancellationToken cancellationToken = default);
    Task<(bool Success, bool IsSaved)> ToggleSavedRecommendationAsync(string clientId, TrackSuggestion suggestion, int? sourcePlaylistId, CancellationToken cancellationToken = default);
    Task<bool> RemoveSavedRecommendationAsync(int id, string clientId, CancellationToken cancellationToken = default);
    Task<bool> IsRecommendationSavedAsync(string clientId, string title, string artist, CancellationToken cancellationToken = default);
}

public class PlaylistService : IPlaylistService
{
    private readonly IEnumerable<IPlaylistProvider> _providers;
    private readonly ISamplePlaylistService _samples;
    private readonly IDiversityScorer _scorer;
    private readonly IRecommendationEngine _recommender;
    private readonly ITrackEnricherService _enricher;
    private readonly AppDbContext _db;
    private readonly ILogger<PlaylistService> _logger;

    public PlaylistService(
        IEnumerable<IPlaylistProvider> providers,
        ISamplePlaylistService samples,
        IDiversityScorer scorer,
        IRecommendationEngine recommender,
        ITrackEnricherService enricher,
        AppDbContext db,
        ILogger<PlaylistService> logger)
    {
        _providers = providers;
        _samples = samples;
        _scorer = scorer;
        _recommender = recommender;
        _enricher = enricher;
        _db = db;
        _logger = logger;
    }

    public Task<AnalysisDashboardData> AnalyzeUrlAsync(string url, string clientId, CancellationToken cancellationToken = default)
        => AnalyzeUrlAsync(url, clientId, progress: null, cancellationToken);

    public async Task<AnalysisDashboardData> AnalyzeUrlAsync(
        string url,
        string clientId,
        IProgress<(int Completed, int Total)>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Please enter a valid absolute playlist URL.");
        }

        var provider = _providers.FirstOrDefault(p => p.CanHandle(uri));
        if (provider == null)
        {
            throw new NotSupportedException($"No provider found for URL '{uri.Host}'. Please use Spotify, Apple Music, or YouTube Music.");
        }

        var fetchResult = await provider.GetPlaylistAsync(uri, progress, cancellationToken);
        if (!fetchResult.Success && !fetchResult.IsPartialResult)
        {
            throw new InvalidOperationException(fetchResult.ErrorMessage ?? "Failed to fetch playlist data.");
        }

        return await ProcessAndPersistAsync(
            sourceUrl: uri.ToString(),
            platform: fetchResult.Platform,
            title: fetchResult.Title,
            coverUrl: fetchResult.CoverUrl,
            tracks: fetchResult.Tracks,
            isPartialResult: fetchResult.IsPartialResult,
            warningMessage: fetchResult.ErrorMessage,
            clientId: clientId,
            cancellationToken: cancellationToken
        );
    }

    public async Task<AnalysisDashboardData> AnalyzeSampleAsync(string sampleId, string clientId, CancellationToken cancellationToken = default)
    {
        var sample = _samples.GetSamplePlaylist(sampleId)
            ?? throw new ArgumentException($"Sample playlist '{sampleId}' was not found.");

        // Clone tracks so sample master definition stays immutable
        var tracks = sample.Tracks.Select(t => new AnalyzedTrack
        {
            Title = t.Title,
            Artist = t.Artist,
            Album = t.Album,
            Genre = t.Genre,
            ReleaseYear = t.ReleaseYear,
            DurationMs = t.DurationMs,
            ArtworkUrl = t.ArtworkUrl,
            PreviewUrl = t.PreviewUrl
        }).ToList();

        return await ProcessAndPersistAsync(
            sourceUrl: $"sample://{sample.Id}",
            platform: sample.Platform,
            title: sample.Title,
            coverUrl: sample.CoverUrl,
            tracks: tracks,
            isPartialResult: false,
            warningMessage: null,
            clientId: clientId,
            cancellationToken: cancellationToken
        );
    }

    public Task<AnalysisDashboardData> AnalyzeManualTrackListAsync(
        string title,
        string platform,
        string? coverUrl,
        string rawTrackList,
        string clientId,
        CancellationToken cancellationToken = default)
        => AnalyzeManualTrackListAsync(title, platform, coverUrl, rawTrackList, clientId, progress: null, cancellationToken);

    public async Task<AnalysisDashboardData> AnalyzeManualTrackListAsync(
        string title,
        string platform,
        string? coverUrl,
        string rawTrackList,
        string clientId,
        IProgress<(int Completed, int Total)>? progress,
        CancellationToken cancellationToken = default)
    {
        var tracks = FallbackTrackListParser.ParseTrackList(rawTrackList);
        if (tracks.Count == 0)
        {
            throw new ArgumentException("Could not parse any tracks. Please use 'Artist - Title' format, one per line.");
        }

        // Enrich the pasted tracks
        await _enricher.EnrichTracksAsync(tracks, progress, maxToEnrich: 100, cancellationToken);

        return await ProcessAndPersistAsync(
            sourceUrl: "manual://pasted-tracklist",
            platform: string.IsNullOrWhiteSpace(platform) ? "Custom" : platform,
            title: string.IsNullOrWhiteSpace(title) ? "Custom Tracklist" : title,
            coverUrl: coverUrl,
            tracks: tracks,
            isPartialResult: false,
            warningMessage: null,
            clientId: clientId,
            cancellationToken: cancellationToken
        );
    }

    private async Task<AnalysisDashboardData> ProcessAndPersistAsync(
        string sourceUrl,
        string platform,
        string title,
        string? coverUrl,
        List<AnalyzedTrack> tracks,
        bool isPartialResult,
        string? warningMessage,
        string clientId,
        CancellationToken cancellationToken)
    {
        var diversity = _scorer.CalculateScore(tracks);

        var recommendations = isPartialResult || tracks.Count == 0
            ? new List<TrackSuggestion>()
            : await _recommender.GenerateRecommendationsAsync(tracks, 8, cancellationToken);

        // Format total duration
        long totalMs = tracks.Sum(t => (long)t.DurationMs);
        string formattedDuration = FormatDuration(totalMs);

        var playlistEntity = new AnalyzedPlaylist
        {
            ClientId = clientId,
            SourceUrl = sourceUrl,
            Platform = platform,
            Title = string.IsNullOrWhiteSpace(title) ? $"{platform} Playlist" : title,
            CoverUrl = coverUrl,
            DiversityScore = diversity.TotalScore,
            GenreVariety = diversity.GenreVariety,
            ArtistUniqueness = diversity.ArtistUniqueness,
            EraSpread = diversity.EraSpread,
            TrackCount = tracks.Count,
            TotalDuration = formattedDuration,
            AnalyzedAt = DateTime.UtcNow,
            Tracks = tracks
        };

        // Persist completed analyses only (never save 0-track or partial results to history)
        if (!isPartialResult && tracks.Count > 0 && !string.IsNullOrWhiteSpace(clientId))
        {
            try
            {
                _db.AnalyzedPlaylists.Add(playlistEntity);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save analyzed playlist to database for client {ClientId}", clientId);
            }
        }

        return new AnalysisDashboardData(
            Playlist: playlistEntity,
            Diversity: diversity,
            Recommendations: recommendations,
            IsPartialResult: isPartialResult,
            WarningMessage: warningMessage
        );
    }

    public async Task<List<AnalyzedPlaylist>> GetHistoryAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return new List<AnalyzedPlaylist>();

        return await _db.AnalyzedPlaylists
            .AsNoTracking()
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.AnalyzedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<AnalyzedPlaylist?> GetPlaylistByIdAsync(int id, string clientId, CancellationToken cancellationToken = default)
    {
        return await _db.AnalyzedPlaylists
            .Include(p => p.Tracks)
            .FirstOrDefaultAsync(p => p.Id == id && p.ClientId == clientId, cancellationToken);
    }

    public async Task<bool> DeleteHistoryItemAsync(int id, string clientId, CancellationToken cancellationToken = default)
    {
        var item = await _db.AnalyzedPlaylists.FirstOrDefaultAsync(p => p.Id == id && p.ClientId == clientId, cancellationToken);
        if (item == null) return false;

        _db.AnalyzedPlaylists.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<SavedRecommendation>> GetSavedRecommendationsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return new List<SavedRecommendation>();

        return await _db.SavedRecommendations
            .AsNoTracking()
            .Where(s => s.ClientId == clientId)
            .OrderByDescending(s => s.SavedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SaveRecommendationAsync(string clientId, TrackSuggestion suggestion, int? sourcePlaylistId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(suggestion?.Title) ||
            string.IsNullOrWhiteSpace(suggestion?.Artist))
        {
            return false;
        }

        string titleLower = suggestion.Title.Trim().ToLowerInvariant();
        string artistLower = suggestion.Artist.Trim().ToLowerInvariant();

        try
        {
            bool alreadySaved = await _db.SavedRecommendations.AnyAsync(
                s => s.ClientId == clientId &&
                     s.Title.ToLower() == titleLower &&
                     s.Artist.ToLower() == artistLower,
                cancellationToken);

            if (alreadySaved) return true;

            int? validSourcePlaylistId = null;
            if (sourcePlaylistId is > 0 &&
                await _db.AnalyzedPlaylists.AnyAsync(p => p.Id == sourcePlaylistId.Value, cancellationToken))
            {
                validSourcePlaylistId = sourcePlaylistId.Value;
            }

            var entity = new SavedRecommendation
            {
                ClientId = Truncate(clientId.Trim(), 64),
                Title = Truncate(suggestion.Title.Trim(), 256),
                Artist = Truncate(suggestion.Artist.Trim(), 256),
                Genre = string.IsNullOrWhiteSpace(suggestion.Genre) ? null : Truncate(suggestion.Genre.Trim(), 128),
                ArtworkUrl = string.IsNullOrWhiteSpace(suggestion.ArtworkUrl) ? null : Truncate(suggestion.ArtworkUrl.Trim(), 2048),
                SourcePlaylistId = validSourcePlaylistId,
                SavedAt = DateTime.UtcNow
            };

            _db.SavedRecommendations.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save recommendation '{Title}' by '{Artist}' for client {ClientId}", suggestion.Title, suggestion.Artist, clientId);
            return false;
        }
    }

    public async Task<(bool Success, bool IsSaved)> ToggleSavedRecommendationAsync(string clientId, TrackSuggestion suggestion, int? sourcePlaylistId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(suggestion?.Title) ||
            string.IsNullOrWhiteSpace(suggestion?.Artist))
        {
            return (false, false);
        }

        string titleLower = suggestion.Title.Trim().ToLowerInvariant();
        string artistLower = suggestion.Artist.Trim().ToLowerInvariant();

        try
        {
            var existing = await _db.SavedRecommendations.FirstOrDefaultAsync(
                s => s.ClientId == clientId &&
                     s.Title.ToLower() == titleLower &&
                     s.Artist.ToLower() == artistLower,
                cancellationToken);

            if (existing != null)
            {
                _db.SavedRecommendations.Remove(existing);
                await _db.SaveChangesAsync(cancellationToken);
                return (true, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle recommendation '{Title}' by '{Artist}' for client {ClientId}", suggestion.Title, suggestion.Artist, clientId);
            return (false, false);
        }

        bool saved = await SaveRecommendationAsync(clientId, suggestion, sourcePlaylistId, cancellationToken);
        return (saved, saved);
    }

    public async Task<bool> RemoveSavedRecommendationAsync(int id, string clientId, CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await _db.SavedRecommendations.FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId, cancellationToken);
            if (item == null) return false;

            _db.SavedRecommendations.Remove(item);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove saved recommendation {Id} for client {ClientId}", id, clientId);
            return false;
        }
    }

    public async Task<bool> IsRecommendationSavedAsync(string clientId, string title, string artist, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return false;

        string titleLower = title.Trim().ToLowerInvariant();
        string artistLower = artist.Trim().ToLowerInvariant();

        return await _db.SavedRecommendations.AnyAsync(
            s => s.ClientId == clientId &&
                 s.Title.ToLower() == titleLower &&
                 s.Artist.ToLower() == artistLower,
            cancellationToken);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string FormatDuration(long totalMs)
    {
        if (totalMs <= 0) return "0m";

        var ts = TimeSpan.FromMilliseconds(totalMs);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }
        return $"{ts.Minutes}m {ts.Seconds}s";
    }
}
