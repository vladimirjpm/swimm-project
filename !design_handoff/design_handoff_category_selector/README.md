# Handoff: Category-linked competition selector (Homepage → Results)

> **⚠️ UPDATE (July 2026): see `CHANGES.md`** — the selector now collapses into a
> compact competition header once a selection is made ("Change" reopens it as a
> dropdown), and the deep-link `?category=` state shows the selector open inline
> with dimmed content. `CHANGES.md` is a delta on top of this README; the reference
> DC includes the new states. One rule below is superseded: category switch no
> longer resets the selection.

## Overview
Replaces the empty "Select Data Source" multi-select on the results page with a
**category-first selector**: category tabs (All / Young 8–11 / Junior / Masters) +
grouped, searchable competition list. The homepage passes context via URL, so the
results page never opens in the empty "no source selected" state.

Chosen direction: **3a** from the design exploration (`Homepage Options.dc.html`,
sections 3a → 4a → 5a in the main project), scaled for dozens of competitions
per category.

## URL contract
- Homepage category tile click → `/competitions?category=junior` (also `young8_11`,
  `masters`, `all`). Optional `&season=2026`.
- Direct competition click (or selection inside the list) →
  `/competitions?competitionId=<id>` for single-day, `/competitions?eventId=<id>`
  for multi-day events — same split `filter-data-source-ddl.tsx` already uses
  when calling `/api/results` (`api:event:<id>` / `api:competition:<id>`).
- `category` and `competitionId|eventId` are mutually exclusive; a concrete id wins.
- On arrival with `category`: activate the tab, auto-select nothing, show the list.
  On arrival with `competitionId|eventId`: load it immediately (no empty state,
  no yellow warning banner).

## ⚠️ Backend gap (blocker)
`/api/competitions` returns `{kind, id, name, date, date_end, day_count, result_count}` —
**no category field**. Add `category: 'young8_11' | 'junior' | 'masters'` to the DTO.
Derivation if needed server-side: `is_masters` → masters; age range ≤11 → young8_11;
else junior. Also useful: a `status` (`live | upcoming | done`) or at least reliable
`date`/`date_end` so the client can compute Live/Upcoming.

## Behaviour rules (validated in the reference DC)
1. **Live + upcoming always on top**, outside grouping/pagination.
2. Finished competitions group **by month**, newest first; anything older than the
   current couple of months is collapsed behind "Show N earlier competitions".
3. **Search filters inside the active category**; season switch cuts to one year.
4. Tabs show **counts** per category (from `/api/competitions`).
5. Category switch resets selection and collapses the archive.

## Theming — what was wrong and what is wired now
The early mocks (4a/5a) hardcoded a green accent (`#2bcf88` / `#1a9d63`) that does
not exist in the token system. Fixed in the reference DC; use these mappings:

| Element | Token |
|---|---|
| Active tab bg / selected row border / upcoming date | `--theme-primary` (context accent, mode-aware) |
| Selected row fill | `--theme-primary-light` |
| Page / card / input surfaces | `--theme-mode-page-bg`, `--theme-mode-surface`, `--theme-mode-input-bg` |
| Row & input borders | `--theme-mode-border-input` (rows may use `--theme-mode-border`) |
| Text (name / meta / labels) | `--theme-mode-text`, `--theme-mode-text-secondary`, `--theme-mode-text-muted` |

**LIVE is a STATUS colour, not a theme colour** (same rule as medals/records in
`design_handoff_theme_modes`): context-independent, mode-dependent pair —
light `#148253` on `rgba(20,130,83,.09)`, dark `#3ddc97` on `rgba(61,220,151,.10)`.
Add these two as new status tokens; do not reuse `--theme-primary` even when the
training context happens to be green.

Active-tab text: white in light mode, `#0f1319` in dark mode (dark-mode accents
are bright).

Homepage tiles stay on the homepage's own dark hero styling (it is not part of the
mode system today) — only the results-page selector follows mode × context.

## Files to touch in `client/`
- `src/projects/components/filter-data-source-ddl/*` — replace the react-select
  multi-select with this selector (keep `loadFromApi` / `loadPicked` logic; the
  multi-source combine feature is dropped from primary UI — confirm with product
  whether multi-select stays as an "advanced" mode before deleting).
- `src/pages/results-main-page.tsx` — read `category` / `competitionId` / `eventId`
  from the URL on mount; write them back on user selection (history.replaceState).
- `src/projects/home-project/home.tsx` — category tiles / grouped list linking
  with the params above.
- `src/utils/constants/filter-constants.ts` — category keys + labels.
- `src/index.css` — the two new LIVE status tokens (light/dark).

## States covered by the reference DC
- Category with live + upcoming + months + collapsed archive (Junior)
- Category with no live (Masters after its live ends) — "Live · Upcoming" block hides
- Selected row (accent tint + accent border), URL readout updates to `competitionId`/`eventId`
- All 4 mode × context combinations via the preview toggles (toggles are a review
  tool — do not ship)

Not designed yet (ask if needed): search-active state with no matches; mobile layout
(tabs likely become a horizontally scrollable row); multi-select/combine mode.

## Files in this bundle
- `Category Selector.dc.html` — interactive theme-wired reference. Open in a browser;
  toggle Light/Dark × Training/Competition, switch tabs, click rows, expand archive.
- `competitions-by-category.json` — mock of the extended `/api/competitions` payload
  (with `category`, `status`) the client should receive.
