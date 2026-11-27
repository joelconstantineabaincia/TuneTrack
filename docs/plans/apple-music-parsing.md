# Apple Music Playlist Parsing & iTunes Batch Lookup Plan

> **For agentic workers:** Follow `test-driven-development` (failing tests first, confirm RED, minimal GREEN implementation), `ponytail` (shared helpers, shallow nesting, minimal diff), `impeccable` (`detect` + rendered 1440px/390px screenshots), and `verification-before-completion`.

**Goal:** Fix Apple Music playlist parsing across any public `music.apple.com` storefront by extracting real titles, artists, albums, durations, and numeric track IDs from structured HTML data, resolving track metadata in batches via Apple's keyless `itunes.apple.com/lookup` endpoint, gracefully handling partial/unknown-artist/album/song URLs, and keeping header/score/history metrics consistent when track metadata is sparse or empty.

**Architecture:** Extract structured track data from `<script id="serialized-server-data">` (with `<meta property="music:song">` / JSON-LD ID fallback) in `AppleMusicPlaylistProvider`, derive `&country=` from the URL storefront segment, add batch ID lookup (`LookupTracksByIdsAsync`) onto `ITrackEnricherService`/`TrackEnricherService` sharing its existing `IMemoryCache`, `SemaphoreSlim(1, 1)` throttle, retry backoff, and genre/artwork normalization, guard zero-metadata History persistence in `PlaylistService`, and align empty/low-coverage display in `Home.razor`.

**Tech Stack:** .NET 10 Blazor Web App (InteractiveServer), xUnit + `HttpMessageHandler` stubs, Tailwind CSS, Puppeteer-core (`verify-ui`), Impeccable CLI.

---

## Saved HTML Findings (`https://music.apple.com/ca/playlist/apple-music-list/pl.41fda5adbf9b4db4ba1a2c0a76099ef9`)

- Plain HTTP GET with a desktop `User-Agent` returns ~261k characters of HTML containing three complementary data sources:
  1. **`<script type="application/json" id="serialized-server-data">`** (~100k chars):
     - `data[0].data.sections` contains the header section (`itemKind: "containerDetailHeaderLockup"`, `items[0].title`, `items[0].artwork.dictionary.url`) and tracklist section (`itemKind: "trackLockup"`, 16 `items`).
     - Each `trackLockup` item holds:
       - `title`: real track title with punctuation and featuring tags (e.g., `"rockstar (feat. 21 Savage)"`, `"Good Old Days (feat. Kesha)"`, `"1-800-273-8255 (feat. Alessia Cara & Khalid)"`).
       - `subtitleLinks`: `[{ "title": "..." }]` ordered artist list (`["J Balvin", "Willy William"]`, `["ALMA", "French Montana"]`); `artistName` provides single/formatted string fallback (`"Post Malone"`, `"J Balvin & Willy William"`).
       - `tertiaryLinks[0].title`: album title (`"The Diamond Collection (Deluxe)"`, `"GEMINI"`).
       - `duration`: integer duration in milliseconds (`186939`, `218146`).
       - `showExplicitBadge`: boolean.
       - Numeric track ID: `contentDescriptor.identifiers.storeAdamID` (`"1444327839"`), `contentDescriptor.url` (`.../1444327818?i=1444327839`), and `impressionMetrics.fields.id`.
  2. **`<script id="schema:music-playlist" type="application/ld+json">`** (~11k chars): `name`, `numTracks`, and `track[]` with `name`, `duration` (ISO-8601), and `url` (`https://music.apple.com/ca/song/.../{id}` — note JSON-LD omits artist names).
  3. **`<meta property="music:song">` / `<meta property="music:song:duration">`** tags + `og:title` / `og:image`: ordered song URLs containing numeric track IDs and durations, omitting artists.
- Verified identical `serialized-server-data` schema on other storefronts (`https://music.apple.com/us/playlist/top-25-new-york-city/pl.a88b5c26caea48a59484370b6f79c9df`) and verified album (`/album/...`) / single-song (`/song/...`) HTML use distinct `CanHandle` / section shapes that should resolve gracefully to tuneable partial state instead of failing with an unsupported host error.
- Verified keyless batch lookup `https://itunes.apple.com/lookup?id=1444327839,1698400133&country=ca` returns HTTP 200 JSON (`wrapperType="track"`, `kind="song"`, `trackId`, `trackName`, `artistName`, `collectionName`, `primaryGenreName`, `releaseDate`, `artworkUrl100`, `previewUrl`, `trackTimeMillis`).

---

## Global Constraints
- No playlist-specific values in production code (no hardcoded IDs, titles, artists, or storefront).
- Derive the storefront dynamically from the URL path (fallback null if omitted).
- Cover playlists with/without featured artists, explicit badges, unknown/missing artists (`"Unknown artist"` / empty -> partial fallback if majority missing), and album/single-song URLs returning partial paste state gracefully.
- Leave `<UserSecretsId>` changes in `src/TuneTrack/TuneTrack.csproj` unstaged.
- `dotnet build` exits 0 with 0 warnings; full `dotnet test` suite passes; run `impeccable detect` and capture 1440px/390px screenshots.

## Review Focus
- Comma-separated artist join from `subtitleLinks` vs fallback to `artistName` (preserving `"J Balvin, Willy William"` and `"ALMA, French Montana"`, without inventing `"Apple Music Artist"`).
- Storefront-scoped cache keys (`itunes_id_{country}_{trackId}`) so `/ca/` and `/us/` lookups never collide, sharing `_throttle` and retry logic.
- Fallback path when `serialized-server-data` is absent/malformed but `<meta property="music:song">` IDs exist: builds tracks cleanly from `LookupTracksByIdsAsync` without throwing.
- Album or single-song URLs (`music.apple.com/{cc}/album/...`, `/song/...`) fail gracefully into `Partial` with paste fallback instead of throwing `NotSupportedException`.
- Header/score/history gating: 0 matched -> `"Not enough data"`, `"— Genres"`, not saved to History; `< 50%` matched -> `"Based on X of Y tracks"`; normal >= 50% -> whole-number score.

---

### Task 1: Keyless iTunes Batch ID Lookup (`TrackEnricherService`)
**Files:**
- Modify: `src/TuneTrack/Services/Recommendations/TrackEnricherService.cs`
- Test: `tests/TuneTrack.Tests/TrackEnricherServiceTests.cs`

**Steps:**
- [ ] Write failing stubbed-HTTP test covering `LookupTracksByIdsAsync` batching (`lookup?id=...&entity=song&country=ca`), caching per `(country, id)`, 429/403 retry backoff reuse, and artwork/genre normalization without fuzzy matching.
- [ ] Run `dotnet test` to confirm RED failure.
- [ ] Add `TrackLookupItem` record and `LookupTracksByIdsAsync` on `ITrackEnricherService` / `TrackEnricherService`, extracting shared HTTP send/throttle/retry and `ITunesTrackItem` -> `TrackEnrichmentResult` mapping helpers.
- [ ] Run `dotnet test` and confirm GREEN.

### Task 2: Apple Music Structured Parsing, ID Resolution & Edge Shapes (`AppleMusicPlaylistProvider`)
**Files:**
- Modify: `src/TuneTrack/Services/Providers/AppleMusicPlaylistProvider.cs`
- Create/Modify: `tests/TuneTrack.Tests/PlaylistAndProviderTests.cs` + trimmed HTML fixture (`tests/TuneTrack.Tests/Fixtures/apple-playlist-ca-trimmed.html` or inline resource)

**Steps:**
- [ ] Write failing TDD tests using trimmed saved HTML covering:
  - Ground truth tracks (`Mi Gente`, `rockstar (feat. 21 Savage)`, `Good Old Days (feat. Kesha)`, `Lonely Together (feat. Rita Ora)`, `Phases`, `Bodak Yellow`, `Silence (feat. Khalid)`), real artists (`J Balvin, Willy William`, `Post Malone`, etc.), album/duration, storefront `ca` derivation, batch `/lookup` resolution, and zero `"Apple Music Artist"`.
  - Different storefront URL (e.g. `us` / `ph`) and omission of storefront.
  - Broken/missing `serialized-server-data` with numeric `<meta property="music:song">` IDs falls back cleanly to building tracks from lookup.
  - Missing artists on majority of tracks returns `Partial` without fake artists.
  - Non-playlist `/album/` and `/song/` Apple Music URLs return `Partial` gracefully.
- [ ] Run `dotnet test` to confirm RED failure.
- [ ] Implement `serialized-server-data` parsing, ID extraction (`storeAdamID`, `?i=`, `/song/.../{id}`, `music:song` meta fallback), storefront extraction, batch lookup merge, majority-unknown partial fallback, and `CanHandle` graceful handling for `/album/` & `/song/`.
- [ ] Run `dotnet test` to confirm GREEN.

### Task 3: Header / Score / History Consistency (`PlaylistService` & `Home.razor`)
**Files:**
- Modify: `src/TuneTrack/Services/Providers/PlaylistService.cs`
- Modify: `src/TuneTrack/Components/Pages/Home.razor`
- Test: `tests/TuneTrack.Tests/PlaylistAndProviderTests.cs`

**Steps:**
- [ ] Write failing test verifying a zero-metadata analysis (all tracks lacking Genre/ReleaseYear/Album) is NOT persisted to History (`GetHistoryAsync` remains empty).
- [ ] Update `PlaylistService.ProcessAndPersistAsync` to require `hasAnyTrackMetadata` before saving to `AnalyzedPlaylists`. Update paste test to supply `CountingEnricher` (enriched metadata) while partial/zero-data stay unpersisted.
- [ ] Update `Home.razor` header and scorecard:
  - `matchedTrackCount == 0` -> show `"Not enough data"` instead of score numbers, and `"— Genres"` when `genreCount == 0`.
  - `matchedTrackCount > 0 && matchedTrackCount * 2 < totalTrackCount` -> display `"Based on X of Y tracks"`.
- [ ] Run `dotnet build` (`0` warnings) and `dotnet test` (all pass).

### Task 4: Live Verification, Screenshots, Impeccable Detect & Commit
- [ ] Boot app against Supabase Postgres and analyze:
  1. `https://music.apple.com/ca/playlist/apple-music-list/pl.41fda5adbf9b4db4ba1a2c0a76099ef9` (report before `0/16` vs after matched count, verify no `"Apple Music Artist"`).
  2. Non-CA playlist (`https://music.apple.com/us/playlist/top-25-new-york-city/pl.a88b5c26caea48a59484370b6f79c9df`) and report matched count.
  3. Capture 1440px and 390px screenshots of resolved Apple Music results, run `impeccable detect`, confirm clean Postgres & git status (`TuneTrack.csproj` unstaged).
- [ ] Commit `fix(services): parse Apple Music artists and resolve tracks by ID`, push to `origin/main`, and present results + Skills evidence table.
