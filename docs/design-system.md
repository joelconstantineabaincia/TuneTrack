# TuneTrack Design System

> **Aesthetic Archetype**: Editorial & Music-Centric. Calmer than standard Spotify, crisper than default SaaS. Characterized by borderless hairline dividers, deep neutral canvas, high typographic contrast, and mono numeric readouts.

---

## 1. Color Tokens & Surface Hierarchy

| Token | Hex / Value | Tailwind Equivalent | Role |
| :--- | :--- | :--- | :--- |
| **Canvas** | `#09090B` | `bg-zinc-950` | Primary background, full bleed |
| **Surface Elevated** | `#121215` | `bg-[#121215]` | Sticky sidebar, toolbars, popovers |
| **Surface Interactive** | `#18181B` | `bg-zinc-900` | Input backgrounds, row hovers, pill tags |
| **Border Hairline** | `rgba(255,255,255,0.08)`| `border-white/10` / `divide-white/5` | Borderless aesthetic hairlines |
| **Accent Primary** | `#1DB954` | `emerald-500` | Playback state, primary actions, high score |
| **Accent Cyan** | `#06B6D4` | `cyan-500` | Genre variety metric bar |
| **Accent Violet** | `#8B5CF6` | `violet-500` | Era spread metric bar |
| **Text High Contrast**| `#FAFAFA` | `text-zinc-50` / `text-white` | Track titles, headers, scores |
| **Text Secondary** | `#A1A1AA` | `text-zinc-400` | Artist names, album names, subtitles |
| **Text Muted** | `#71717A` | `text-zinc-500` | Track numbers, durations, empty states |

---

## 2. Typography Scale

- **Display & Body**: Inter (`font-sans`)
- **Data & Metrics**: JetBrains Mono / Monospace (`font-mono`, `tabular-nums`)

| Level | Size | Weight | Tracking | Usage |
| :--- | :--- | :--- | :--- | :--- |
| **Hero Display** | `2rem` (32px) / `2.5rem` (40px) | `font-bold` (700) | `tracking-tight` | Playlist title in header |
| **Score Display** | `2.5rem` (40px) | `font-black` (900) | `tabular-nums` | Diversity total score (0–100) |
| **Section Title** | `0.875rem` (14px) | `font-semibold` (600) | `tracking-wider uppercase text-zinc-400` | Sidebar headers |
| **Body Primary** | `0.9375rem` (15px) | `font-medium` (500) | normal | Track titles, search input |
| **Body Secondary**| `0.8125rem` (13px) | `font-normal` (400) | normal | Artist names, descriptions |
| **Micro Monospace**| `0.75rem` (12px) | `font-medium` (500) | `tabular-nums` | Track duration, release year, percentage |

---

## 3. Spacing & Grid System

- **Navbar**: Exact `h-14` (56px) sticky bar with bottom hairline `border-b border-white/10`.
- **Hero / Header**: Full-width ambient cover gradient, square thumbnail `160×160px` (`w-40 h-40`), compact stat strip.
- **Desktop Workspace (≥ 1024px)**: Asymmetric 12-column grid (`grid grid-cols-12 gap-8`):
  - **Main (8 cols)**: Search bar, genre filter pill row, borderless tracklist table with `divide-y divide-white/5`.
  - **Sidebar (4 cols)**: Sticky right panel (`sticky top-20`) holding diversity sub-metric bars, genre distribution, and suggested tracks.
- **Mobile (< 1024px)**: Single column stacked layout with sidebar rendered beneath tracklist.

---

## 4. Component States & Interaction

### Inputs & Buttons
- **Focus States**: Explicit, accessible ring: `focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2 focus-visible:ring-offset-zinc-950 outline-none`.
- **Button Hover**: `hover:bg-white/10 active:scale-[0.98] transition-all duration-150`.
- **Disabled**: `opacity-50 cursor-not-allowed`.

### Track Rows
- **Default**: Transparent background, subtle border divider `border-white/5`.
- **Hover**: Background shifts to `bg-white/[0.04]` or `bg-zinc-900/60`, action buttons (preview, copy, bookmark) reveal.
- **Active Playing**: Artwork displays animated green sound wave bars or spinning ring.

### Accessibility & Reduced Motion
- All icons rendered as clean, inline accessible SVGs (never emojis).
- Text contrast strictly satisfies WCAG AA (all text meets ≥ 4.5:1 ratio on dark surfaces).
- `@media (prefers-reduced-motion: reduce)` respected via Tailwind's `motion-reduce:*` utility or global CSS reset.
