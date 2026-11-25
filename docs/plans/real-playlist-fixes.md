# Real-Playlist Results, Enrichment Matching & Paste Flow Plan

> **For agentic workers:** Follow `superpowers:test-driven-development` (RED failing tests first, verify failure, minimal GREEN implementation), `ui-ux-pro-max` (form label/empty state rules), `impeccable` (`detect` verification + rendered 1440px/390px screenshots), and `ponytail` (minimal diff, no new packages).

**Goal:** Improve TuneTrack's real-playlist pipeline across partial-result handling, fallback tracklist parsing & paste persistence, per-track artwork, iTunes genre normalization & fuzzy artist matching, Diversity Score match counts, and best-effort `ytInitialData` YouTube Music extraction.

**Architecture:** Logic fixes stay strictly inside `FallbackTrackListParser`, `TrackEnricherService`, `PlaylistService`, and the three playlist providers (`SpotifyPlaylistProvider`, `AppleMusicPlaylistProvider`, `YouTubeMusicPlaylistProvider`) with TDD unit/HTTP-stub tests under `tests/TuneTrack.Tests/`. UI updates stay localized to `src/TuneTrack/Components/Pages/Home.razor`.

**Tech Stack:** .NET 10 Blazor Web App (InteractiveServer), xUnit, `HttpMessageHandler` test stubs, Tailwind CSS, `impeccable detect`, Puppeteer-core (`scripts/verify-ui.js`).

## Global Constraints
- No new NuGet or npm packages.
- Write failing tests FIRST and confirm each fails before modifying production code.
- `dotnet build` exits 0 with 0 warnings/errors; full `dotnet test` suite passes.
- Verify live links (Spotify + YouTube Music), capture 1440px and 390px screenshots for partial state, pasted result, and Spotify result, and run `impeccable detect` on `/`.

## Review Focus
- `FallbackTrackListParser`: `"50 Cent - In Da Club"` must NOT strip `"50 "` as a track number, whereas `"1. "`, `"1) "`, `"1)"`, and `"01 - "` before `"Artist - Title"` must strip cleanly.
- `TrackEnricherService`: unverified first iTunes hit (wrong artist) must be rejected (`Genre = null`, `ReleaseYear = null`, `ArtworkUrl = null`), not blindly accepted.
- `TrackEnricherService`: iTunes `"Hip-Hop"` and `"Hip-Hop/Rap"` normalize to `"Hip-Hop/Rap"` before `DiversityScorer`, genre chips, and distribution calculations.
- `PlaylistService`: zero-track partial responses must NOT persist to History; pasted tracklists from a partial result retain the playlist's `Title`, `CoverUrl`, and `Platform` and DO save to History.
- `YouTubeMusicPlaylistProvider`: failure/empty `ytInitialData` returns clean `Partial` state without throwing.

---

### Task 1: TDD — `FallbackTrackListParser` Enhancements
**Files:**
- Modify: `tests/TuneTrack.Tests/FallbackTrackListParserTests.cs` (Create)
- Modify: `src/TuneTrack/Services/Providers/FallbackTrackListParser.cs`

**Steps:**
- [ ] Write failing xUnit tests covering: `"1. Artist - Title"`, `"1) Artist - Title"`, `"1)Artist - Title"`, hyphen/en-dash/em-dash (`-`, `–`, `—`) with/without extra spaces, hyphenated artist names (`"Jay-Z - Empire State of Mind"`, `"50 Cent - In Da Club"`), blank/whitespace-only lines, and extra internal whitespace.
- [ ] Run `dotnet test` and confirm the new parser tests fail against existing `FallbackTrackListParser`.
- [ ] Update `FallbackTrackListParser` (`StripLeadingNumber`, separator handling, whitespace normalization) minimally so all parser tests pass.

### Task 2: TDD — `TrackEnricherService` Fuzzy Artist Matching & Genre Normalization
**Files:**
- Create: `tests/TuneTrack.Tests/TrackEnricherServiceTests.cs`
- Modify: `src/TuneTrack/Services/Recommendations/TrackEnricherService.cs`

**Steps:**
- [ ] Write failing xUnit tests (with stubbed `HttpMessageHandler`) covering:
  - Genre normalization mapping (`"Hip-Hop"` and `"Hip-Hop/Rap"` -> `"Hip-Hop/Rap"`, etc.) applied after enrichment.
  - Rejecting non-matching iTunes artist (leaves `Genre`, `ReleaseYear`, `ArtworkUrl` empty).
  - Selecting a matching candidate from `limit=5` ignoring `"feat."`, `"&"`, `"ft."`, case, and partial artist containment.
- [ ] Run `dotnet test` and watch the new tests fail.
- [ ] Implement `NormalizeGenre`, `IsArtistMatch`, `&limit=5` candidate filtering, and post-enrichment normalization in `TrackEnricherService.cs`. Confirm all tests pass.

### Task 3: TDD — Partial Persistence Guard, Paste Preservation & Provider Updates
**Files:**
- Create: `tests/TuneTrack.Tests/PlaylistServiceTests.cs` (using EF InMemory DB + stub providers)
- Modify: `src/TuneTrack/Services/Providers/PlaylistService.cs`
- Modify: `src/TuneTrack/Services/Providers/SpotifyPlaylistProvider.cs`
- Modify: `src/TuneTrack/Services/Providers/AppleMusicPlaylistProvider.cs`
- Modify: `src/TuneTrack/Services/Providers/YouTubeMusicPlaylistProvider.cs`

**Steps:**
- [ ] Write failing tests verifying: partial/0-track URL result is NOT persisted to `AnalyzedPlaylists`; 3-line and 10-line `AnalyzeManualTrackListAsync` calls preserve link `Title`, `CoverUrl`, `Platform` and DO save to History; YouTube `ytInitialData` `- Topic` / `Artist - Title` parsing vs clean partial fallback.
- [ ] Run `dotnet test` and observe expected failures.
- [ ] Remove `ArtworkUrl = coverUrl` repetition from `SpotifyPlaylistProvider` and `AppleMusicPlaylistProvider`; update `PlaylistService` guard; add time-boxed `youtube.com/playlist?list=ID` + cookie + `ytInitialData` parser in `YouTubeMusicPlaylistProvider` with clean fallback. Verify all tests pass.

### Task 4: Partial View, Placeholder, Track Thumbnails & Match Count UI (`Home.razor`)
**Files:**
- Modify: `src/TuneTrack/Components/Pages/Home.razor`

**Steps:**
- [ ] Hide stat strip, filter bar, empty tracklist table, and sidebar when `dashboard.IsPartialResult || showPasteFallback || dashboard.Playlist.Tracks.Count == 0`, showing only header (cover, title, platform badge) and paste card.
- [ ] Rewrite paste card copy to `"{Platform} doesn't share track lists publicly. Paste your tracks, one 'Artist - Title' per line, and we'll analyze them."`, add `<label class="sr-only">`, and bind `placeholder` to a C# string with real `\n` newlines for the 3 unnumbered tracks.
- [ ] Ensure track row thumbnails render neutral placeholder when `track.ArtworkUrl` is missing (never falling back to playlist cover), and show `"Matched X of Y tracks"` near the Diversity Score.

### Task 5: Verification, Real Links, Screenshots, Impeccable Detect & Push
**Steps:**
- [ ] Run `dotnet build` (0 warnings, 0 errors) and full `dotnet test`.
- [ ] Boot local app and run real Spotify & YouTube Music links in headless Chrome; capture `1440px` and `390px` screenshots for partial state, pasted result, and Spotify result; verify 3-line and 10-line paste History persistence; run `impeccable detect` on `/` and fix any findings.
- [ ] Commit with `fix: handle partial playlists, track artwork, and genre matching` and push to `origin/main`.
