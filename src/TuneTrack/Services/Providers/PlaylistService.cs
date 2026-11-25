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
    Task<AnalysisDashboardData> AnalyzeSampleAsync(string sampleId, string clientId, CancellationToken cancellationToken = default);
    Task<AnalysisDashboardData> AnalyzeManualTrackListAsync(string title, string platform, string? coverUrl, string rawTrackList, string clientId, CancellationToken cancellationToken = default);
    Task<List<AnalyzedPlaylist>> GetHistoryAsync(string clientId, CancellationToken cancellationToken = default);
    Task<AnalyzedPlaylist?> GetPlaylistByIdAsync(int id, string clientId, CancellationToken cancellationToken = default);
    Task<bool> DeleteHistoryItemAsync(int id, string clientId, CancellationToken cancellationToken = default);
    Task<List<SavedRecommendation>> GetSavedRecommendationsAsync(string clientId, CancellationToken cancellationToken = default);
    Task<bool> SaveRecommendationAsync(string clientId, TrackSuggestion suggestion, int? sourcePlaylistId, CancellationToken cancellationToken = default);
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

    public async Task<AnalysisDashboardData> AnalyzeUrlAsync(string url, string clientId, CancellationToken cancellationToken = default)
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

        var fetchResult = await provider.GetPlaylistAsync(uri, cancellationToken);
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

    public async Task<AnalysisDashboardData> AnalyzeManualTrackListAsync(
        string title,
        string platform,
        string? coverUrl,
        string rawTrackList,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var tracks = FallbackTrackListParser.ParseTrackList(rawTrackList);
        if (tracks.Count == 0)
        {
            throw new ArgumentException("Could not parse any tracks. Please use 'Artist - Title' format, one per line.");
        }

        // Enrich the pasted tracks
        await _enricher.EnrichTracksAsync(tracks, maxToEnrich: 30, cancellationToken);

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

        // Persist to database if we have tracks or a non-empty report
        if (tracks.Count > 0 && !string.IsNullOrWhiteSpace(clientId))
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
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        bool alreadySaved = await _db.SavedRecommendations.AnyAsync(
            s => s.ClientId == clientId && s.Title == suggestion.Title && s.Artist == suggestion.Artist,
            cancellationToken);

        if (alreadySaved) return true;

        var entity = new SavedRecommendation
        {
            ClientId = clientId,
            Title = suggestion.Title,
            Artist = suggestion.Artist,
            Genre = suggestion.Genre,
            ArtworkUrl = suggestion.ArtworkUrl,
            SourcePlaylistId = sourcePlaylistId,
            SavedAt = DateTime.UtcNow
        };

        _db.SavedRecommendations.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveSavedRecommendationAsync(int id, string clientId, CancellationToken cancellationToken = default)
    {
        var item = await _db.SavedRecommendations.FirstOrDefaultAsync(s => s.Id == id && s.ClientId == clientId, cancellationToken);
        if (item == null) return false;

        _db.SavedRecommendations.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IsRecommendationSavedAsync(string clientId, string title, string artist, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        return await _db.SavedRecommendations.AnyAsync(
            s => s.ClientId == clientId && s.Title == title && s.Artist == artist,
            cancellationToken);
    }

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
