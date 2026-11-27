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

    private static string ExtractTerm(string query)
    {
        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("term=", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[5..]).Replace('+', ' ');
            }
        }
        return string.Empty;
    }

    [Fact]
    public async Task LookupTrackAsync_RetriesWithBackoffOnHttp429AndSucceeds()
    {
        int attempts = 0;
        var handler = new StubHandler(_ =>
        {
            attempts++;
            if (attempts <= 2)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            string json = JsonSerializer.Serialize(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        trackName = "Anything",
                        artistName = "Phoebe Bridgers",
                        collectionName = "Stranger in the Alps",
                        primaryGenreName = "Alternative",
                        releaseDate = "2017-09-22T00:00:00Z",
                        artworkUrl100 = "https://is1-ssl.mzstatic.com/image/pb/100x100bb.jpg",
                        previewUrl = "https://audio-ssl.itunes.apple.com/pb.m4a"
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        var result = await enricher.LookupTrackAsync("Phoebe Bridgers", "Anything");

        Assert.Equal(3, attempts);
        Assert.NotNull(result);
        Assert.Equal("Alternative", result!.Genre);
    }

    [Fact]
    public async Task LookupTrackAsync_RetriesOnTimeoutAndDoesNotCacheTransientFailure()
    {
        int timeoutTrackCalls = 0;
        int exhaustedCalls = 0;
        var handler = new AsyncStubHandler(async (req, _) =>
        {
            string query = req.RequestUri?.Query ?? string.Empty;
            if (query.Contains("TimeoutTrack", StringComparison.OrdinalIgnoreCase))
            {
                timeoutTrackCalls++;
                if (timeoutTrackCalls == 1)
                {
                    await Task.CompletedTask;
                    throw new TaskCanceledException("Simulated HTTP timeout");
                }

                string okJson = JsonSerializer.Serialize(new
                {
                    resultCount = 1,
                    results = new[]
                    {
                        new
                        {
                            trackName = "TimeoutTrack",
                            artistName = "Slowdive",
                            collectionName = "Souvlaki",
                            primaryGenreName = "Alternative",
                            releaseDate = "1993-05-11T00:00:00Z",
                            artworkUrl100 = "https://is1-ssl.mzstatic.com/image/sd/100x100bb.jpg",
                            previewUrl = "https://audio-ssl.itunes.apple.com/sd.m4a"
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(okJson, Encoding.UTF8, "application/json")
                };
            }

            exhaustedCalls++;
            if (exhaustedCalls <= 3)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            string recoveredJson = JsonSerializer.Serialize(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        trackName = "RecoveredSong",
                        artistName = "Alvvays",
                        collectionName = "Blue Rev",
                        primaryGenreName = "Alternative",
                        releaseDate = "2022-10-07T00:00:00Z",
                        artworkUrl100 = "https://is1-ssl.mzstatic.com/image/al/100x100bb.jpg",
                        previewUrl = "https://audio-ssl.itunes.apple.com/al.m4a"
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(recoveredJson, Encoding.UTF8, "application/json")
            };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        var timedOutThenOk = await enricher.LookupTrackAsync("Slowdive", "TimeoutTrack");
        Assert.NotNull(timedOutThenOk);
        Assert.Equal(2, timeoutTrackCalls);

        // First call exhausts retries on 429 -> returns null without poisoning cache
        var firstAttempt = await enricher.LookupTrackAsync("Alvvays", "RecoveredSong");
        Assert.Null(firstAttempt);

        // Next call should hit HTTP again (not negative cache) and recover
        var secondAttempt = await enricher.LookupTrackAsync("Alvvays", "RecoveredSong");
        Assert.NotNull(secondAttempt);
        Assert.Equal("Alternative", secondAttempt!.Genre);
    }

    [Fact]
    public async Task LookupTrackAsync_FallsBackToFirstListedArtistThenTitleOnlyWithArtistMatch()
    {
        var seenTerms = new List<string>();
        var handler = new StubHandler(req =>
        {
            string decodedQuery = Uri.UnescapeDataString(req.RequestUri?.Query ?? string.Empty);
            seenTerms.Add(decodedQuery);

            if (decodedQuery.Contains("term=The Avalanches ", StringComparison.OrdinalIgnoreCase) &&
                !decodedQuery.Contains("Loaded Honey", StringComparison.OrdinalIgnoreCase))
            {
                string json = JsonSerializer.Serialize(new
                {
                    resultCount = 1,
                    results = new[]
                    {
                        new
                        {
                            trackName = "I Give Everything I Have 2 U",
                            artistName = "The Avalanches",
                            collectionName = "Single",
                            primaryGenreName = "Electronic",
                            releaseDate = "2024-01-01T00:00:00Z",
                            artworkUrl100 = "https://is1-ssl.mzstatic.com/image/av/100x100bb.jpg",
                            previewUrl = "https://audio-ssl.itunes.apple.com/av.m4a"
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }

            if (decodedQuery.Contains("term=1901&", StringComparison.OrdinalIgnoreCase))
            {
                string json = JsonSerializer.Serialize(new
                {
                    resultCount = 2,
                    results = new[]
                    {
                        new
                        {
                            trackName = "1901",
                            artistName = "Tribute Band",
                            collectionName = "Covers",
                            primaryGenreName = "Pop",
                            releaseDate = "2015-01-01T00:00:00Z",
                            artworkUrl100 = "https://is1-ssl.mzstatic.com/image/x/100x100bb.jpg",
                            previewUrl = "https://audio-ssl.itunes.apple.com/x.m4a"
                        },
                        new
                        {
                            trackName = "1901",
                            artistName = "Phoenix",
                            collectionName = "Wolfgang Amadeus Phoenix",
                            primaryGenreName = "Alternative",
                            releaseDate = "2009-05-25T00:00:00Z",
                            artworkUrl100 = "https://is1-ssl.mzstatic.com/image/ph/100x100bb.jpg",
                            previewUrl = "https://audio-ssl.itunes.apple.com/ph.m4a"
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }

            if (decodedQuery.Contains("term=Missing Only Song&", StringComparison.OrdinalIgnoreCase))
            {
                string json = JsonSerializer.Serialize(new
                {
                    resultCount = 1,
                    results = new[]
                    {
                        new
                        {
                            trackName = "Missing Only Song",
                            artistName = "Completely Different Artist",
                            collectionName = "Other",
                            primaryGenreName = "Rock",
                            releaseDate = "2020-01-01T00:00:00Z",
                            artworkUrl100 = "https://is1-ssl.mzstatic.com/image/o/100x100bb.jpg",
                            previewUrl = "https://audio-ssl.itunes.apple.com/o.m4a"
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }

            string empty = JsonSerializer.Serialize(new { resultCount = 0, results = Array.Empty<object>() });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(empty, Encoding.UTF8, "application/json") };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        var collabResult = await enricher.LookupTrackAsync("The Avalanches, Loaded Honey", "I Give Everything I Have 2 U");
        Assert.NotNull(collabResult);
        Assert.Equal("Electronic", collabResult!.Genre);

        var titleOnlyResult = await enricher.LookupTrackAsync("Phoenix", "1901");
        Assert.NotNull(titleOnlyResult);
        Assert.Equal("Alternative", titleOnlyResult!.Genre);

        var rejectedTitleOnly = await enricher.LookupTrackAsync("Expected Indie Artist", "Missing Only Song");
        Assert.Null(rejectedTitleOnly);
    }

    [Fact]
    public async Task LookupTrackAsync_CleansEditionVersionAndFeatureSuffixesWithoutDroppingRealParens()
    {
        var seenTerms = new List<string>();
        var handler = new StubHandler(req =>
        {
            seenTerms.Add(Uri.UnescapeDataString(req.RequestUri?.Query ?? string.Empty));
            string okJson = JsonSerializer.Serialize(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        trackName = "Track",
                        artistName = "MatchArtist",
                        collectionName = "Album",
                        primaryGenreName = "Rock",
                        releaseDate = "2011-01-01T00:00:00Z",
                        artworkUrl100 = "https://is1-ssl.mzstatic.com/image/t/100x100bb.jpg",
                        previewUrl = "https://audio-ssl.itunes.apple.com/t.m4a"
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(okJson, Encoding.UTF8, "application/json") };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        await enricher.LookupTrackAsync("MatchArtist", "Space Oddity - Remastered 2011");
        await enricher.LookupTrackAsync("MatchArtist", "Take On Me - Single Version");
        await enricher.LookupTrackAsync("MatchArtist", "Hotel California (Live)");
        await enricher.LookupTrackAsync("MatchArtist", "Blood Bank - Spotify Singles");
        await enricher.LookupTrackAsync("MatchArtist", "Someone Like You (feat. Guest Artist)");
        await enricher.LookupTrackAsync("MatchArtist", "All I Wanted (stay)");
        await enricher.LookupTrackAsync("MatchArtist", "I Know You Are (But What Am I Thinking)");

        Assert.Contains("term=MatchArtist Space Oddity&", seenTerms[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("term=MatchArtist Take On Me&", seenTerms[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("term=MatchArtist Hotel California&", seenTerms[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("term=MatchArtist Blood Bank&", seenTerms[3], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("term=MatchArtist Someone Like You&", seenTerms[4], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("term=MatchArtist All I Wanted (stay)&", seenTerms[5], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("term=MatchArtist I Know You Are (But What Am I Thinking)&", seenTerms[6], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrichTracksAsync_ProcessesMoreThan30TracksByDefault()
    {
        var handler = new StubHandler(req =>
        {
            string decodedQuery = Uri.UnescapeDataString(req.RequestUri?.Query ?? string.Empty);
            string term = ExtractTerm(decodedQuery);
            string artist = term.Split(' ')[0];
            string json = JsonSerializer.Serialize(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        trackName = "Song",
                        artistName = artist,
                        collectionName = "Album",
                        primaryGenreName = "Rock",
                        releaseDate = "2022-01-01T00:00:00Z",
                        artworkUrl100 = "https://is1-ssl.mzstatic.com/image/r/100x100bb.jpg",
                        previewUrl = "https://audio-ssl.itunes.apple.com/r.m4a"
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        var tracks = Enumerable.Range(1, 35)
            .Select(i => new AnalyzedTrack { Artist = $"Artist{i}", Title = $"Track{i}" })
            .ToList();

        var reported = new List<(int Completed, int Total)>();
        var progress = new SyncProgress<(int Completed, int Total)>(p =>
        {
            lock (reported)
            {
                reported.Add(p);
            }
        });

        await enricher.EnrichTracksAsync(tracks, progress);

        Assert.Equal(35, tracks.Count(t => t.Genre == "Rock"));
        Assert.Contains((0, 35), reported);
        Assert.Contains((35, 35), reported);
        Assert.Equal(36, reported.Count);
    }

    [Fact]
    public async Task LookupTrackAsync_ReleasesThrottleWhenCancelledMidFlight()
    {
        int callCount = 0;
        CancellationTokenSource? activeCts = null;

        var handler = new AsyncStubHandler((req, ct) =>
        {
            callCount++;
            if (callCount <= 2)
            {
                activeCts?.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            string json = JsonSerializer.Serialize(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        trackName = "Song",
                        artistName = "Phoebe Bridgers",
                        collectionName = "Punisher",
                        primaryGenreName = "Alternative",
                        releaseDate = "2020-06-18T00:00:00Z",
                        artworkUrl100 = "https://is1-ssl.mzstatic.com/image/p/100x100bb.jpg",
                        previewUrl = "https://audio-ssl.itunes.apple.com/p.m4a"
                    }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        for (int i = 1; i <= 2; i++)
        {
            using var cts = new CancellationTokenSource();
            activeCts = cts;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => enricher.LookupTrackAsync($"CancelledArtist{i}", $"CancelledTitle{i}", cts.Token));
        }

        using var verifyCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var recovered = await enricher.LookupTrackAsync("Phoebe Bridgers", "Kyoto", verifyCts.Token);

        Assert.NotNull(recovered);
        Assert.Equal("Alternative", recovered!.Genre);
    }

    [Fact]
    public async Task LookupTrackAsync_RetriesUnmatchedTrackWithPhStorefrontAndCachesResult()
    {
        var seenQueries = new List<string>();
        var handler = new StubHandler(req =>
        {
            string query = req.RequestUri?.Query ?? string.Empty;
            seenQueries.Add(query);

            if (query.Contains("country=PH", StringComparison.OrdinalIgnoreCase) &&
                query.Contains("Hev", StringComparison.OrdinalIgnoreCase))
            {
                string phJson = JsonSerializer.Serialize(new
                {
                    resultCount = 1,
                    results = new[]
                    {
                        new
                        {
                            trackName = "Para Sa Streets",
                            artistName = "Hev Abi",
                            collectionName = "Sakalam",
                            primaryGenreName = "Hip-Hop",
                            releaseDate = "2023-06-02T00:00:00Z",
                            artworkUrl100 = "https://is1-ssl.mzstatic.com/image/ha/100x100bb.jpg",
                            previewUrl = "https://audio-ssl.itunes.apple.com/ha.m4a"
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(phJson, Encoding.UTF8, "application/json") };
            }

            if (query.Contains("MatchedUsTrack", StringComparison.OrdinalIgnoreCase))
            {
                string usJson = JsonSerializer.Serialize(new
                {
                    resultCount = 1,
                    results = new[]
                    {
                        new
                        {
                            trackName = "MatchedUsTrack",
                            artistName = "US Artist",
                            collectionName = "US Album",
                            primaryGenreName = "Rock",
                            releaseDate = "2020-01-01T00:00:00Z",
                            artworkUrl100 = "https://is1-ssl.mzstatic.com/image/us/100x100bb.jpg",
                            previewUrl = "https://audio-ssl.itunes.apple.com/us.m4a"
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(usJson, Encoding.UTF8, "application/json") };
            }

            string empty = JsonSerializer.Serialize(new { resultCount = 0, results = Array.Empty<object>() });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(empty, Encoding.UTF8, "application/json") };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        // Matched on default storefront -> never calls country=PH
        var usResult = await enricher.LookupTrackAsync("US Artist", "MatchedUsTrack");
        Assert.NotNull(usResult);
        Assert.DoesNotContain(seenQueries, q => q.Contains("country=PH", StringComparison.OrdinalIgnoreCase));

        // Unmatched on default storefront -> falls back to country=PH and caches match
        var phResult = await enricher.LookupTrackAsync("Hev Abi", "Para Sa Streets");
        Assert.NotNull(phResult);
        Assert.Equal("Hip-Hop/Rap", phResult!.Genre);
        Assert.Contains(seenQueries, q => q.Contains("country=PH", StringComparison.OrdinalIgnoreCase));

        int callsBeforeCacheHit = seenQueries.Count;
        var cachedPhResult = await enricher.LookupTrackAsync("Hev Abi", "Para Sa Streets");
        Assert.NotNull(cachedPhResult);
        Assert.Equal("Hip-Hop/Rap", cachedPhResult!.Genre);
        Assert.Equal(callsBeforeCacheHit, seenQueries.Count);
    }

    [Fact]
    public async Task LookupTrackAsync_PhRetryUsesBackoffOn429AndStillEnforcesArtistMatch()
    {
        int phAttempts = 0;
        var handler = new StubHandler(req =>
        {
            string query = req.RequestUri?.Query ?? string.Empty;

            if (query.Contains("country=PH", StringComparison.OrdinalIgnoreCase))
            {
                if (query.Contains("Flow", StringComparison.OrdinalIgnoreCase))
                {
                    phAttempts++;
                    if (phAttempts == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    }

                    string phMatchJson = JsonSerializer.Serialize(new
                    {
                        resultCount = 1,
                        results = new[]
                        {
                            new
                            {
                                trackName = "Driftstar",
                                artistName = "Flow G",
                                collectionName = "Single",
                                primaryGenreName = "Hip-Hop/Rap",
                                releaseDate = "2024-02-01T00:00:00Z",
                                artworkUrl100 = "https://is1-ssl.mzstatic.com/image/fg/100x100bb.jpg",
                                previewUrl = "https://audio-ssl.itunes.apple.com/fg.m4a"
                            }
                        }
                    });
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(phMatchJson, Encoding.UTF8, "application/json") };
                }

                string wrongArtistJson = JsonSerializer.Serialize(new
                {
                    resultCount = 1,
                    results = new[]
                    {
                        new
                        {
                            trackName = "Some OPM Song",
                            artistName = "Wrong Karaoke Band",
                            collectionName = "Covers",
                            primaryGenreName = "Pop",
                            releaseDate = "2021-01-01T00:00:00Z",
                            artworkUrl100 = "https://is1-ssl.mzstatic.com/image/w/100x100bb.jpg",
                            previewUrl = "https://audio-ssl.itunes.apple.com/w.m4a"
                        }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(wrongArtistJson, Encoding.UTF8, "application/json") };
            }

            string empty = JsonSerializer.Serialize(new { resultCount = 0, results = Array.Empty<object>() });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(empty, Encoding.UTF8, "application/json") };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        var matched = await enricher.LookupTrackAsync("Flow G", "Driftstar");
        Assert.NotNull(matched);
        Assert.Equal(2, phAttempts);
        Assert.Equal("Hip-Hop/Rap", matched!.Genre);

        var rejected = await enricher.LookupTrackAsync("Expected OPM Artist", "Some OPM Song");
        Assert.Null(rejected);
    }

    [Fact]
    public async Task LookupTrackAsync_MatchesHyphenatedAndReorderedCollaboratorArtists()
    {
        var factory = CreateStubFactory(query =>
        {
            string decoded = Uri.UnescapeDataString(query);
            if (decoded.Contains("Driftstar", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new StubCandidate("Driftstar", "Flow-G", "Single", "Hip-Hop/Rap", "2024-02-01T00:00:00Z",
                        "https://is1-ssl.mzstatic.com/image/fg/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/fg.m4a")
                };
            }
            if (decoded.Contains("Wag Ipagsabi", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new StubCandidate("Wag Ipagsabi (Live at The Cozy Cove)", "Bert Symoun & Dreycruz", "Live", "Pop", "2024-06-01T00:00:00Z",
                        "https://is1-ssl.mzstatic.com/image/db/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/db.m4a")
                };
            }
            if (decoded.Contains("Dominga", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new StubCandidate("Dominga", "Nateman & La Mave", "Single", "Hip-Hop/Rap", "2024-04-01T00:00:00Z",
                        "https://is1-ssl.mzstatic.com/image/lm/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/lm.m4a")
                };
            }
            if (decoded.Contains("Unrelated Track", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new StubCandidate("Buti Di Ka Nanatili", "CK YG, Realest Cram & Nateman", "Single", "Hip-Hop/Rap", "2024-01-01T00:00:00Z",
                        "https://is1-ssl.mzstatic.com/image/x/100x100bb.jpg", "https://audio-ssl.itunes.apple.com/x.m4a")
                };
            }
            return Array.Empty<StubCandidate>();
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(factory, cache, NullLogger<TrackEnricherService>.Instance);

        var hyphenMatch = await enricher.LookupTrackAsync("Flow G", "Driftstar");
        Assert.NotNull(hyphenMatch);

        var swappedCollab1 = await enricher.LookupTrackAsync("Dreycruz, Bert Symoun", "Wag Ipagsabi");
        Assert.NotNull(swappedCollab1);

        var swappedCollab2 = await enricher.LookupTrackAsync("La Mave, Nateman", "Dominga");
        Assert.NotNull(swappedCollab2);

        // A single overlapping secondary collaborator when primary differs must still be rejected
        var singleCollabOverlap = await enricher.LookupTrackAsync("Realest Cram, Young Blood Neet", "Unrelated Track");
        Assert.Null(singleCollabOverlap);
    }

    [Fact]
    public async Task LookupTrackAsync_TreatsHttp403AsRateLimitWithoutHammeringOrNegativeCaching()
    {
        int calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            if (calls <= 3)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            string okJson = JsonSerializer.Serialize(new
            {
                resultCount = 1,
                results = new[]
                {
                    new
                    {
                        trackName = "Get Low",
                        artistName = "O SIDE MAFIA & BRGR",
                        collectionName = "Single",
                        primaryGenreName = "Hip-Hop/Rap",
                        releaseDate = "2024-01-01T00:00:00Z",
                        artworkUrl100 = "https://is1-ssl.mzstatic.com/image/os/100x100bb.jpg",
                        previewUrl = "https://audio-ssl.itunes.apple.com/os.m4a"
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(okJson, Encoding.UTF8, "application/json") };
        });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var enricher = new TrackEnricherService(new StubFactory(new HttpClient(handler)), cache, NullLogger<TrackEnricherService>.Instance);

        var blocked = await enricher.LookupTrackAsync("O SIDE MAFIA, BRGR", "Get Low");
        Assert.Null(blocked);
        Assert.Equal(3, calls);

        var recovered = await enricher.LookupTrackAsync("O SIDE MAFIA, BRGR", "Get Low");
        Assert.NotNull(recovered);
        Assert.Equal("Hip-Hop/Rap", recovered!.Genre);
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

    private sealed class AsyncStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }

    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
