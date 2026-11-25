# Results, Recommendations & Consistency Fix Plan

> **For agentic workers:** Follow `superpowers:test-driven-development` (write failing tests first for `RecommendationEngine` and `SamplePlaylistService`), `ponytail` (keep diffs minimal, no new packages), `ui-ux-pro-max` (responsive chip/table rules), and `impeccable` (`detect` + visual check).

**Goal:** Diversify recommendations across genres and adjacent genres, compute sample metadata from track data with offline cover assets, standardize whole-number diversity rounding and pluralization, expose all genre filter chips, fix mobile 390px results layout, verify `"Active"` removal across all routes, and render `/history` timestamps in viewer local time.

**Architecture:** Inject `IDiversityScorer` into `RecommendationEngine` to detect low-diversity playlists (`score < 40`) and sample round-robin across top genres (plus an adjacent-genre map for low-diversity sets). Update `SamplePlaylistService` to derive descriptions from `Tracks`, use `"Sample"` as `Platform`, and reference static files under `wwwroot/images/samples/`. Update Razor pages (`Home.razor`, `History.razor`, `Saved.razor`) and `wwwroot/js/tunetrack.js` without changing database schema or adding dependencies.

**Tech Stack:** .NET 10 Blazor Web App (InteractiveServer), Tailwind CSS, xUnit, Puppeteer-core (`scripts/verify-ui.js`), Impeccable CLI.

## Global Constraints
- No new NuGet or npm packages.
- `dotnet build` must finish with 0 warnings and 0 errors; `dotnet test` must pass.
- No remote cover URLs in `SamplePlaylistService` (100% offline local files under `wwwroot/images/samples/`).
- No horizontal overflow on `1440px` or `390px` across `/`, `/history`, `/saved`, and both sample result views.

## Review Focus
- Tied genre counts (`1, 1, 1, 1`) in `RecommendationEngine` must NOT exhaust all suggestions on the first alphabetical/arbitrary genre.
- Low-diversity single-genre playlist (`score < 40`) must include >= 50% suggestions from adjacent genres not already present in the playlist.
- `RecommendationEngine` must still handle empty iTunes responses, missing genres, and de-duplication cleanly without throwing.
- History UTC timestamps (`DateTimeKind.Unspecified` after EF Core read) must explicitly specify `DateTimeKind.Utc` before converting to ISO 8601 for browser local-time conversion.
- Genre filter row on 390px mobile must scroll internally without forcing `documentElement.scrollWidth > clientWidth`.

---

### Task 1: `RecommendationEngine` Round-Robin & Adjacent-Genre Expansion (TDD)

**Files:**
- Create: `TuneTrack.Tests/RecommendationEngineTests.cs`
- Modify: `Services/RecommendationEngine.cs`

**Steps:**
- [ ] Write failing xUnit tests in `TuneTrack.Tests/RecommendationEngineTests.cs`:
  - `TiedGenres_SamplesRoundRobinAcrossMultipleGenres`
  - `LowDiversityPlaylist_IncludesAtLeastHalfFromAdjacentGenresNotInPlaylist`
  - `ExcludesExistingTracksAndArtists_AndDeduplicates`
- [ ] Run `dotnet test` and confirm the new tests fail against existing `RecommendationEngine.cs`.
- [ ] Update `RecommendationEngine` constructor to accept `IDiversityScorer` (from existing DI singleton), group genres by count, take top 5, add static adjacency map (`Synth-Pop`, `Pop`, `Rock`, `Hip-Hop`, `Jazz`, `Electronic`, `Indie Rock`, `Indie`, `Alternative`, etc.), build candidate lists per genre, sample round-robin across target genres (for `score < 40`, include adjacent genres outside the playlist for at least half the target count).
- [ ] Run `dotnet test` and confirm all tests pass.

### Task 2: `SamplePlaylistService` Computed Metadata, `"Sample"` Platform & Local Covers

**Files:**
- Add: `wwwroot/images/samples/eclectic-horizon.png`, `strictly-synthpop.png`, `modern-indie.png`
- Create: `TuneTrack.Tests/SamplePlaylistServiceTests.cs`
- Modify: `Services/SamplePlaylistService.cs`
- Modify: `Components/Pages/Home.razor` & `Components/Pages/History.razor` (add `"Sample"` badge styling)

**Steps:**
- [ ] Write failing test in `TuneTrack.Tests/SamplePlaylistServiceTests.cs` verifying: each sample's `Description` matches its actual track count, min–max `ReleaseYear`, distinct `Genre` count, and distinct `Artist` count; `Platform == "Sample"`; `CoverUrl` and track `ArtworkUrl` use local `/images/...` paths (no `http://` or `https://`).
- [ ] Run `dotnet test` and watch it fail.
- [ ] Save local cover images to `wwwroot/images/samples/`, update `SamplePlaylistService.cs` to compute descriptions via helper and use local `/images/samples/...` paths + `"Sample"` platform.
- [ ] Run `dotnet test` and verify pass.

### Task 3: Rounding, Pluralization, All Genre Chips & Mobile Results Layout

**Files:**
- Modify: `Components/Pages/Home.razor`
- Modify: `Components/Pages/History.razor`

**Steps:**
- [ ] Format diversity scores with `Math.Round(score, MidpointRounding.AwayFromZero).ToString("0")` on idle tiles, header stats, sidebar score card + badge, and `/history`.
- [ ] Pluralize `"Genre"` / `"Genres"` (and `"Track"` / `"Tracks"`) cleanly.
- [ ] Replace `GetTopGenres(5)` with all genres sorted by count descending in a bounded horizontally scrollable chip bar (`min-w-0 max-w-full overflow-x-auto flex items-center gap-1.5` with `whitespace-nowrap flex-shrink-0` chips).
- [ ] Fix header stats wrapping on 390px so no orphaned `·` separators appear (clean flex wrap with CSS dot dividers or gap-based items without orphaned `<span>·</span>` tokens).
- [ ] Move genre pill under artist on `< sm` (`sm:hidden`) and use `whitespace-nowrap` in desktop table cells (`hidden sm:table-cell`) so genre pills never wrap awkwardly on 390px.

### Task 4: Verify `"Active"` Removed Everywhere & Local Time Conversion in `/history`

**Files:**
- Modify: `wwwroot/js/tunetrack.js`
- Modify: `Components/Pages/History.razor`

**Steps:**
- [ ] Confirm zero `"Active"` status indicators on `/`, `/history`, `/saved`.
- [ ] Add `formatLocalDates(isoUtcStrings)` to `wwwroot/js/tunetrack.js` using `Intl.DateTimeFormat`.
- [ ] In `History.razor`, convert each item's `AnalyzedAt` (explicitly marked UTC via `DateTime.SpecifyKind(..., DateTimeKind.Utc)`) via `tuneTrack.formatLocalDates` after client load and render localized date/time.

### Task 5: End-to-End Verification, Impeccable Audit & Commit

**Steps:**
- [ ] Run `dotnet build` (0 warnings, 0 errors) and `dotnet test` (all pass).
- [ ] Update `scripts/verify-ui.js` to capture `1440px` and `390px` screenshots for: idle `/`, Eclectic Horizon results, Strictly Synth-Pop results, `/history`, and `/saved`, and assert no horizontal overflow, no `"Active"`, whole-number scores, multi-genre chips, and multi-genre recommendations.
- [ ] Run `.agents/skills/impeccable/scripts/impeccable.cmd detect --json --changed` (and rendered checks) once, fix any findings, rebuild, and re-verify screenshots.
- [ ] Commit with `fix(ui): diversify recommendations, fix sample data and mobile results layout` and push to `origin/main`.
