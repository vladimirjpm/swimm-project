# Handoff: /my-media — header + filter bar (Ф4), themed

## Overview
Redesign of the top of `/my-media` (SwimHub, `client/src/projects/my-media-project/`): the profile header becomes two folder-tabs (My swims / Moderation), the favourite-swimmer chips move into the filter panel, and a **selected-filters bar** (`FilterBar`, geometry `card`/`columns`) appears above the swim list. Mobile keeps the same content in the `rows` geometry with a floating **Filters** pill identical to the results page. Everything is expressed through local theme tokens so the page can switch to the `deep` palette (Ф5) with light/dark.

Decisions taken in review (Vlad, 2026-09-07): header **2a** (desktop) / **2c** (mobile), filter bar **1a**, Swimming Style card and distances **reuse the results-page components as-is**.

## About the Design Files
Files here are **design references in HTML** (Design Component prototypes), not production code. Recreate them in the existing React + TS + Tailwind client using the shared components already in the branch: `FilterHost`, `FilterCard`, `FilterBar` (filter-section/filter-bar.tsx + .css), `MobileFiltersDrawer` (variant `sheet`), `FilterSwimmingStyle` + `FilterDistance`, `UI_SwimmStyleIcon`, `SwimRow`. Do not write new geometry — only add the new cards (Swimmers) and override palette tokens on the page container.

- `My Media v4.dc.html` — **the target**: 3a desktop (1440) + 3b mobile (390). Tweaks → `theme: dark | light`.
- `My Media Filter Options.dc.html` — exploration board (turns 1–2). Chosen: 2a, 2c, 1a. Others are reference only.
- `assets/styles/*.png` — copies of `client/public/images/swimm-style-icon/` used by the mocks (no new assets).

## Fidelity
**High-fidelity** for layout, hierarchy, sizes and token mapping. Colours are the current hard-coded my-media palette expressed as tokens; when Ф5 lands, remap the `--t-*` values to `--deep-*` (see Design Tokens) — do not copy the hexes.

## Screens

### 3a Desktop (≥1024)
Page padding 28px top, 48px sides. Topbar = existing `AppTopbar`.

**Header row** (flex, align-end, gap 24)
- Left block: eyebrow `MY PROFILE · {userName}` — 11px/800, uppercase, letter-spacing .28em, `--t-accent`; H1 **My media** 40px/900, line-height .95, letter-spacing −.04em, `--t-text`.
- **Folder tabs** (flex, align-end, gap 6, margin-left 16). Active tab: radius 12 12 0 0, 1px `--t-accent` border, **no bottom border**, bg `--t-surface`, padding 12 20 14, `margin-bottom:-1px`, z-index 1 (sits on the panel's top rule). Inactive: 1px `--t-border`, transparent, padding 10 20 12. Content: title 14px/800 (`--t-accent` active / `--t-text-2` inactive) + subline JetBrains Mono 11px/800 (`70 swims · 25/26` in `--t-text-2`; Moderation: badge 16px pill bg `--t-warn` text `--t-warn-ink` + `waiting` in `--t-warn`).
- Right: **+ Add link** — mono 13px/800, radius 10, bg `--t-cta`, text `--t-cta-ink`, padding 10 18, `margin-bottom:12px`.
- Removed: `Media` / `My groups ↗` / `Settings · soon` chips. `My groups` goes to the avatar menu in the topbar.

**Content panel** — margin 0 48 40, `border-top:1px solid --t-accent`, bg `--t-surface`, radius 0 12 12 12, padding 20 20 24. Inside: grid `320px minmax(0,1fr)`, gap 20.
In **Moderation** mode the mode accent switches: eyebrow, active tab border/bg, panel top rule, card borders use `--t-warn` / `--t-warn-soft` / `--t-warn-border` (see Options board 2a bottom frame). Implement as two token values on the panel container (`--mode-accent`, `--mode-soft`), not as per-element colours.

**Sidebar** (`FilterHost` sidebar, 320px, gap 10) — card order and default open state:
1. **Swimmers** (new card) — open. Rows 6px gap: 22px initial avatar + name (`dir="rtl"`, 13px/700) + count mono 11.5px/800 `--t-text-2`. Selected row: 1px `--t-accent`, bg `--t-accent-soft`, text `--t-accent`; idle: 1px `--t-border`. Summary in card header shows the selected name (RTL). Single-select for now.
2. **Video** — open. Three `fseg` rows: All swims · 70 / With video · 10 / Without video · 60.
3. **Swimming Style** — open when a style is set, else collapsed. **Exactly `FilterSwimmingStyle` + `FilterDistance`** from results: column of style buttons (All, then 5 icons at `w-20`), dashed left border, distance column. Only change: style buttons get a light plate `--t-plate` (#e6f2fb) behind the PNG so the black silhouette reads on dark — this is the Q1 answer: **plate under the icon**, one set of images.
4. Competition · 5. Date range · 6. Shared with · 7. Publication status (only when Video = With video) — collapsed, header shows `All ▾`.
Below: `Reset all · N` text button, mono 11.5px/800, `--t-accent-dim`.
Card: radius 14, 1px `--t-border` (active card: `--t-accent`), bg `--t-surface2`, padding 14 16; header title 13px/800, summary 11.5px/800 (`--t-accent` active / `--t-text-3` idle).

**Filter bar** (`FilterBar rows="columns"`, geometry `card`) — radius 14, 1px `--t-border`, bg `--t-surface2`, padding 12 8, `align-items:stretch`.
- First cell **SEASON** (not a filter — a separate request): label 11px/800 ls .8px `--t-text-3`; stepper `‹ 25/26 ›` mono 20px/800 `--t-accent` with `--t-glow`. Right rule 1px `--t-border-2`.
- Then one column per filter: Swimmer · Video · Event · Competition · Date · Shared with · Status. Column: flex column, centred, gap 8, padding 6 12, radius 10, margin 0 4. Cells divided by 1px `--t-border-2`.
- Idle column: label + value `All` 20px/800 `--t-text-2`.
- Selected column: 1px `--t-accent` border, bg `--t-accent-soft`, **flex 1.5** (idle 1) so the value never truncates; value mono 13px/800 `--t-accent`, `white-space:normal`, centred (bar.css rule: active values are never ellipsised).
- Event when set: `UI_SwimmStyleIcon styleType="icon-len"` on a `--t-plate` plate 40px tall, distance in `--t-dist` (#c1272d) top-right.
- Below the bar: `4 swims · sorted by date ↓` 11.5px/700 `--t-text-2`.

**Swim list** — existing `SwimList`/`SwimRow`; competition card radius 14, 1px `--t-border`, bg `--t-card`, `--t-shadow`. Row grid `48 28 66 1fr 72 110 120`, gap 14. Icon plate `--t-plate` 46px with `UI_SwimmStyleIcon` (lenPlate=false). Media cell: has video → `▶ 1 video` chip (1px `--t-accent`, bg `--t-accent-soft`); none → `+ Add video` dashed `--t-cta`.
Density at 1280 with a 320 sidebar: first to shrink is the media column (120→96, label `+ Video`), then the tag column (72→64), then plate 66→60 — see Options board 1c for the compact grid `40 24 60 1fr 64 96 110`.

### 3b Mobile (<1024; sidebar → `MobileFiltersDrawer variant="sheet"`)
- Header: row 1 — H1 24px/900 + eyebrow name 10px/800 uppercase ls .2em `--t-accent` + `+ Add` (mono 11.5px/800, bg `--t-cta`) right. Row 2 — two folder tabs `flex:1` each (same styling as desktop, padding 12 14 13 / 10 14 11, subline `70 · 25/26`).
- Panel: `border-top:1px solid --t-accent`, bg `--t-surface`, padding 12 16 20, gap 10.
- **Filter bar** (`FilterBar` geometry `card`, `rows` layout) — radius 12, 1px `--t-border`, bg `--t-surface2`, padding 8, gap 8:
  - Row 1 (flex, gap 6): **Season cell** (label `SEASON` 8.5px + stepper `‹ 25/26 ›` 13px/800 `--t-accent`, 1px `--t-border-2`, min-height 44) and, right-aligned, the count `4 swims · date ↓` 11px/700.
  - Row 2 — **idle filters**, `flex:1` each, nowrap, gap 4: label 8px/800 uppercase `--t-text-3` (ellipsised) over `All` 10px/700 `--t-text-2`; 1px `--t-border-2`, radius 8, padding 4 2.
  - Dashed divider 1px `--t-border-2`, padding-top 8.
  - Row 3 — **selected filters**, single line, `overflow-x:auto`, gap 6: chip = column, label 8.5px/800 uppercase `--t-text-3` **above** value mono 12px/800 `--t-accent`; 1px `--t-accent`, bg `--t-accent-soft`, radius 9, padding 5 12, min-width 56. Event chip: icon on a 26px `--t-plate` plate, distance 10px `--t-dist` top-right. Empty row → not rendered (bar rule).
- **Filters trigger** — floating pill, **same as results page**: fixed bottom 22px, centred; height 48, radius 24, padding 0 22, bg `--t-accent`, text `--t-accent-ink` 15px/800 `Filters`, badge (20px pill, bg `--t-accent-ink`, text `--t-accent`, mono 11px/900, count of active filters) and `▲` 10px; shadow `0 12px 30px rgba(2,10,24,.45)`. Opens the sheet (handle, cards, sticky footer `Show N swims`).
- Swim rows: grid `34 58 1fr auto`, gap 10; place + medal stacked, plate 44px, time mono 13.5px `--t-accent`, media chip (`▶ 1` / `+ Video`) min-height 44.
- Result: first swim row ≈ 330px from the top of the viewport (was ≈ 470).

## Interactions & Behaviour
- Tabs: switch mode; Moderation tab only for group admins; badge = pending count. Mode switch swaps the mode tokens (accent ↔ warn) on the panel container.
- Season stepper: separate server request, not part of `FilterHost` values.
- Filter bar columns/chips are read-only mirrors of `FilterHost` values, except clickable ones open their card (desktop: expands the card in the sidebar; mobile: opens the sheet scrolled to that card). `✕` on a selected mobile chip is optional — omitted in the target.
- `Reset all · N` clears every card except Season and Swimmers.
- Focus ring, hover: as in `filter-section.css` (`--fseg-hover-*`).

## State
`FilterHost` values: `swimmer_id`, `video: all|with|without`, `style_name`, `style_len`, `competition_id`, `date_from/to`, `group_id`, `pub_status`. Page state: `mode: swims|moderation`, `season`. Counts per option come from filter-hints.

## Design Tokens (page container, temporary hex → deep mapping)
Dark (current) / Light:
- `--t-bg` page gradient `160deg #0d2036 → #0b1b31 45% → #050e1c` / `#f2f6fb → #e9eff7 → #dde6f1`
- `--t-surface` rgba(8,25,48,.72) / #fff · `--t-surface2` rgba(125,211,252,.07) / rgba(3,105,161,.06) · `--t-card` gradient rgba(56,189,248,.07)→rgba(8,25,48,.8) / #fff
- `--t-border` rgba(125,211,252,.22) / rgba(3,105,161,.28) · `--t-border-2` rgba(125,211,252,.12) / rgba(3,105,161,.14)
- `--t-accent` #7dd3fc / #0369a1 · `--t-accent-ink` #04101f / #fff · `--t-accent-soft` rgba(125,211,252,.14) / rgba(3,105,161,.10) · `--t-accent-dim` 60% / 70%
- `--t-text` #f3f8fd / #0b1b31 · `--t-text-2` rgba(203,224,240,.62) / rgba(11,27,49,.66) · `--t-text-3` .40 / .42
- `--t-plate` #e6f2fb (both) · `--t-plate-ink` #0b1b31 · `--t-dist` #c1272d
- `--t-cta` = `--t-accent` (both). **Q5 answer:** the green #38ef8f is dropped; primary actions use the accent. If a "success" colour is still needed for Published, take it from `--deep-*` semantic tokens, not #38ef8f.
- `--t-warn` #ffca7a / #b45309 · `--t-warn-soft` rgba(255,202,122,.10) / rgba(180,83,9,.08) · `--t-warn-ink` #3a2a08 / #fff · `--t-warn-border` .45 / .40
- `--t-glow` 0 0 18px rgba(125,211,252,.35) / none · `--t-shadow` 0 18px 50px rgba(2,10,24,.5) / 0 10px 30px rgba(11,27,49,.1)

Map to the shared component tokens on `.my-media`: `--fc-*` (card) · `--fseg-*` (segments) · `--fb-*` (bar: `--fb-bg`=surface2, `--fb-border`=border, `--fb-divider`=border-2, `--fb-label`=text-3, `--fb-idle-text`=text-2, `--fb-active-bg`=accent-soft, `--fb-active-border`=accent, `--fb-active-text`=accent) · `--fdrawer-*` · `--sr-*`. In Ф5 replace the right-hand values with `--deep-*` and drop the hex block.

Type: Inter (UI), JetBrains Mono (labels, counts, times, buttons). Radii: cards 14, bar 14/12, chips 8–10, tabs 12 top, pill 24. Mobile hit targets ≥ 44px.

## Open questions answered
1. Icons on dark — light plate under the icon, one image set. 2. Card order — Swimmers, Video, Swimming Style (open), then Competition, Date, Shared with, Status (collapsed). 3. Bar — yes, chips: Swimmer · Video · Event · Competition · Date · Shared with · Status (+ Season cell). 4. Tablet 640–1024 — sheet + floating pill, sidebar from 1024 (unchanged). 5. Green — replaced by accent. 6. Density at 1280 — shrink media column first (see 3a notes).

## Files
- `My Media v4.dc.html` — target (open in a browser with `support.js` alongside)
- `My Media Filter Options.dc.html` — exploration board
- `assets/styles/` — swimming style PNGs (copies)
- `support.js` — prototype runtime, required to open the .dc.html files
