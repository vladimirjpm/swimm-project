# CHANGES — collapse selector into a header once a competition is selected

Delta spec on top of the already-implemented category selector (see README.md).
The selector itself (tabs, search, season, grouping, URL contract, tokens) does
NOT change — only WHERE and WHEN it renders.

Reference: `Category Selector.dc.html` in this folder (updated). Click a
competition row → it collapses into the header; click "Change" → the panel
drops down over the page content. Design exploration: frames **6a** (selected
state) and **7a** (deep-link `?cat=` state) in the main project's
`Homepage Options.dc.html`.

## New behaviour

### 1. Selected → header (replaces the always-expanded selector block)
When `competitionId`/`eventId` is resolved, do NOT render the expanded selector.
Render a compact **competition header** instead:
- Single row, ~72px: competition name (RTL, ellipsis on overflow) + meta line
  (date · pool length · month · season) on the left, **"Change"** button on the right.
- Colors: background `--theme-primary`, text = accent-text (white in light mode,
  `#0f1319` in dark mode — same rule as active tabs). The green gradient in the
  early mocks maps to `--theme-primary`; do not hardcode green.
- The header replaces the current green banner on the results page — one block
  instead of banner + selector.

### 2. "Change" → dropdown panel
- Click "Change" → the existing selector panel opens **absolutely positioned**
  under the header (top: header bottom + 10px, full header width, z-index above
  page content, shadow `0 18px 44px rgba(...,.18)`, ~180ms fade/slide-in).
  Page content below does NOT reflow.
- Panel gets a "✕ Close" affordance; picking a competition or clicking
  Close/outside/Esc closes it.
- Panel keeps ALL existing behaviour: tabs with counts, search-in-category,
  season, Live·Upcoming on top, month groups, collapsed archive.

### 3. Deep link `?category=...` without a competition (frame 7a)
- No header yet — the selector renders **inline, already open**, pre-filtered
  to the category from the URL (active tab, search scope, list).
- Page content below (filters, results area) is dimmed (`opacity ~.45`,
  `pointer-events: none`) with the hint "Select a competition above to see results".
- After selection: selector collapses into the header (state 1), content activates.

### 4. Rule change: category switch no longer clears selection
Previously: switching a tab reset the selection. Now the header keeps the
current competition while the user browses categories inside the open panel;
selection changes only when a new row is clicked. (Otherwise the header would
disappear mid-interaction.)

## Files to touch in `client/`
- `src/pages/results-main-page.tsx` — render header vs inline-open selector
  based on resolved `competitionId|eventId`; dim content when none.
- `src/projects/components/filter-data-source-ddl/*` (or wherever the selector
  landed) — extract the panel so it renders in two hosts: inline (no selection)
  and dropdown (from header). Add open/close state + outside-click/Esc handling.
- Remove the old standalone green banner on the results page — the header is it.

## Not designed yet (ask if needed)
- Mobile header (likely: name + "Change" only; panel becomes full-screen sheet).
- Live competition in the header (LIVE status chip inside the accent header).
- Multi-day event header (day switcher next to meta).
