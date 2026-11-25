# Editorial Typography & Flattened Results Layout Plan

> **For agentic workers:** Follow `superpowers:writing-plans` and `verification-before-completion`, `ui-ux-pro-max` typography/contrast guidance, `impeccable` (`craft-floor.md`, `critique.md`, and `detect`), and `ponytail` (visual-only changes, minimal diff, preserve existing selectors used by `scripts/verify-ui.js`).

**Goal:** Replace `Inter` (and fallback `Roboto`) with a distinctive, readable editorial pairing (`Syne` for display/headings + `Manrope` for body/UI, retaining `JetBrains Mono` for numeric readouts) across TuneTrack, flatten the results view toward a borderless editorial layout, run the real `impeccable critique` and `impeccable detect` passes, fix findings, and capture updated 1440px and 390px verification screenshots.

**Architecture:** Visual-only pass across `docs/design-system.md`, `tailwind.config.js`, `Components/App.razor`, `Styles/app.css`, `Components/Layout/MainLayout.razor`, and the Razor pages (`Home.razor`, `History.razor`, `Saved.razor`, `Error.razor`, `NotFound.razor`). No C# service logic, domain models, EF Core configuration, or tests are altered.

**Tech Stack:** .NET 10 Blazor Web App, Tailwind CSS v3, Google Fonts (`Syne`, `Manrope`, `JetBrains Mono`), Puppeteer-core (`scripts/verify-ui.js`), Impeccable CLI.

## Global Constraints
- Visual only: zero modifications to `Services/`, `Models/`, `Data/`, `Migrations/`, `Program.cs`, or `TuneTrack.Tests/`.
- Preserve DOM selectors relied on by `scripts/verify-ui.js` (`h1`, `table`, `tbody tr`, `tbody span.sm:hidden`, `[aria-label="Filter tracks by genre"] button`, `.lg:col-span-4 span.font-mono.mt-0.5`, `time`, `button[title*="Bookmark"]`).
- Zero `overused-font` references (`Inter`, `Roboto`, `Fraunces`, `Geist`, `Plus Jakarta Sans`, `Space Grotesk`) in `tailwind.config.js`, `App.razor`, `Styles/app.css`, or `docs/design-system.md`.
- `dotnet build` exits 0 with 0 warnings/errors and `dotnet test` passes after all edits.

---

### Task 1: Update Font Pairing & Design System Documentation
**Files:**
- Modify: `docs/design-system.md`
- Modify: `tailwind.config.js`
- Modify: `Components/App.razor`
- Modify: `Styles/app.css`

**Steps:**
- [ ] Update `docs/design-system.md` section 2 to document `Syne` (display/headings, `font-display`), `Manrope` (body/UI, `font-sans`), and `JetBrains Mono` (metrics/data, `font-mono`), plus system fallback stacks and flattened results-view rules in section 3.
- [ ] Update `tailwind.config.js` `theme.extend.fontFamily` with `display` (`Syne` + system sans fallback), `sans` (`Manrope` + system sans fallback, removing `Inter` and `Roboto`), and `mono` (`JetBrains Mono` + monospace fallback).
- [ ] Update Google Fonts `<link>` in `Components/App.razor` to load `Syne:wght@600;700`, `Manrope:wght@400;500;600;700`, and `JetBrains+Mono:wght@400;500;600;700`.
- [ ] Add base typography rules in `Styles/app.css` (`body` in `font-sans`, display headings `h1, h2` in `font-display` with `text-wrap: balance`, body paragraphs `text-wrap: pretty`, subtle styled dark scrollbar/focus defaults).

### Task 2: Apply Typography Everywhere & Flatten Results Layout
**Files:**
- Modify: `Components/Layout/MainLayout.razor`
- Modify: `Components/Pages/Home.razor`
- Modify: `Components/Pages/History.razor`
- Modify: `Components/Pages/Saved.razor`
- Modify: `Components/Pages/Error.razor`
- Modify: `Components/Pages/NotFound.razor`

**Steps:**
- [ ] Apply `font-display` cleanly to the navbar wordmark in `MainLayout.razor`, all page `h1` headings, idle sample card titles, and display numbers/headings (`Error.razor`, `NotFound.razor`, `History.razor`, `Saved.razor`, `Home.razor`), ensuring body UI consistently inherits `Manrope` (`font-sans`).
- [ ] Flatten the results header in `Home.razor`: remove outer `rounded-2xl bg-zinc-900/60 border border-white/5 p-6 sm:p-8` card container and decorative `bg-emerald-500/10 blur-3xl` radial wash; use open horizontal layout with bottom hairline `pb-8 border-b border-white/10`.
- [ ] Flatten the tracklist in `Home.razor`: remove `rounded-xl border border-white/5 bg-zinc-900/30` outer wrapper and `bg-zinc-900/80` header box fill; retain `overflow-x-auto`, `border-b border-white/10` on `<thead>`, and `divide-y divide-white/5` row dividers only.
- [ ] Flatten the results sidebar in `Home.razor`: replace the three `p-5 rounded-xl bg-zinc-900/60 border border-white/5` cards and inner `bg-zinc-950/50 border` recommendation boxes with spacing + hairline dividers (`pb-7 border-b border-white/10` between sections and `divide-y divide-white/5` between recommendation rows). Mirror the flat structure in the loading skeleton.

### Task 3: Impeccable Critique, Mechanical Detect & Fixes
**Steps:**
- [ ] Run `dotnet build` and start TuneTrack locally on `http://127.0.0.1:5199`.
- [ ] Run real `impeccable critique` (following `.agents/skills/impeccable/reference/critique.md`: Assessment A isolated design review + Assessment B `impeccable detect --json` across markup and live URL viewports `1440x900` and `390x844`).
- [ ] Fix any genuine typography, contrast, layout, or slop findings discovered by critique or `detect` (preserving no-logic-change constraint).
- [ ] Re-run `impeccable detect --json` on modified UI and live URLs to verify resolution.

### Task 4: Verification Screenshots, Build, Tests & Push
**Steps:**
- [ ] Run `node scripts/verify-ui.js` against the local app to regenerate all 10 screenshots (`1440px` and `390px` for idle, Eclectic, Synth-Pop, `/history`, `/saved`).
- [ ] Visually inspect generated screenshots (or key renders via `read_file`) to confirm typography rendering, flat results hierarchy, and responsive fit.
- [ ] Stop background app, run `dotnet build` (0 warnings, 0 errors) and `dotnet test` (all pass).
- [ ] Commit with `style(ui): replace default font and flatten results layout` and push to `origin/main`.
