namespace TuneTrack.Services.Samples;

using TuneTrack.Models;

public interface ISamplePlaylistService
{
    IReadOnlyList<SamplePlaylistDefinition> GetSamplePlaylists();
    SamplePlaylistDefinition? GetSamplePlaylist(string id);
}

public record SamplePlaylistDefinition(
    string Id,
    string Title,
    string Platform,
    string Description,
    string CoverUrl,
    string DiversityBadge,
    List<AnalyzedTrack> Tracks
);

public class SamplePlaylistService : ISamplePlaylistService
{
    private static readonly List<SamplePlaylistDefinition> Samples = new()
    {
        BuildSample(
            id: "eclectic-horizon",
            title: "Eclectic Horizon: Multi-Genre Exploration",
            coverUrl: "/images/samples/eclectic-horizon.jpg",
            diversityBadge: "High Diversity",
            tracks: new List<AnalyzedTrack>
            {
                new() { Title = "Time", Artist = "Pink Floyd", Album = "The Dark Side of the Moon", Genre = "Progressive Rock", ReleaseYear = 1973, DurationMs = 413000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "So What", Artist = "Miles Davis", Album = "Kind of Blue", Genre = "Jazz", ReleaseYear = 1959, DurationMs = 562000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "Give Up the Funk", Artist = "Parliament", Album = "Mothership Connection", Genre = "Funk", ReleaseYear = 1975, DurationMs = 345000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "Nightcall", Artist = "Kavinsky", Album = "OutRun", Genre = "Synthwave", ReleaseYear = 2010, DurationMs = 259000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "King Kunta", Artist = "Kendrick Lamar", Album = "To Pimp a Butterfly", Genre = "Hip-Hop", ReleaseYear = 2015, DurationMs = 234000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "White Winter Hymnal", Artist = "Fleet Foxes", Album = "Fleet Foxes", Genre = "Indie Folk", ReleaseYear = 2008, DurationMs = 147000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "Water No Get Enemy", Artist = "Fela Kuti", Album = "Expensive Shit", Genre = "Afrobeat", ReleaseYear = 1975, DurationMs = 660000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "Could You Be Loved", Artist = "Bob Marley & The Wailers", Album = "Uprising", Genre = "Reggae", ReleaseYear = 1980, DurationMs = 237000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "An Ending (Ascent)", Artist = "Brian Eno", Album = "Apollo", Genre = "Ambient", ReleaseYear = 1983, DurationMs = 264000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "The Girl from Ipanema", Artist = "Stan Getz & João Gilberto", Album = "Getz/Gilberto", Genre = "Bossa Nova", ReleaseYear = 1964, DurationMs = 324000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "MALAMENTE", Artist = "Rosalía", Album = "El Mal Querer", Genre = "Flamenco Pop", ReleaseYear = 2018, DurationMs = 150000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" },
                new() { Title = "Harder, Better, Faster, Stronger", Artist = "Daft Punk", Album = "Discovery", Genre = "Electronic", ReleaseYear = 2001, DurationMs = 224000, ArtworkUrl = "/images/samples/eclectic-horizon.jpg" }
            }
        ),
        BuildSample(
            id: "strictly-synthpop",
            title: "Strictly Synth-Pop: Neon Nightdrive",
            coverUrl: "/images/samples/strictly-synthpop.jpg",
            diversityBadge: "Low Diversity",
            tracks: new List<AnalyzedTrack>
            {
                new() { Title = "He Said She Said", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 191000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "How Not To Drown", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 271000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "Good Girls", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 199000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "Asking for a Friend", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 305000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "California Day", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 217000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "Sunset", Artist = "The Midnight", Album = "Endless Summer", Genre = "Synth-Pop", ReleaseYear = 2020, DurationMs = 326000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "Vampires", Artist = "The Midnight", Album = "Endless Summer", Genre = "Synth-Pop", ReleaseYear = 2020, DurationMs = 317000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "Gloria", Artist = "The Midnight", Album = "Monsters", Genre = "Synth-Pop", ReleaseYear = 2020, DurationMs = 374000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "Prom Night", Artist = "The Midnight", Album = "Monsters", Genre = "Synth-Pop", ReleaseYear = 2020, DurationMs = 315000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" },
                new() { Title = "Heartbeat", Artist = "The Midnight", Album = "Heroes", Genre = "Synth-Pop", ReleaseYear = 2022, DurationMs = 278000, ArtworkUrl = "/images/samples/strictly-synthpop.jpg" }
            }
        ),
        BuildSample(
            id: "indie-rock-odyssey",
            title: "Modern Alt & Indie Wave",
            coverUrl: "/images/samples/modern-indie.jpg",
            diversityBadge: "Balanced Vibe",
            tracks: new List<AnalyzedTrack>
            {
                new() { Title = "Kyoto", Artist = "Phoebe Bridgers", Album = "Punisher", Genre = "Indie Rock", ReleaseYear = 2020, DurationMs = 184000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "The Adults Are Talking", Artist = "The Strokes", Album = "The New Abnormal", Genre = "Garage Rock", ReleaseYear = 2020, DurationMs = 309000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "Ode to Viceroy", Artist = "Mac DeMarco", Album = "2", Genre = "Jangle Pop", ReleaseYear = 2012, DurationMs = 233000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "Not Strong Enough", Artist = "boygenius", Album = "the record", Genre = "Indie Folk", ReleaseYear = 2023, DurationMs = 234000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "Jackie Down The Line", Artist = "Fontaines D.C.", Album = "Skinty Fia", Genre = "Post-Punk", ReleaseYear = 2022, DurationMs = 241000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "Chaise Longue", Artist = "Wet Leg", Album = "Wet Leg", Genre = "Indie Rock", ReleaseYear = 2021, DurationMs = 196000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "Archie, Marry Me", Artist = "Alvvays", Album = "Alvvays", Genre = "Dream Pop", ReleaseYear = 2014, DurationMs = 196000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "Be Sweet", Artist = "Japanese Breakfast", Album = "Jubilee", Genre = "Indie Pop", ReleaseYear = 2021, DurationMs = 195000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "Motion Sickness", Artist = "Phoebe Bridgers", Album = "Stranger in the Alps", Genre = "Indie Rock", ReleaseYear = 2017, DurationMs = 229000, ArtworkUrl = "/images/samples/modern-indie.jpg" },
                new() { Title = "Brooklyn Bridge to Chorus", Artist = "The Strokes", Album = "The New Abnormal", Genre = "New Wave", ReleaseYear = 2020, DurationMs = 235000, ArtworkUrl = "/images/samples/modern-indie.jpg" }
            }
        )
    };

    private static SamplePlaylistDefinition BuildSample(
        string id,
        string title,
        string coverUrl,
        string diversityBadge,
        List<AnalyzedTrack> tracks)
    {
        int trackCount = tracks.Count;
        var years = tracks
            .Where(t => t.ReleaseYear.HasValue && t.ReleaseYear.Value > 0)
            .Select(t => t.ReleaseYear!.Value)
            .ToList();

        string yearRange = years.Count == 0
            ? "various years"
            : years.Min() == years.Max()
                ? years.Min().ToString()
                : $"{years.Min()}–{years.Max()}";

        int genreCount = tracks
            .Select(t => t.Genre?.Trim())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int artistCount = tracks
            .Select(t => t.Artist.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        string description =
            $"{diversityBadge}: {trackCount} {(trackCount == 1 ? "track" : "tracks")} " +
            $"spanning {yearRange} across {genreCount} {(genreCount == 1 ? "genre" : "genres")} " +
            $"and {artistCount} {(artistCount == 1 ? "artist" : "artists")}.";

        return new SamplePlaylistDefinition(
            Id: id,
            Title: title,
            Platform: "Sample",
            Description: description,
            CoverUrl: coverUrl,
            DiversityBadge: diversityBadge,
            Tracks: tracks);
    }

    public IReadOnlyList<SamplePlaylistDefinition> GetSamplePlaylists() => Samples;

    public SamplePlaylistDefinition? GetSamplePlaylist(string id) =>
        Samples.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
