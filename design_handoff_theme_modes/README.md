# Handoff: Light/Dark Mode × Training/Competition Theming

## Overview
This adds a **second, independent theming axis — Light / Dark mode** — on top of the
app's existing **Training / Competition** context theming. The two axes combine
(mode × context = 4 visual states minimum, more once your existing named variants
like `training-ocean` or `competition-warm` are considered). Mode controls base
surfaces (page/card backgrounds, borders, text). Context only tints the accent
color a little. This mirrors the axis split you already use for Training/Competition
— we're just adding the orthogonal one.

## About the design files
The bundled file **`Competition Results.dc.html`** is an **HTML design reference/prototype** —
built to work out the token model and prove it looks good, not code to copy
directly into the app. Open it in a browser; it's fully interactive (click the
floating "Themes" button, bottom-right, to flip Mode and Context independently).

**The real implementation should be done directly in `client/`**, extending the
patterns already in:
- `client/src/index.css` — the `:root[data-theme="…"]` CSS-variable blocks
- `client/src/hooks/useTheme.ts` — the hook that sets `data-theme` on `<html>`
  from `state.filterSelected.activity_type`
- `client/src/projects/results-table/*`, `sportsmen-details/*`, `filter-section/*`,
  `components/mix/*` — the actual components to theme

Do not port the prototype's inline-style/React-class approach — that's an artifact
of the design tool, not a pattern for this codebase. Keep using CSS variables +
Tailwind, same as today.

## Fidelity
**Hifi for the token model and exact values** (colors, the mode/context split,
animation timings). **Not hifi for component layout** — your real components
(`results-table-desktop.tsx`, `sportsmen-details.tsx`, etc.) already have their
own structure; only restyle them via variables, don't reshape them to match the
prototype's markup.

## The core idea (already validated in the prototype)
Two independent state values, each changing a disjoint set of CSS variables:

- **`data-mode="light" | "dark"`** (NEW — doesn't exist yet) → swaps *surface* variables:
  page background, card/section background, borders, all text colors.
- **`data-theme="…"`** (ALREADY EXISTS) → keeps doing what it does today: setting
  the *accent* variables (`--theme-primary`, `--theme-primary-light`,
  `--theme-bg-active`, `--btn-training-bg`, `--btn-competition-bg`, etc.)

Because the two are orthogonal, you do **not** need to hand-author a dark variant
of every existing named theme (`training-ocean`, `competition-warm`, …). Recommended
approach: keep your current named themes as the **light-mode accent catalog**, and
add **one dark surface set** that every context's accent gets layered onto in dark
mode (exactly what the prototype does — see `get palette()` in its script block).
Expanding to fully custom per-variant dark palettes later is possible, but start
with the shared dark surface + existing accent hues — it's a fraction of the work
and already looks good (validated in the prototype across all 4 combos).

## Design tokens

### New MODE variables to add (surfaces — swap wholesale between light/dark)
| Variable | Light value | Dark value | Used for |
|---|---|---|---|
| `--theme-mode-page-bg` | `#f5f5f7` | `#0f1319` | page background |
| `--theme-mode-surface` | `#ffffff` | `#1a1f28` | cards, table, popups |
| `--theme-mode-surface-alt` | `#fafbfd` | `#20262f` | table header row |
| `--theme-mode-border` | `#eef1f6` | `#2a313c` | hairline dividers |
| `--theme-mode-border-row` | `#f3f5f9` | `#242b35` | table row separators |
| `--theme-mode-border-drawer` | `#e0e8e3` | `#2a313c` | filter-section card borders |
| `--theme-mode-border-input` | `#d6e0da` | `#333c48` | selects, segmented buttons, dashed dividers |
| `--theme-mode-text` | `#1a1a1a` | `#eef1f6` | primary text |
| `--theme-mode-text-secondary` | `#5b6470` | `#9aa4b2` | secondary text (club names, labels) |
| `--theme-mode-text-muted` | `#aab0bd` | `#6b7686` | muted labels, chevrons |
| `--theme-mode-drawer-bg` | `#f0f5f2` | `#14181f` | filter drawer / popup background |
| `--theme-mode-input-bg` | `#f7faf8` | `#232a34` | select boxes, club rows |
| `--theme-mode-name-bar-bg` | `#8a93a3` | `#242e3b` | sportsmen-details name bar |
| `--theme-mode-me-highlight` | `#fffdf6` | `#241f16` | "this is me" row/card tint (keep the gold border as-is, only the fill changes) |
| `--theme-mode-hero-grad` | `linear-gradient(160deg,#dcebf7,#c4def0)` | `linear-gradient(160deg,#223243 0%,#151d28 100%)` | sportsmen-details hero card |
| `--theme-mode-avatar-bg` | `#bfe0f5` | `#2c3d52` | avatar circle background |

### Existing CONTEXT variables (keep using, just add dark-aware values)
`--theme-primary`, `--theme-primary-light`, `--theme-bg-active`, `--btn-training-bg`,
`--btn-competition-bg`, etc. — already defined per `data-theme` in `index.css`.
**In dark mode, brighten these slightly for contrast** against the darker surfaces
(the prototype does this): e.g. training accent `#1b8a4b` → `#3ecb7f` in dark;
competition accent `#1565c0` → `#4da3ff` in dark. Same idea for the masthead
gradient (`--theme-bg-header` equivalent): darken it, and optionally layer a
subtle radial glow of the accent color at ~15-17% opacity behind hero/avatar
elements — see `heroGrad` composition in the prototype's `renderVals()` for the
exact recipe (`radial-gradient(circle at 50% 34%, <accent>2b, transparent 60%), <dark gradient>`).

### Colors that must NOT change with mode or context (keep as-is everywhere)
Medal colors (gold/silver/bronze radial gradients), level-category colors
(youth/adult/pro), record badges (gold gradient "NEW RECORD"/"RECORD"), the
⭐ "ME" marker (`#f5b800`/`#d99a00`), favorite heart red (`#e23b5a`), reset-button
red (`#e63946`). These are status colors, not theme colors — likely already
isolated in `mix/medal-icon`, `mix/normative-level-icon`, `mix/record-count`.

## ⚠️ Audit hardcoded colors (important — this bit us in the prototype)
While building the prototype we found an accent purple (`#5B4FFF`) and its tint
(`#eef0ff`) hardcoded across filter values, the swim-style chip, and the progress-%
chip — completely bypassing the theme system. In dark mode this produced a bright,
disconnected chip sitting on a dark card (see the "found issues" round in this
project's history if you want the before/after). **Expect the same problem in
this codebase.** Before wiring up dark mode, grep `client/src` for raw hex colors
(`#[0-9a-fA-F]{3,6}`) and `rgba(` inside `.tsx`/`.css` files in at least:
`projects/results-table/`, `projects/sportsmen-details/`, `projects/components/filter-section/`,
`projects/components/mix/swimm-style-icon/`, `projects/components/mix/progress-level/`,
`projects/components/popup/`. Anything that's a themeable surface/text/accent
color (not one of the status colors above) should move to a CSS variable.

## Interactions & animations
No production UI for switching mode is required yet (the floating "Themes" button
in the prototype is an exploration tool only — do not ship it). Whatever settings
UI the product team eventually adds, the underlying mechanism is just toggling
`data-mode` on `<html>` (same pattern as `useTheme.ts` does for `data-theme` today);
persist the choice (localStorage, falling back to `prefers-color-scheme` on first
visit is a common default, confirm with product before assuming).

Two animation additions validated in the prototype, worth carrying over to
`popup.css` / `filter-section.css`:
- **Filter drawer entrance**: backdrop `opacity 0→1` over `0.25s ease`; panel
  slides `translateX(100%)→0` with slight overshoot-in via
  `cubic-bezier(0.22, 1, 0.36, 1)` over `0.34s`. Both `animation … both` (fill-forwards),
  so they run once on mount and don't restart on internal state changes.
- **Sportsmen-details popup entrance**: `translateY(14px) scale(0.97) → translateY(0) scale(1)`,
  opacity `0→1`, same easing, `0.3s`.

## Progress-level gauge (small polish, carry over)
The arc in `mix/progress-level` should use `stroke-width: 3.5` (not `6`) for both
the track and the value arc — thinner reads cleaner at this size. The track color
must use `--theme-mode-border` (not a fixed light gray) so it's visible in dark mode.

## Assets
No new image assets. Emoji are used as avatar/flag/club-icon placeholders in the
prototype only — your real components already use real icon components
(`flag-icon`, `swimmer-icon`, `club-icon`, etc.); keep those.

## Files in this bundle
- `Competition Results.dc.html` — interactive reference prototype. Open directly
  in a browser. Click "Themes" (bottom-right) to try all 4 Mode × Context
  combinations and see the sportsmen-details popup + filter drawer animations.
