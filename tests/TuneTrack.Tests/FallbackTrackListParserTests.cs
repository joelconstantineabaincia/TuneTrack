namespace TuneTrack.Tests;

using TuneTrack.Services.Providers;
using Xunit;

public class FallbackTrackListParserTests
{
    [Fact]
    public void ParseTrackList_AcceptsDotAndParenNumbering()
    {
        string raw = """
            1. Daft Punk - One More Time
            2) Kendrick Lamar - Alright
            3)Fleet Foxes - White Winter Hymnal
            04 - Miles Davis - So What
            """;

        var tracks = FallbackTrackListParser.ParseTrackList(raw);

        Assert.Equal(4, tracks.Count);
        Assert.Equal(("Daft Punk", "One More Time"), (tracks[0].Artist, tracks[0].Title));
        Assert.Equal(("Kendrick Lamar", "Alright"), (tracks[1].Artist, tracks[1].Title));
        Assert.Equal(("Fleet Foxes", "White Winter Hymnal"), (tracks[2].Artist, tracks[2].Title));
        Assert.Equal(("Miles Davis", "So What"), (tracks[3].Artist, tracks[3].Title));
    }

    [Fact]
    public void ParseTrackList_PreservesNumericArtistNames()
    {
        string raw = """
            50 Cent - In Da Club
            1. 21 Savage - a lot
            2) 3 Doors Down - Kryptonite
            """;

        var tracks = FallbackTrackListParser.ParseTrackList(raw);

        Assert.Equal(3, tracks.Count);
        Assert.Equal(("50 Cent", "In Da Club"), (tracks[0].Artist, tracks[0].Title));
        Assert.Equal(("21 Savage", "a lot"), (tracks[1].Artist, tracks[1].Title));
        Assert.Equal(("3 Doors Down", "Kryptonite"), (tracks[2].Artist, tracks[2].Title));
    }

    [Fact]
    public void ParseTrackList_HandlesDashesBlankLinesAndExtraWhitespace()
    {
        string raw = "\n   \n  Daft Punk   -   Around the World  \n\t\nKavinsky – Nightcall\nPink Floyd—Time\n   \nJay-Z   -   Empire State of Mind\n";

        var tracks = FallbackTrackListParser.ParseTrackList(raw);

        Assert.Equal(4, tracks.Count);
        Assert.Equal(("Daft Punk", "Around the World"), (tracks[0].Artist, tracks[0].Title));
        Assert.Equal(("Kavinsky", "Nightcall"), (tracks[1].Artist, tracks[1].Title));
        Assert.Equal(("Pink Floyd", "Time"), (tracks[2].Artist, tracks[2].Title));
        Assert.Equal(("Jay-Z", "Empire State of Mind"), (tracks[3].Artist, tracks[3].Title));
    }
}
