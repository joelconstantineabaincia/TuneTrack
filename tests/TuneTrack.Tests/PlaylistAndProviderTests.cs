namespace TuneTrack.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
    public async Task SpotifyProvider_EnrichesMoreThan30Tracks()
    {
        var trackJson = string.Join(",", Enumerable.Range(1, 35)
            .Select(i => $"{{\"title\":\"Song {i}\",\"subtitle\":\"Artist {i}\",\"duration\":180000}}"));
        string nextData = $"{{\"props\":{{\"pageProps\":{{\"state\":{{\"data\":{{\"entity\":{{\"name\":\"Big Playlist\",\"coverArt\":{{\"sources\":[{{\"url\":\"https://i.scdn.co/image/big\"}}]}},\"trackList\":[{trackJson}]}}}}}}}}}}}}";
        string html = $"<html><body><script id=\"__NEXT_DATA__\" type=\"application/json\">{nextData}</script></body></html>";

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        });
        var enricher = new CountingEnricher();
        var provider = new SpotifyPlaylistProvider(
            new StubFactory(new HttpClient(handler)),
            enricher,
            NullLogger<SpotifyPlaylistProvider>.Instance);

        var progress = new Progress<(int Completed, int Total)>();
        var result = await provider.GetPlaylistAsync(new Uri("https://open.spotify.com/playlist/big51"), progress, CancellationToken.None);

        Assert.Equal(35, result.Tracks.Count);
        Assert.Equal(35, enricher.RequestedMaxToEnrich >= 35 ? 35 : enricher.EnrichedCount);
        Assert.Same(progress, enricher.CapturedProgress);
        Assert.False(enricher.CapturedCancellationToken.CanBeCanceled);
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
            new CountingEnricher(),
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

    [Fact]
    public async Task PlaylistService_SaveAndToggleRecommendations_PersistsAndNormalizesSourcePlaylistId()
    {
        string dbName = $"TuneTrackSavedTests_{Guid.NewGuid()}";
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        int validPlaylistId;
        await using (var db = new AppDbContext(dbOptions))
        {
            var playlist = new AnalyzedPlaylist
            {
                ClientId = "client-a",
                SourceUrl = "https://open.spotify.com/playlist/valid",
                Platform = "Spotify",
                Title = "Valid Mix",
                TrackCount = 1,
                TotalDuration = "3m 0s"
            };
            db.AnalyzedPlaylists.Add(playlist);
            await db.SaveChangesAsync();
            validPlaylistId = playlist.Id;
        }

        var suggestionA = new TrackSuggestion("Time", "Pink Floyd", "Rock", "https://art/1.jpg", "https://prev/1.m4a", "Reason");
        var suggestionSameTitleDifferentArtist = new TrackSuggestion("Time", "Hans Zimmer", "Soundtrack", "https://art/2.jpg", "https://prev/2.m4a", "Reason");
        var suggestionNullSource = new TrackSuggestion("Midnight City", "M83", "Electronic", "https://art/3.jpg", "https://prev/3.m4a", "Reason");

        await using (var db = new AppDbContext(dbOptions))
        {
            var service = new PlaylistService(
                Array.Empty<IPlaylistProvider>(),
                new SamplePlaylistService(),
                new DiversityScorer(),
                new EmptyRecommender(),
                new NullEnricher(),
                db,
                NullLogger<PlaylistService>.Instance);

            Assert.True(await service.SaveRecommendationAsync("client-a", suggestionA, validPlaylistId));
            // Repeat save deduplicates
            Assert.True(await service.SaveRecommendationAsync("client-a", suggestionA, validPlaylistId));
            // Sample / unpersisted source playlist IDs (0 or nonexistent) normalize to null
            Assert.True(await service.SaveRecommendationAsync("client-a", suggestionSameTitleDifferentArtist, 0));
            Assert.True(await service.SaveRecommendationAsync("client-a", suggestionNullSource, 999999));

            // Toggle removes only the matching (Title, Artist) row
            var toggleOff = await service.ToggleSavedRecommendationAsync("client-a", suggestionA, validPlaylistId);
            Assert.Equal((true, false), toggleOff);

            var remaining = await service.GetSavedRecommendationsAsync("client-a");
            Assert.Equal(2, remaining.Count);
            Assert.DoesNotContain(remaining, r => r.Artist == "Pink Floyd");
            Assert.All(remaining, r => Assert.Null(r.SourcePlaylistId));

            // Toggle back on persists
            var toggleOn = await service.ToggleSavedRecommendationAsync("client-a", suggestionA, validPlaylistId);
            Assert.Equal((true, true), toggleOn);
        }

        // Fresh context simulates page reload
        await using (var reloadDb = new AppDbContext(dbOptions))
        {
            var reloadService = new PlaylistService(
                Array.Empty<IPlaylistProvider>(),
                new SamplePlaylistService(),
                new DiversityScorer(),
                new EmptyRecommender(),
                new NullEnricher(),
                reloadDb,
                NullLogger<PlaylistService>.Instance);

            var clientASaved = await reloadService.GetSavedRecommendationsAsync("client-a");
            Assert.Equal(3, clientASaved.Count);
            Assert.Contains(clientASaved, r => r.Artist == "Pink Floyd" && r.SourcePlaylistId == validPlaylistId);
            Assert.Empty(await reloadService.GetSavedRecommendationsAsync("client-b"));
        }
    }

    [Fact]
    public async Task PlaylistService_SaveAndToggle_LogsAndReturnsFalseWhenSaveFails()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TuneTrackFailingSaveTests_{Guid.NewGuid()}")
            .Options;
        await using var db = new FailingSaveDbContext(dbOptions);
        var logger = new RecordingLogger<PlaylistService>();

        var service = new PlaylistService(
            Array.Empty<IPlaylistProvider>(),
            new SamplePlaylistService(),
            new DiversityScorer(),
            new EmptyRecommender(),
            new NullEnricher(),
            db,
            logger);

        var suggestion = new TrackSuggestion("Song", "Artist", "Indie", null, null, "Reason");

        bool saveResult = await service.SaveRecommendationAsync("client-a", suggestion, sourcePlaylistId: null);
        var toggleResult = await service.ToggleSavedRecommendationAsync("client-a", suggestion, sourcePlaylistId: null);

        Assert.False(saveResult);
        Assert.Equal((false, false), toggleResult);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task AppleMusicProvider_ParsesSerializedServerDataAndResolvesByIdWithStorefront()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "apple-playlist-ca-trimmed.html");
        string fixtureHtml = await File.ReadAllTextAsync(fixturePath);

        var lookupUris = new List<string>();
        var handler = new StubHandler(req =>
        {
            string uri = req.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("music.apple.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(fixtureHtml, Encoding.UTF8, "text/html")
                };
            }

            Assert.DoesNotContain("/search", uri, StringComparison.OrdinalIgnoreCase);
            lookupUris.Add(uri);

            string lookupJson = JsonSerializer.Serialize(new
            {
                resultCount = 7,
                results = new object[]
                {
                    new { wrapperType = "track", kind = "song", trackId = 1444327839L, trackName = "Mi Gente", artistName = "J Balvin & Willy William", collectionName = "Mi Gente - Single", primaryGenreName = "Pop", releaseDate = "2017-06-30T12:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/1/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/1.m4a", trackTimeMillis = 186939 },
                    new { wrapperType = "track", kind = "song", trackId = 1698400133L, trackName = "rockstar (feat. 21 Savage)", artistName = "Post Malone", collectionName = "The Diamond Collection (Deluxe)", primaryGenreName = "Hip-Hop", releaseDate = "2017-09-15T12:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/2/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/2.m4a", trackTimeMillis = 218146 },
                    new { wrapperType = "track", kind = "song", trackId = 1272353167L, trackName = "Good Old Days (feat. Kesha)", artistName = "Macklemore", collectionName = "GEMINI", primaryGenreName = "Hip-Hop/Rap", releaseDate = "2017-09-22T12:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/3/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/3.m4a", trackTimeMillis = 240507 },
                    new { wrapperType = "track", kind = "song", trackId = 1440899202L, trackName = "Lonely Together (feat. Rita Ora)", artistName = "Avicii", collectionName = "AVĪCI (01) - EP", primaryGenreName = "Dance", releaseDate = "2017-08-11T12:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/4/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/4.m4a", trackTimeMillis = 181731 },
                    new { wrapperType = "track", kind = "song", trackId = 1444278727L, trackName = "Phases", artistName = "ALMA & French Montana", collectionName = "Phases - Single", primaryGenreName = "Pop", releaseDate = "2017-09-21T12:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/5/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/5.m4a", trackTimeMillis = 203892 },
                    new { wrapperType = "track", kind = "song", trackId = 1248378144L, trackName = "Bodak Yellow", artistName = "Cardi B", collectionName = "Bodak Yellow - Single", primaryGenreName = "Hip-Hop", releaseDate = "2017-06-16T12:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/6/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/6.m4a", trackTimeMillis = 223962 },
                    new { wrapperType = "track", kind = "song", trackId = 1268152499L, trackName = "Silence (feat. Khalid)", artistName = "Marshmello", collectionName = "Silence (feat. Khalid) - Single", primaryGenreName = "Electronic", releaseDate = "2017-08-11T12:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/7/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/7.m4a", trackTimeMillis = 180823 }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(lookupJson, Encoding.UTF8, "application/json")
            };
        });

        var factory = new StubFactory(new HttpClient(handler));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(factory, cache, NullLogger<TrackEnricherService>.Instance);
        var provider = new AppleMusicPlaylistProvider(factory, enricher, NullLogger<AppleMusicPlaylistProvider>.Instance);

        var result = await provider.GetPlaylistAsync(new Uri("https://music.apple.com/ca/playlist/apple-music-list/pl.41fda5adbf9b4db4ba1a2c0a76099ef9"));

        Assert.True(result.Success);
        Assert.False(result.IsPartialResult);
        Assert.Equal("Apple Music List", result.Title);
        Assert.Equal(7, result.Tracks.Count);
        Assert.Single(lookupUris);
        Assert.Contains("country=ca", lookupUris[0], StringComparison.OrdinalIgnoreCase);

        Assert.Equal(("Mi Gente", "J Balvin, Willy William"), (result.Tracks[0].Title, result.Tracks[0].Artist));
        Assert.Equal(("rockstar (feat. 21 Savage)", "Post Malone"), (result.Tracks[1].Title, result.Tracks[1].Artist));
        Assert.Equal(("Good Old Days (feat. Kesha)", "Macklemore"), (result.Tracks[2].Title, result.Tracks[2].Artist));
        Assert.Equal(("Lonely Together (feat. Rita Ora)", "Avicii"), (result.Tracks[3].Title, result.Tracks[3].Artist));
        Assert.Equal(("Phases", "ALMA, French Montana"), (result.Tracks[4].Title, result.Tracks[4].Artist));
        Assert.Equal(("Bodak Yellow", "Cardi B"), (result.Tracks[5].Title, result.Tracks[5].Artist));
        Assert.Equal(("Silence (feat. Khalid)", "Marshmello"), (result.Tracks[6].Title, result.Tracks[6].Artist));

        Assert.DoesNotContain(result.Tracks, t => t.Artist.Contains("Apple Music Artist", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Tracks, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Genre));
            Assert.NotNull(t.ReleaseYear);
            Assert.False(string.IsNullOrWhiteSpace(t.Album));
            Assert.EndsWith("600x600bb.jpg", t.ArtworkUrl);
            Assert.False(string.IsNullOrWhiteSpace(t.PreviewUrl));
        });
    }

    [Fact]
    public async Task AppleMusicProvider_BuildsTracksFromLookupWhenSerializedDataMissing_AndUsesPhStorefront()
    {
        string metaOnlyHtml = """
            <html><head>
              <meta property="og:title" content="OPM Spotlight on Apple Music">
              <meta property="og:image" content="https://is1-ssl.mzstatic.com/image/opm/cover.jpg">
              <meta property="music:song" content="https://music.apple.com/ph/song/wag-ipagsabi/1700000001">
              <meta property="music:song" content="https://music.apple.com/ph/song/dominga/1700000002">
            </head><body></body></html>
            """;

        string? capturedLookupUri = null;
        var handler = new StubHandler(req =>
        {
            string uri = req.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("music.apple.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(metaOnlyHtml, Encoding.UTF8, "text/html")
                };
            }

            capturedLookupUri = uri;
            string json = JsonSerializer.Serialize(new
            {
                resultCount = 2,
                results = new object[]
                {
                    new { wrapperType = "track", kind = "song", trackId = 1700000001L, trackName = "Wag Ipagsabi", artistName = "Bert Symoun & Dreycruz", collectionName = "Live", primaryGenreName = "Pop", releaseDate = "2024-06-01T00:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/a/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/a.m4a", trackTimeMillis = 215000 },
                    new { wrapperType = "track", kind = "song", trackId = 1700000002L, trackName = "Dominga", artistName = "Nateman & La Mave", collectionName = "Single", primaryGenreName = "Hip-Hop", releaseDate = "2024-04-01T00:00:00Z", artworkUrl100 = "https://is1-ssl.mzstatic.com/image/b/100x100bb.jpg", previewUrl = "https://audio-ssl.itunes.apple.com/b.m4a", trackTimeMillis = 190000 }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });

        var factory = new StubFactory(new HttpClient(handler));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(factory, cache, NullLogger<TrackEnricherService>.Instance);
        var provider = new AppleMusicPlaylistProvider(factory, enricher, NullLogger<AppleMusicPlaylistProvider>.Instance);

        var result = await provider.GetPlaylistAsync(new Uri("https://music.apple.com/ph/playlist/opm-spotlight/pl.testph"));

        Assert.True(result.Success);
        Assert.False(result.IsPartialResult);
        Assert.NotNull(capturedLookupUri);
        Assert.Contains("country=ph", capturedLookupUri!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(("Wag Ipagsabi", "Bert Symoun & Dreycruz", "Pop", 215000),
            (result.Tracks[0].Title, result.Tracks[0].Artist, result.Tracks[0].Genre, result.Tracks[0].DurationMs));
        Assert.Equal(("Dominga", "Nateman & La Mave", "Hip-Hop/Rap", 190000),
            (result.Tracks[1].Title, result.Tracks[1].Artist, result.Tracks[1].Genre, result.Tracks[1].DurationMs));
    }

    [Fact]
    public async Task AppleMusicProvider_ReturnsPartialFallbackWhenMajorityArtistsUnknown_AndHandlesAlbumOrSongGracefully()
    {
        string noArtistSsd = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new
                {
                    data = new
                    {
                        sections = new object[]
                        {
                            new
                            {
                                itemKind = "trackLockup",
                                items = new object[]
                                {
                                    new { title = "Track One", artistName = "", duration = 180000 },
                                    new { title = "Track Two", artistName = "", duration = 190000 }
                                }
                            }
                        }
                    }
                }
            }
        });
        string unknownArtistHtml = $"<html><head><meta property=\"og:title\" content=\"Mystery List on Apple Music\"><script type=\"application/json\" id=\"serialized-server-data\">{noArtistSsd}</script></head></html>";

        string albumHtml = "<html><head><meta property=\"og:title\" content=\"GEMINI by Macklemore on Apple Music\"><meta property=\"og:image\" content=\"https://is1-ssl.mzstatic.com/image/gemini.jpg\"></head></html>";

        var handler = new StubHandler(req =>
        {
            string url = req.RequestUri?.ToString() ?? string.Empty;
            string body = url.Contains("/album/") ? albumHtml : unknownArtistHtml;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html")
            };
        });

        var factory = new StubFactory(new HttpClient(handler));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(factory, cache, NullLogger<TrackEnricherService>.Instance);
        var provider = new AppleMusicPlaylistProvider(factory, enricher, NullLogger<AppleMusicPlaylistProvider>.Instance);

        var unknownArtistsResult = await provider.GetPlaylistAsync(new Uri("https://music.apple.com/us/playlist/mystery/pl.u-123"));
        Assert.True(unknownArtistsResult.Success);
        Assert.True(unknownArtistsResult.IsPartialResult);
        Assert.Empty(unknownArtistsResult.Tracks);
        Assert.Equal("Mystery List", unknownArtistsResult.Title);

        var albumUri = new Uri("https://music.apple.com/ca/album/gemini/1272352884");
        var songUri = new Uri("https://music.apple.com/ca/song/mi-gente/1444327839");
        Assert.True(provider.CanHandle(albumUri));
        Assert.True(provider.CanHandle(songUri));

        var albumPartial = await provider.GetPlaylistAsync(albumUri);
        Assert.True(albumPartial.Success);
        Assert.True(albumPartial.IsPartialResult);
        Assert.Equal("GEMINI by Macklemore", albumPartial.Title);
    }

    [Fact]
    public async Task PlaylistService_DoesNotSaveZeroMetadataAnalysisToHistory()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TuneTrackZeroMetaTests_{Guid.NewGuid()}")
            .Options;
        await using var db = new AppDbContext(dbOptions);

        var unenrichedProvider = new StubOkProvider(new List<AnalyzedTrack>
        {
            new() { Title = "Track A", Artist = "Artist A", DurationMs = 180000 },
            new() { Title = "Track B", Artist = "Artist B", DurationMs = 200000 }
        });

        var service = new PlaylistService(
            new[] { unenrichedProvider },
            new SamplePlaylistService(),
            new DiversityScorer(),
            new EmptyRecommender(),
            new NullEnricher(),
            db,
            NullLogger<PlaylistService>.Instance);

        var result = await service.AnalyzeUrlAsync("https://music.apple.com/ca/playlist/empty-meta/pl.test0", "client-zero");

        Assert.False(result.IsPartialResult);
        Assert.Equal(2, result.Playlist.TrackCount);
        Assert.Empty(await service.GetHistoryAsync("client-zero"));
    }

    private sealed class StubOkProvider(List<AnalyzedTrack> tracks) : IPlaylistProvider
    {
        public string PlatformName => "Apple Music";
        public bool CanHandle(Uri uri) => true;
        public Task<PlaylistFetchResult> GetPlaylistAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaylistFetchResult.Ok(PlatformName, "Zero Meta Mix", null, tracks));
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

    private sealed class CountingEnricher : ITrackEnricherService
    {
        public int RequestedMaxToEnrich { get; private set; }
        public int EnrichedCount { get; private set; }
        public IProgress<(int Completed, int Total)>? CapturedProgress { get; private set; }
        public CancellationToken CapturedCancellationToken { get; private set; }

        public Task<TrackEnrichmentResult?> LookupTrackAsync(string artist, string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<TrackEnrichmentResult?>(null);

        public Task EnrichTracksAsync(IList<AnalyzedTrack> tracks, int maxToEnrich = 25, CancellationToken cancellationToken = default) =>
            EnrichTracksAsync(tracks, progress: null, maxToEnrich, cancellationToken);

        public Task EnrichTracksAsync(
            IList<AnalyzedTrack> tracks,
            IProgress<(int Completed, int Total)>? progress,
            int maxToEnrich = 100,
            CancellationToken cancellationToken = default)
        {
            RequestedMaxToEnrich = maxToEnrich;
            CapturedProgress = progress;
            CapturedCancellationToken = cancellationToken;
            EnrichedCount = Math.Min(tracks.Count, maxToEnrich);
            foreach (var track in tracks.Take(maxToEnrich))
            {
                track.Genre = "Indie";
            }
            return Task.CompletedTask;
        }
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

    private sealed class FailingSaveDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new DbUpdateException("Simulated database write failure");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
