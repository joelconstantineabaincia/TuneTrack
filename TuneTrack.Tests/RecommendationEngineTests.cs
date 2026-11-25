namespace TuneTrack.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TuneTrack.Models;
using TuneTrack.Services;
using Xunit;

public class RecommendationEngineTests
{
    private static IHttpClientFactory CreateStubFactory(Func<string, IEnumerable<(string Title, string Artist, string Genre)>> resultsByTerm)
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            string term = ExtractTerm(query);
            var items = resultsByTerm(term).Select(item => new
            {
                trackName = item.Title,
                artistName = item.Artist,
                primaryGenreName = item.Genre,
                artworkUrl100 = "https://is1-ssl.mzstatic.com/image/thumb/100x100bb.jpg",
                previewUrl = "https://audio-ssl.itunes.apple.com/preview.m4a"
            }).ToList();

            string json = JsonSerializer.Serialize(new { resultCount = items.Count, results = items });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        return new StubHttpClientFactory(new HttpClient(handler));
    }

    private static string ExtractTerm(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("term", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }
        return string.Empty;
    }

    [Fact]
    public async Task TiedGenres_SamplesRoundRobinAcrossMultipleGenres()
    {
        var tracks = new List<AnalyzedTrack>
        {
            new() { Title = "Blue in Green", Artist = "Miles Davis", Genre = "Jazz", ReleaseYear = 1959 },
            new() { Title = "Flash Light", Artist = "Parliament", Genre = "Funk", ReleaseYear = 1978 },
            new() { Title = "Nightcall", Artist = "Kavinsky", Genre = "Synthwave", ReleaseYear = 2010 },
            new() { Title = "Alright", Artist = "Kendrick Lamar", Genre = "Hip-Hop", ReleaseYear = 2015 }
        };

        var factory = CreateStubFactory(term =>
            Enumerable.Range(1, 10).Select(i => ($"{term} Song {i}", $"{term} Artist {i}", term)));

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var engine = new RecommendationEngine(factory, cache, NullLogger<RecommendationEngine>.Instance);

        var recs = await engine.GenerateRecommendationsAsync(tracks, count: 8);

        Assert.Equal(8, recs.Count);
        var distinctGenres = recs.Select(r => r.Genre).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(distinctGenres.Count >= 4, $"Expected suggestions from all 4 tied genres, but got only: {string.Join(", ", distinctGenres)}");
    }

    [Fact]
    public async Task LowDiversityPlaylist_IncludesAtLeastHalfFromAdjacentGenresNotInPlaylist()
    {
        // All 10 tracks are Synth-Pop by 2 artists -> DiversityScore < 40
        var tracks = Enumerable.Range(1, 10).Select(i => new AnalyzedTrack
        {
            Title = $"Neon Track {i}",
            Artist = i <= 5 ? "CHVRCHES" : "The Midnight",
            Genre = "Synth-Pop",
            ReleaseYear = 2021
        }).ToList();

        var factory = CreateStubFactory(term =>
            Enumerable.Range(1, 10).Select(i => ($"{term} Cut {i}", $"New {term} Act {i}", term)));

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var engine = new RecommendationEngine(factory, cache, NullLogger<RecommendationEngine>.Instance);

        var recs = await engine.GenerateRecommendationsAsync(tracks, count: 8);

        Assert.Equal(8, recs.Count);
        int outsidePlaylistCount = recs.Count(r => !string.Equals(r.Genre, "Synth-Pop", StringComparison.OrdinalIgnoreCase));
        Assert.True(outsidePlaylistCount >= 4, $"Expected at least half (>= 4) suggestions outside 'Synth-Pop', got {outsidePlaylistCount}");
        Assert.DoesNotContain(recs, r => r.Artist is "CHVRCHES" or "The Midnight");
    }

    [Fact]
    public async Task Recommendations_ExcludeExistingTracksAndArtistsAndDeduplicate()
    {
        var tracks = new List<AnalyzedTrack>
        {
            new() { Title = "Existing Hit", Artist = "Existing Artist", Genre = "Indie Rock", ReleaseYear = 2020 },
            new() { Title = "Another Track", Artist = "Second Artist", Genre = "Dream Pop", ReleaseYear = 2018 }
        };

        var factory = CreateStubFactory(term => new[]
        {
            ("Existing Hit", "Different Artist", term),
            ("Fresh Track", "Existing Artist", term),
            ("Duplicate Track", "Fresh Artist", term),
            ("Duplicate Track", "Fresh Artist", term),
            ("Valid Pick", "Fresh Artist 2", term)
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var engine = new RecommendationEngine(factory, cache, NullLogger<RecommendationEngine>.Instance);

        var recs = await engine.GenerateRecommendationsAsync(tracks, count: 8);

        Assert.DoesNotContain(recs, r => r.Title == "Existing Hit");
        Assert.DoesNotContain(recs, r => r.Artist == "Existing Artist");
        Assert.Equal(1, recs.Count(r => r.Title == "Duplicate Track" && r.Artist == "Fresh Artist"));
        Assert.Contains(recs, r => r.Title == "Valid Pick" && r.Artist == "Fresh Artist 2");
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
