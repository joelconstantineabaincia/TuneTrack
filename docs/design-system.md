# TuneTrack Design System

> **Aesthetic Archetype**: Editorial & Music-Centric. Calmer than standard Spotify, crisper than default SaaS. Characterized by borderless hairline dividers, deep neutral canvas, distinctive display typography paired with a legible technical-humanist body face, and mono numeric readouts.

---

## 1. Color Tokens & Surface Hierarchy

| Token | Hex / Value | Tailwind Equivalent | Role |
| :--- | :--- | :--- | :--- |
| **Canvas** | `#09090B` | `bg-zinc-950` | Primary background, full bleed |
| **Surface Elevated** | `#121215` | `bg-[#121215]` | Subtle elevation, popovers, audio overlays |
| **Surface Interactive** | `#18181B` | `bg-zinc-900` | Input backgrounds, hover surfaces, tags |
| **Border Hairline** | `rgba(255,255,255,0.08)` | `border-white/10` / `divide-white/5` | Borderless editorial hairlines |
| **Accent Primary** | `#1DB954` | `emerald-500` | Playback state, primary actions, high score |
| **Accent Cyan** | `#06B6D4` | `cyan-500` | Genre variety metric bar |
| **Accent Violet** | `#8B5CF6` | `violet-500` | Era spread metric bar |
| **Text High Contrast**| `#FAFAFA` | `text-zinc-50` / `text-white` | Track titles, headers, scores |
| **Text Secondary** | `#A1A1AA` | `text-zinc-400` | Artist names, album names, subtitles |
| **Text Muted** | `#71717A` | `text-zinc-500` | Secondary metadata, empty state icons |

---

## 2. Typography Pairing & Scale

- **Display & Headings (`font-display`)**: `Syne` (weights `600`, `700`; sourced via Google Fonts) with system fallback stack (`ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif`). Used for navbar wordmark, page `h1` titles, section `h2` headings, and playlist/card titles.
- **Body & UI Controls (`font-sans`)**: `Manrope` (weights `400`, `500`, `600`, `700`; sourced via Google Fonts) with system fallback stack (`ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif`). Used for tracklist rows, filter pills, descriptions, inputs, and buttons.
- **Data & Metrics (`font-mono`)**: `JetBrains Mono` (weights `400`, `500`, `600`, `700`, `tabular-nums`; fallback `ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace`). Used for track durations, release years, diversity scores, timestamps, and percentages.
- **Wrapping & Tracking**: Headings apply `text-wrap: balance` with tracking no tighter than `-0.025em` (`tracking-tight`); body copy applies `text-wrap: pretty`.

| Level | Size | Family | Weight | Tracking | Usage |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Hero / Playlist Title** | `2rem`–`2.25rem` (32–36px) | `Syne` (`font-display`) | `font-bold` (700) | `tracking-tight` | Hero headline & playlist `h1` |
| **Page / Score Display** | `1.875rem`–`2.25rem` (30–36px) | `Syne` / `JetBrains Mono` | `font-bold` (700) | `tabular-nums` | Subpage `h1` & diversity score (0–100) |
| **Section Title** | `0.875rem` (14px) | `Syne` (`font-display`) | `font-semibold` (600) | normal | Sidebar & idle section `h2` headings |
| **Body Primary** | `0.875rem`–`1rem` (14–16px) | `Manrope` (`font-sans`) | `font-medium` (500) | normal | Track titles, descriptions, search input |
| **Body Secondary** | `0.75rem`–`0.8125rem` (12–13px) | `Manrope` (`font-sans`) | `font-normal` (400) | normal | Artist/album metadata, subtitles |
| **Micro Monospace** | `0.75rem` (12px) | `JetBrains Mono` (`font-mono`) | `font-medium` (500) | `tabular-nums` | Track duration, release year, percentages |

---

## 3. Spacing & Borderless Editorial Grid

- **Navbar**: Sticky `h-14` (56px) top bar with bottom hairline `border-b border-white/10`.
- **Results Header**: Open, unboxed horizontal composition with `pb-8 border-b border-white/10` (no enclosing box card or decorative radial glow), square thumbnail (`160x160px` / `w-40 h-40`), and inline stat strip.
- **Desktop Workspace (>= 1024px)**: Asymmetric 12-column grid (`grid-cols-1 lg:grid-cols-12 gap-8`):
  - **Main (8 cols)**: Search filter, horizontally scrollable genre chip strip, and borderless tracklist table using `border-b border-white/10` header line + `divide-y divide-white/5` row dividers only (no outer box wrapper).
  - **Sidebar (4 cols)**: Sticky right column (`lg:sticky lg:top-20`) with sections separated by `pb-7 border-b border-white/10` hairlines instead of boxed cards, and flat recommendation rows separated by `divide-y divide-white/5`.
- **Mobile (< 1024px)**: Single-column stacked layout with sidebar metrics and recommendations below the tracklist.

---

## 4. Component States & Interaction

### Inputs & Buttons
- **Focus States**: Explicit accessible ring: `focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2 focus-visible:ring-offset-zinc-950 outline-none`.
- **Button Hover**: `hover:bg-white/10 active:scale-[0.98] transition-all duration-150`.
- **Disabled**: `opacity-50 cursor-not-allowed`.

### Track & Recommendation Rows
- **Default**: Transparent background on `#09090B` canvas with `divide-y divide-white/5`.
- **Hover**: Subtle `hover:bg-white/[0.04]` row highlight; audio preview action reveals cleanly on hover/focus.
- **Active Playing**: Clear emerald pause state on preview overlay.

### Accessibility & Reduced Motion
- All icons are crisp inline SVGs (never emoji).
- Text contrast meets WCAG AA (`>= 4.5:1` body, `>= 3:1` large text).
- `@media (prefers-reduced-motion: reduce)` disables transitions and animations globally in `Styles/app.css`.
