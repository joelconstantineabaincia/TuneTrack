namespace TuneTrack.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TuneTrack.Models;
using TuneTrack.Services.Recommendations;
using Xunit;

public class TrackEnricherServiceTests
{
    private sealed record StubCandidate(
        string TrackName,
        string ArtistName,
        string CollectionName,
        string PrimaryGenreName,
        string ReleaseDate,
        string ArtworkUrl100,
        string PreviewUrl);

    private static IHttpClientFactory CreateStubFactory(Func<string, IEnumerable<StubCandidate>> responder)
    {
        var handler = new StubHandler(req =>
        {
            string query = req.RequestUri?.Query ?? string.Empty;
            var items = responder(query).Select(c => new
            {
                trackName = c.TrackName,
                artistName = c.ArtistName,
                collectionName = c.CollectionName,
                primaryGenreName = c.PrimaryGenreName,
                releaseDate = c.ReleaseDate,
                artworkUrl100 = c.ArtworkUrl100,
                previewUrl = c.PreviewUrl
            }).ToList();

            string json = JsonSerializer.Serialize(new { resultCount = items.Count, results = items });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        return new StubFactory(new HttpClient(handler));
    }

    [Fact]
    public async Task LookupTrackAsync_RejectsUnmatchedArtistCandidate()
    {
        var factory = CreateStubFactory(_ => new[]
        {
            new StubCandidate("Time", "Vitamin String Quartet", "Tribute Album", "Classical", "2021-05-01T00:00:00Z",
                "https://is1-ssl.mzstatic.com/image/thumb/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/preview.m4a")
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(factory, cache, NullLogger<TrackEnricherService>.Instance);

        var result = await enricher.LookupTrackAsync("Pink Floyd", "Time");

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupTrackAsync_SkipsWrongFirstHitAndMatchesFuzzyArtist()
    {
        var factory = CreateStubFactory(_ => new[]
        {
            new StubCandidate("One Kiss", "Karaoke Stars", "Hits", "Pop", "2020-01-01T00:00:00Z",
                "https://is1-ssl.mzstatic.com/image/wrong/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/wrong.m4a"),
            new StubCandidate("One Kiss", "Calvin Harris & Dua Lipa", "One Kiss - Single", "Dance", "2018-04-06T00:00:00Z",
                "https://is1-ssl.mzstatic.com/image/match/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/match.m4a")
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(factory, cache, NullLogger<TrackEnricherService>.Instance);

        var result = await enricher.LookupTrackAsync("Calvin Harris feat. Dua Lipa", "One Kiss");

        Assert.NotNull(result);
        Assert.Equal("Dance", result!.Genre);
        Assert.Equal(2018, result.ReleaseYear);
        Assert.Equal("https://is1-ssl.mzstatic.com/image/match/600x600bb.jpg", result.ArtworkUrl);
    }

    [Fact]
    public async Task LookupTrackAsync_MatchesWhenEitherArtistContainsTheOtherIgnoringConnectors()
    {
        var factory = CreateStubFactory(query =>
        {
            if (query.Contains("SZA", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new StubCandidate("All The Stars", "Kendrick Lamar", "Black Panther", "Hip-Hop", "2018-01-04T00:00:00Z",
                        "https://is1-ssl.mzstatic.com/image/a/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/a.m4a")
                };
            }

            return new[]
            {
                new StubCandidate("Could You Be Loved", "Bob Marley & The Wailers", "Uprising", "Reggae", "1980-06-10T00:00:00Z",
                    "https://is1-ssl.mzstatic.com/image/b/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/b.m4a")
            };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(factory, cache, NullLogger<TrackEnricherService>.Instance);

        var r1 = await enricher.LookupTrackAsync("Kendrick Lamar ft. SZA", "All The Stars");
        var r2 = await enricher.LookupTrackAsync("Bob Marley", "Could You Be Loved");

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal("Reggae", r2!.Genre);
    }

    [Fact]
    public async Task EnrichTracksAsync_NormalizesITunesGenreVariants()
    {
        var factory = CreateStubFactory(query =>
        {
            if (query.Contains("Kendrick", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new StubCandidate("Alright", "Kendrick Lamar", "TPAB", "Hip-Hop", "2015-03-15T00:00:00Z",
                        "https://is1-ssl.mzstatic.com/image/k/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/k.m4a")
                };
            }
            if (query.Contains("Nas", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new StubCandidate("N.Y. State of Mind", "Nas", "Illmatic", "Hip Hop", "1994-04-19T00:00:00Z",
                        "https://is1-ssl.mzstatic.com/image/n/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/n.m4a")
                };
            }
            return new[]
            {
                new StubCandidate("Juicy", "The Notorious B.I.G.", "Ready to Die", "Hip-Hop/Rap", "1994-08-08T00:00:00Z",
                    "https://is1-ssl.mzstatic.com/image/b/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/b.m4a")
            };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(factory, cache, NullLogger<TrackEnricherService>.Instance);

        var tracks = new List<AnalyzedTrack>
        {
            new() { Artist = "Kendrick Lamar", Title = "Alright" },
            new() { Artist = "Nas", Title = "N.Y. State of Mind" },
            new() { Artist = "The Notorious B.I.G.", Title = "Juicy" }
        };

        await enricher.EnrichTracksAsync(tracks);

        Assert.All(tracks, t => Assert.Equal("Hip-Hop/Rap", t.Genre));
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
