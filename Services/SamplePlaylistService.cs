namespace TuneTrack.Services;

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
        new SamplePlaylistDefinition(
            Id: "eclectic-horizon",
            Title: "Eclectic Horizon: Multi-Genre Exploration",
            Platform: "Spotify",
            Description: "High Diversity: 12 tracks spanning 1959–2024 across 10 distinct musical genres and 12 artists.",
            CoverUrl: "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=600&q=80",
            DiversityBadge: "High Diversity",
            Tracks: new List<AnalyzedTrack>
            {
                new() { Title = "Time", Artist = "Pink Floyd", Album = "The Dark Side of the Moon", Genre = "Progressive Rock", ReleaseYear = 1973, DurationMs = 413000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "So What", Artist = "Miles Davis", Album = "Kind of Blue", Genre = "Jazz", ReleaseYear = 1959, DurationMs = 562000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "Give Up the Funk", Artist = "Parliament", Album = "Mothership Connection", Genre = "Funk", ReleaseYear = 1975, DurationMs = 345000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "Nightcall", Artist = "Kavinsky", Album = "OutRun", Genre = "Synthwave", ReleaseYear = 2010, DurationMs = 259000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "King Kunta", Artist = "Kendrick Lamar", Album = "To Pimp a Butterfly", Genre = "Hip-Hop", ReleaseYear = 2015, DurationMs = 234000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "White Winter Hymnal", Artist = "Fleet Foxes", Album = "Fleet Foxes", Genre = "Indie Folk", ReleaseYear = 2008, DurationMs = 147000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "Water No Get Enemy", Artist = "Fela Kuti", Album = "Expensive Shit", Genre = "Afrobeat", ReleaseYear = 1975, DurationMs = 660000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "Could You Be Loved", Artist = "Bob Marley & The Wailers", Album = "Uprising", Genre = "Reggae", ReleaseYear = 1980, DurationMs = 237000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "An Ending (Ascent)", Artist = "Brian Eno", Album = "Apollo", Genre = "Ambient", ReleaseYear = 1983, DurationMs = 264000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "The Girl from Ipanema", Artist = "Stan Getz & João Gilberto", Album = "Getz/Gilberto", Genre = "Bossa Nova", ReleaseYear = 1964, DurationMs = 324000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "MALAMENTE", Artist = "Rosalía", Album = "El Mal Querer", Genre = "Flamenco Pop", ReleaseYear = 2018, DurationMs = 150000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" },
                new() { Title = "Harder, Better, Faster, Stronger", Artist = "Daft Punk", Album = "Discovery", Genre = "Electronic", ReleaseYear = 2001, DurationMs = 224000, ArtworkUrl = "https://images.unsplash.com/photo-1511671782779-c97d3d27a1d4?auto=format&fit=crop&w=300&q=80" }
            }
        ),
        new SamplePlaylistDefinition(
            Id: "strictly-synthpop",
            Title: "Strictly Synth-Pop: Neon Nightdrive",
            Platform: "Apple Music",
            Description: "Low Diversity: 10 tracks within a single genre from 2020–2023 by only 2 artists.",
            CoverUrl: "https://images.unsplash.com/photo-1508700115892-45ecd05ae2ad?auto=format&fit=crop&w=600&q=80",
            DiversityBadge: "Low Diversity",
            Tracks: new List<AnalyzedTrack>
            {
                new() { Title = "He Said She Said", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 191000 },
                new() { Title = "How Not To Drown", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 271000 },
                new() { Title = "Good Girls", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 199000 },
                new() { Title = "Asking for a Friend", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 305000 },
                new() { Title = "California Day", Artist = "CHVRCHES", Album = "Screen Violence", Genre = "Synth-Pop", ReleaseYear = 2021, DurationMs = 217000 },
                new() { Title = "Sunset", Artist = "The Midnight", Album = "Endless Summer", Genre = "Synth-Pop", ReleaseYear = 2020, DurationMs = 326000 },
                new() { Title = "Vampires", Artist = "The Midnight", Album = "Endless Summer", Genre = "Synth-Pop", ReleaseYear = 2020, DurationMs = 317000 },
                new() { Title = "Gloria", Artist = "The Midnight", Album = "Monsters", Genre = "Synth-Pop", ReleaseYear = 2020, DurationMs = 374000 },
                new() { Title = "Prom Night", Artist = "The Midnight", Album = "Monsters", Genre = "Synth-Pop", ReleaseYear = 2020, DurationMs = 315000 },
                new() { Title = "Heartbeat", Artist = "The Midnight", Album = "Heroes", Genre = "Synth-Pop", ReleaseYear = 2022, DurationMs = 278000 }
            }
        ),
        new SamplePlaylistDefinition(
            Id: "indie-rock-odyssey",
            Title: "Modern Alt & Indie Wave",
            Platform: "YouTube Music",
            Description: "Balanced Diversity: 10 indie tracks from 2012–2023 across 8 unique modern bands.",
            CoverUrl: "https://images.unsplash.com/photo-1470225620780-dba8ba36b745?auto=format&fit=crop&w=600&q=80",
            DiversityBadge: "Balanced Vibe",
            Tracks: new List<AnalyzedTrack>
            {
                new() { Title = "Kyoto", Artist = "Phoebe Bridgers", Album = "Punisher", Genre = "Indie Rock", ReleaseYear = 2020, DurationMs = 184000 },
                new() { Title = "The Adults Are Talking", Artist = "The Strokes", Album = "The New Abnormal", Genre = "Garage Rock", ReleaseYear = 2020, DurationMs = 309000 },
                new() { Title = "Ode to Viceroy", Artist = "Mac DeMarco", Album = "2", Genre = "Jangle Pop", ReleaseYear = 2012, DurationMs = 233000 },
                new() { Title = "Not Strong Enough", Artist = "boygenius", Album = "the record", Genre = "Indie Folk", ReleaseYear = 2023, DurationMs = 234000 },
                new() { Title = "Jackie Down The Line", Artist = "Fontaines D.C.", Album = "Skinty Fia", Genre = "Post-Punk", ReleaseYear = 2022, DurationMs = 241000 },
                new() { Title = "Chaise Longue", Artist = "Wet Leg", Album = "Wet Leg", Genre = "Indie Rock", ReleaseYear = 2021, DurationMs = 196000 },
                new() { Title = "Archie, Marry Me", Artist = "Alvvays", Album = "Alvvays", Genre = "Dream Pop", ReleaseYear = 2014, DurationMs = 196000 },
                new() { Title = "Be Sweet", Artist = "Japanese Breakfast", Album = "Jubilee", Genre = "Indie Pop", ReleaseYear = 2021, DurationMs = 195000 },
                new() { Title = "Motion Sickness", Artist = "Phoebe Bridgers", Album = "Stranger in the Alps", Genre = "Indie Rock", ReleaseYear = 2017, DurationMs = 229000 },
                new() { Title = "Brooklyn Bridge to Chorus", Artist = "The Strokes", Album = "The New Abnormal", Genre = "New Wave", ReleaseYear = 2020, DurationMs = 235000 }
            }
        )
    };

    public IReadOnlyList<SamplePlaylistDefinition> GetSamplePlaylists() => Samples;

    public SamplePlaylistDefinition? GetSamplePlaylist(string id) =>
        Samples.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
