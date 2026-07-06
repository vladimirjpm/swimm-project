# Handoff: Competition selector → header collapse (desktop + mobile) — ALL CHANGES

Single delta spec on top of the ALREADY-IMPLEMENTED category selector
(the one built from `design_handoff_category_selector/README.md`: category tabs,
search-in-category, season, Live·Upcoming, month groups, archive, URL contract,
token system). Nothing in that base spec changes except where noted.

References in this folder (open in a browser, all interactive & token-wired):
- `Category Selector.dc.html` — desktop: click a row → collapses into header; "Change" → dropdown.
- `Selector Mobile.dc.html` (+ `Row.dc.html` child) — mobile: toggles Light/Dark ×
  Training/Competition × Selected/`?category=…`; "Change" → bottom sheet.
- `competitions-by-category.json` — extended `/api/competitions` mock (`category`, `status`).

---

## Change 1 — Selected → compact competition header (desktop & mobile)

When `competitionId`/`eventId` is resolved, do NOT render the expanded selector
and REMOVE the old standalone green banner — one accent header block replaces both:

- Background `--theme-primary`; text = accent-text (white in light mode, `#0f1319`
  in dark — same rule as active tabs). Never hardcode green.
- Desktop: single row ~72px — name (RTL, ellipsis) + meta (date · pool · month ·
  season) left, **"Change"** ghost button right.
- Mobile (<640px): two rows — name + **"⇄ Change"** (min-height 44px,
  `white-space: nowrap`), meta line below (12px, opacity .85).

## Change 2 — "Change" opens the selector as an overlay

Same selector component, two hosts:
- **Desktop: dropdown** under the header — absolute, full header width,
  top = header bottom + 10px, z-index above content, shadow
  `0 18px 44px rgba(...,.18)`, ~180ms fade/slide. Page content does not reflow.
  "✕ Close" in the panel header; closes on pick / Close / outside click / Esc.
- **Mobile: bottom sheet** — fixed inset-0 backdrop `rgba(10,14,20,.5)`,
  sheet from bottom (max-height 88%, radius 22px top, drag handle, ~220ms slide-up,
  `env(safe-area-inset-bottom)`). Sticky title + ✕ (44×44); tabs = horizontally
  scrollable pill row (min-height 40px, nowrap); search 44px; rows min-height 52px.
  Closes on pick / ✕ / backdrop / swipe-down / Esc; lock body scroll while open.
- `prefers-reduced-motion: reduce` → no animations.

## Change 3 — Deep link `?category=…` without a competition

- No header. The selector renders **inline, already open**, pre-filtered to the
  URL category (active tab, search scope, list).
- Page content below is dimmed (`opacity ~.45–.55`, `pointer-events: none`) with
  the hint "Select a competition above to see results".
- First pick → collapses into the header (Change 1). Category stays in the URL.

## Change 4 — Rule change: category switch no longer clears selection

Switching a tab inside the open panel/sheet only filters the list; the current
competition (and header) stays until a new row is clicked. (The old base-spec
rule "category switch resets selection" is superseded.)

## Open question — many categories on mobile (not yet decided)

The tab row clips when categories outgrow 390px. Two designed options
(frames 9a/9b in the main project's `Homepage Options.dc.html`); do NOT
implement until product picks one:
- **9a**: keep the scroll row, add right fade-gradient + "›" chevron affordance,
  auto-scroll active tab into view. OK up to ~6–8 categories.
- **9b**: replace mobile tabs with a "Category: Junior · 9 ▾" select row (48px)
  opening its own menu (rows 46px, ✓ on active). Scales to any count; desktop
  keeps tabs.

## Files to touch in `client/`

- `src/pages/results-main-page.tsx` — render header vs inline-open selector based
  on resolved `competitionId|eventId`; dim content when none; remove old banner.
- Selector component (`src/projects/components/filter-data-source-ddl/*` or where
  it landed) — extract panel body so it renders in three hosts: inline (deep link),
  desktop dropdown, mobile bottom sheet. Add open/close state, outside-click/Esc,
  body-scroll lock (mobile).
- Breakpoint: <640px (Tailwind `sm`) switches dropdown → bottom sheet.
- Tokens per the table in the base handoff; LIVE stays a status colour pair
  (light `#148253`/`rgba(20,130,83,.09)`, dark `#3ddc97`/`rgba(61,220,151,.10)`).
- Tap targets: buttons ≥44px, mobile rows ≥52px.

## Not designed yet (ask if needed)
- Landscape phones / tablets; LIVE competition inside the header;
  multi-day event header (day switcher); swipe-down gesture visual.
