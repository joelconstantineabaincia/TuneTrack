namespace TuneTrack.Tests;

using TuneTrack.Services.Samples;
using Xunit;

public class SamplePlaylistServiceTests
{
    private readonly SamplePlaylistService _service = new();

    [Fact]
    public void AllSamples_HaveSamplePlatformAndLocalCoverUrls()
    {
        var samples = _service.GetSamplePlaylists();
        Assert.Equal(3, samples.Count);

        foreach (var sample in samples)
        {
            Assert.Equal("Sample", sample.Platform);
            Assert.StartsWith("/images/samples/", sample.CoverUrl);
            Assert.DoesNotContain("http", sample.CoverUrl, StringComparison.OrdinalIgnoreCase);

            foreach (var track in sample.Tracks)
            {
                if (!string.IsNullOrEmpty(track.ArtworkUrl))
                {
                    Assert.StartsWith("/images/samples/", track.ArtworkUrl);
                }
            }
        }
    }

    [Fact]
    public void AllSamples_HaveDescriptionsComputedFromActualTracks()
    {
        foreach (var sample in _service.GetSamplePlaylists())
        {
            int trackCount = sample.Tracks.Count;
            int minYear = sample.Tracks.Where(t => t.ReleaseYear.HasValue).Min(t => t.ReleaseYear!.Value);
            int maxYear = sample.Tracks.Where(t => t.ReleaseYear.HasValue).Max(t => t.ReleaseYear!.Value);
            int genreCount = sample.Tracks.Select(t => t.Genre).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int artistCount = sample.Tracks.Select(t => t.Artist).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            Assert.Contains($"{trackCount} track", sample.Description);
            Assert.Contains($"{minYear}–{maxYear}", sample.Description);
            Assert.Contains($"{genreCount} genre", sample.Description);
            Assert.Contains($"{artistCount} artist", sample.Description);
        }
    }
}
