namespace TuneTrack.Tests;

using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TuneTrack.Data;
using TuneTrack.Models;
using TuneTrack.Services.Analysis;
using TuneTrack.Services.Providers;
using TuneTrack.Services.Recommendations;
using TuneTrack.Services.Samples;
using Xunit;

public class PlaylistAndProviderTests
{
    [Fact]
    public async Task SpotifyProvider_DoesNotRepeatPlaylistCoverWhenTrackArtworkMissing()
    {
        string nextData = """
            {"props":{"pageProps":{"state":{"data":{"entity":{
              "name":"Test Spotify Mix",
              "coverArt":{"sources":[{"url":"https://i.scdn.co/image/playlist-cover"}]},
              "trackList":[
                {"title":"Unmatched Song","subtitle":"Unknown Indie Band","duration":200000}
              ]
            }}}}}}
            """;
        string html = $"<html><body><script id=\"__NEXT_DATA__\" type=\"application/json\">{nextData}</script></body></html>";

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        });
        var provider = new SpotifyPlaylistProvider(
            new StubFactory(new HttpClient(handler)),
            new NullEnricher(),
            NullLogger<SpotifyPlaylistProvider>.Instance);

        var result = await provider.GetPlaylistAsync(new Uri("https://open.spotify.com/playlist/123456"));

        Assert.True(result.Success);
        Assert.Equal("https://i.scdn.co/image/playlist-cover", result.CoverUrl);
        Assert.Single(result.Tracks);
        Assert.Null(result.Tracks[0].ArtworkUrl);
    }

    [Fact]
    public async Task YouTubeMusicProvider_FetchesWwwPlaylistWithConsentCookiesAndParsesYtInitialData()
    {
        HttpRequestMessage? captured = null;
        string ytJson = """
            {
              "metadata":{"playlistMetadataRenderer":{"title":"Night Drive"}},
              "microformat":{"microformatDataRenderer":{"thumbnail":{"thumbnails":[{"url":"https://i.ytimg.com/vi/abc/hqdefault.jpg"}]}}},
              "contents":{"twoColumnBrowseResultsRenderer":{"tabs":[{"tabRenderer":{"content":{"sectionListRenderer":{"contents":[{"itemSectionRenderer":{"contents":[{"playlistVideoListRenderer":{"contents":[
                {"playlistVideoRenderer":{"videoId":"v1","title":{"runs":[{"text":"Harder, Better, Faster, Stronger"}]},"shortBylineText":{"runs":[{"text":"Daft Punk - Topic"}]},"lengthSeconds":"224"}},
                {"playlistVideoRenderer":{"videoId":"v2","title":{"runs":[{"text":"Kavinsky - Nightcall"}]},"shortBylineText":{"runs":[{"text":"SynthChannel"}]},"lengthSeconds":"259"}},
                {"playlistVideoRenderer":{"videoId":"v3","title":{"runs":[{"text":"[Private video]"}]},"shortBylineText":{"runs":[{"text":""}]}}}
              ]}}]}}]}}}}]}}
            }
            """;
        string html = $"<html><head><meta property=\"og:title\" content=\"Night Drive - YouTube\"></head><body><script>var ytInitialData = {ytJson};</script></body></html>";

        var handler = new StubHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        });
        var provider = new YouTubeMusicPlaylistProvider(
            new StubFactory(new HttpClient(handler)),
            new NullEnricher(),
            NullLogger<YouTubeMusicPlaylistProvider>.Instance);

        var result = await provider.GetPlaylistAsync(new Uri("https://music.youtube.com/playlist?list=PLtest999"));

        Assert.NotNull(captured);
        Assert.Equal("https://www.youtube.com/playlist?list=PLtest999", captured!.RequestUri!.ToString());
        string cookieHeader = string.Join("; ", captured.Headers.GetValues("Cookie"));
        Assert.Contains("SOCS=", cookieHeader);
        Assert.Contains("CONSENT=", cookieHeader);

        Assert.True(result.Success);
        Assert.False(result.IsPartialResult);
        Assert.Equal("Night Drive", result.Title);
        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(("Daft Punk", "Harder, Better, Faster, Stronger", 224000),
            (result.Tracks[0].Artist, result.Tracks[0].Title, result.Tracks[0].DurationMs));
        Assert.Equal(("Kavinsky", "Nightcall", 259000),
            (result.Tracks[1].Artist, result.Tracks[1].Title, result.Tracks[1].DurationMs));
    }

    [Fact]
    public async Task PlaylistService_DoesNotSavePartialZeroTrackToHistory_AndPastePreservesHeaderAndSaves()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TuneTrackTests_{Guid.NewGuid()}")
            .Options;
        await using var db = new AppDbContext(dbOptions);

        var partialProvider = new StubPartialProvider();
        var service = new PlaylistService(
            new[] { partialProvider },
            new SamplePlaylistService(),
            new DiversityScorer(),
            new EmptyRecommender(),
            new NullEnricher(),
            db,
            NullLogger<PlaylistService>.Instance);

        const string clientId = "test-client-1";
        var partial = await service.AnalyzeUrlAsync("https://open.spotify.com/playlist/partial123", clientId);

        Assert.True(partial.IsPartialResult);
        Assert.Empty(partial.Playlist.Tracks);
        Assert.Empty(await service.GetHistoryAsync(clientId));

        string paste3 = "Daft Punk - One More Time\nKendrick Lamar - Alright\nFleet Foxes - White Winter Hymnal";
        var pasted3Result = await service.AnalyzeManualTrackListAsync(
            partial.Playlist.Title,
            partial.Playlist.Platform,
            partial.Playlist.CoverUrl,
            paste3,
            clientId);

        Assert.False(pasted3Result.IsPartialResult);
        Assert.Equal("Spotify", pasted3Result.Playlist.Platform);
        Assert.Equal("Locked Mix", pasted3Result.Playlist.Title);
        Assert.Equal("https://i.scdn.co/image/locked-cover", pasted3Result.Playlist.CoverUrl);
        Assert.Equal(3, pasted3Result.Playlist.TrackCount);

        var historyAfter3 = await service.GetHistoryAsync(clientId);
        Assert.Single(historyAfter3);
        Assert.Equal(("Locked Mix", "Spotify", 3), (historyAfter3[0].Title, historyAfter3[0].Platform, historyAfter3[0].TrackCount));

        string paste10 = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"{i}. Artist {i} - Song {i}"));
        var pasted10Result = await service.AnalyzeManualTrackListAsync(
            partial.Playlist.Title,
            partial.Playlist.Platform,
            partial.Playlist.CoverUrl,
            paste10,
            clientId);

        Assert.Equal(10, pasted10Result.Playlist.TrackCount);
        Assert.Equal(2, (await service.GetHistoryAsync(clientId)).Count);
    }

    private sealed class StubPartialProvider : IPlaylistProvider
    {
        public string PlatformName => "Spotify";
        public bool CanHandle(Uri uri) => true;
        public Task<PlaylistFetchResult> GetPlaylistAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaylistFetchResult.Partial("Spotify", "Locked Mix", "https://i.scdn.co/image/locked-cover", "No tracks"));
    }

    private sealed class NullEnricher : ITrackEnricherService
    {
        public Task<TrackEnrichmentResult?> LookupTrackAsync(string artist, string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<TrackEnrichmentResult?>(null);

        public Task EnrichTracksAsync(IList<AnalyzedTrack> tracks, int maxToEnrich = 25, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyRecommender : IRecommendationEngine
    {
        public Task<List<TrackSuggestion>> GenerateRecommendationsAsync(IReadOnlyList<AnalyzedTrack> existingTracks, int count = 8, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<TrackSuggestion>());
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
