# Improve iTunes Match Rate Implementation Plan

> **For agentic workers:** Follow TDD (`test-driven-development`), keep diffs minimal (`ponytail`), and verify with `dotnet build`, `dotnet test`, and live Spotify verification before committing.

**Goal:** Raise iTunes enrichment match rate on real playlists (e.g., *Indie's Top 50*, previously 26/51) by removing the 30-track cap/10s shared enrichment timeout, retrying HTTP 429 and timeouts with backoff, normalizing complex track titles, adding bounded fallback queries (`firstArtist + title`, then `title` only with strict artist matching), and surfacing progress in `Home.razor`.

**Root-Cause Findings (51 tracks on Indie's Top 50):**
- Exact current app run (`maxToEnrich: 30`, `10s` shared CTS): `26` matched, `21` skipped by `maxToEnrich: 30`, `3` zero results, `1` artist-mismatch rejection (`0` 429/timeout).
- Uncapped spaced run (all 51 tracks, no retries): `41` matched, `6` HTTP 429 rate-limit failures (#39, #43, #45, #46, #47, #49), `3` zero results (#4, #8, #16), `1` artist-mismatch rejection (#5).
- Unthrottled spaced probe without 429 shows `#39`, `#43`, `#47`, `#49` exist in iTunes (ceiling `45/51`; remaining 6 are not in US iTunes or are Spotify-exclusive `#45`).

---

### Task 1: Failing TDD Tests (`TrackEnricherServiceTests` & `PlaylistAndProviderTests`)
- Add stubbed HTTP handler tests covering every failure/recovery mode:
  1. HTTP `429` retry with backoff succeeds on retry (and `429` exhausted does NOT poison negative cache).
  2. Per-request timeout / `TaskCanceledException` retry succeeds on retry.
  3. Second-pass fallback: multi-artist comma/collab credit (`"Artist A, Artist B"`) falls back to first listed artist + cleaned title.
  4. Third-pass fallback: `"title only"` query succeeds when artist+title returns empty, while still rejecting wrong artists via `IsArtistMatch`.
  5. Stronger title cleaning strips `"- Remastered 2011"`, `"- Single Version"`, `"(feat. X)"`, `"(Live)"`, `"- Spotify Singles"`, etc. without stripping real titles like `"(stay)"` or `"(But What Am I Thinking)"`.
  6. `EnrichTracksAsync` reports progress `(Completed, Total)` and processes `> 30` tracks (e.g. `SpotifyPlaylistProvider` enriches > 30 tracks and separates HTML fetch timeout from enrichment).
- Run `dotnet test` to confirm RED failures.

### Task 2: `TrackEnricherService` Retries, Title Cleaning, Fallback Queries & Diagnostics
- Files: `src/TuneTrack/Services/Recommendations/TrackEnricherService.cs`
- Add `IProgress<(int Completed, int Total)>? progress` default-interface overload on `ITrackEnricherService`.
- Expand `CleanTrackTitle` for `- Remastered YYYY`, `- YYYY Remaster`, `- Single Version`, `- Radio Edit`, `- Live...`, `- Spotify Singles`, `(feat. ...)`, `(Live)`, `[...]`, keeping non-suffix parentheticals intact.
- Add `ExtractFirstListedArtist` and run query passes cleanly inside `LookupTrackAsync`:
  1. `"{artist} {cleanTitle}"` (`limit=5`)
  2. `"{firstArtist} {cleanTitle}"` (`limit=5`, only if different from `artist`)
  3. `"{cleanTitle}"` (`limit=10`, only if non-empty, still gated by `IsArtistMatch`)
- Retry transient HTTP `429` / `503` / per-attempt timeouts inside throttled calls using backoff (`Retry-After` if present, else `[750ms, 1500ms]` in prod; injectable delays in internal test constructor); do not cache transient failures in `_cache`.
- Log per-track unmatched reason and summary counts (`429`, `timeout`, `zero results`, `artist mismatch`), and raise default `maxToEnrich` from `25` to `100`.

### Task 3: Providers, `PlaylistService`, and Loading Progress UI
- Files: `IPlaylistProvider.cs`, `SpotifyPlaylistProvider.cs`, `AppleMusicPlaylistProvider.cs`, `YouTubeMusicPlaylistProvider.cs`, `PlaylistService.cs`, `Home.razor`
- Separate 10s HTTP page-fetch timeout from track enrichment in providers and raise `maxToEnrich` from `30` to `100`; forward optional `IProgress<(int Completed, int Total)>`.
- In `Home.razor`, display `"Matching tracks… X/Y"` with a compact progress indicator once `isLoading` exceeds 2 seconds, disposing cancellation cleanly on navigation.

### Task 4: Verification & Ship
- `dotnet build` (`0` warnings, `0` errors), `dotnet test` (all pass).
- Re-run live *Indie's Top 50* Spotify playlist, record before (`26/51`) vs after matched count, verify progress state in Playwright if visible/long-running, commit `fix(services): improve iTunes match rate with retries and fallback queries`, and push.
