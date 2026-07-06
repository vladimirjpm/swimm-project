# Handoff: ISR Age Records — mobile (<640px)

## Overview
Mobile adaptation of the Age Records strip from `design_handoff_age_records`
(**read that README first** — target file, data structure, gender/UI tokens and the
component's public API are identical and not repeated here). Design exploration:
frames **2a–2g** in the main project's `Mobile Results Options.dc.html`; the chosen
variants are 2e (collapsed), 2b, and 2f.

## Reference
`Age Records Mobile.dc.html` — open in a browser. Shows the three mobile states
side by side with real data from `data/normative-age-records.js`. The "many records"
card is interactive: tap the header to expand/collapse.

## Target file
Same as desktop handoff:
`client/src/projects/results-table/components/normative-age-records.tsx`.
Props, helpers, and `window.normative_age_record` unchanged. Only the mobile
(<640px) rendering changes; desktop keeps the design from
`design_handoff_age_records`.

## States

### 1. Many records (age === 'all') — collapsed card, tap to expand
Replaces the two stacked tile-grid cards (too tall for mobile).
- **Collapsed (default):** one card, single header row, min-height 44px, whole row
  tappable: 🏅 + title `ISR Age Records` (13.5px/800) + ♂ and ♀ mini-pills +
  `10–18y` range label (10px, `#aab0bd`) + chevron `▾`.
- **Expanded (tap):** table under a `1px #eef1f6` divider. Grid
  `1fr 52px 1fr`, gap `4px 8px`:
  - Header row: `♂ MAN` (10px/800, male accent, right-aligned) · `AGE`
    (10px/800, `#9098a4`, centered) · `♀ WOMAN` (10px/800, female accent).
  - One row per age: male time right-aligned on `chipBg` tint
    (`#f6faff`, radius 8px, padding 5px 10px), age chip `{age}y`
    (11px/800, `#5b6470`), female time on `#fff7fb`.
  - Times: 15px / 800 / tabular-nums / gender `deep` color.
- Chevron rotates 180° when open (`transform .15s ease`).
- Optional: tap a time cell → holder/club/date (tooltip/popover) — replaces the
  desktop hover tooltip; not mocked, ask if needed.

### 2. Two records (single age, gender = all) — one card, two columns
Replaces the two stacked single-age cards.
- Shared centered header: 🏅 + eyebrow `NATIONAL RECORD · {age}Y`
  (10px / 800 / letter-spacing .09em / uppercase / `#9098a4`).
- Grid `1fr 1px 1fr` (middle column = `#e9edf3` divider), gap 12px. Each column,
  centered: gender pill → time (28px / 800 / tabular-nums / gender `deep`) →
  holder name (RTL, 12.5px/700, ellipsis) → date (10px, `#aab0bd`, tabular-nums).

### 3. One record (single age, single gender) — horizontal strip
Compressed version of the desktop single-age card:
- Card `position:relative; overflow:hidden`, radius 14px, padding
  `12px 14px 12px 18px`, flex row gap 14px.
- Accent edge: absolute left, 4px wide, gender `accent`.
- Left block (flex 1, min-width 0), stacked: name (RTL, 13px/700, ellipsis) →
  club (RTL, 11px, `#8a93a3`, ellipsis) → date (📅 + 10px, `#aab0bd`,
  tabular-nums).
- Gender/age pill `♂ 10y` (12px/800, gender `accent` on `soft`, padding
  5px 11px, radius 999px, flex-shrink 0) sits BEFORE the divider.
- `1px #e9edf3` vertical divider.
- Right block (flex-shrink 0, right-aligned): eyebrow `NAT. RECORD`
  (9px/800/uppercase/`#9098a4`) + time (30px / 800 / tabular-nums /
  gender `deep`).

## Breakpoint & implementation notes for `client/`
- Breakpoint: <640px (Tailwind `sm`), same convention as
  `design_handoff_selector_mobile`.
- Expand/collapse is the ONLY internal state (`useState(false)`); everything else
  derives from props + data, as on desktop.
- Tap targets ≥44px for the collapsible header.
- Cards: white surface, radius 14px, on the page background token; same shadow
  model as desktop handoff.
- Dark theme: not mocked for mobile — map the same gender/UI dark tokens from the
  desktop README table; ask if a mocked reference is needed.
- `prefers-reduced-motion: reduce` → no chevron/expand animation.

## Design tokens
All colors are the LIGHT tokens from `design_handoff_age_records/README.md`
(gender accent/deep/soft/chipBg/chipBorder + UI surface table). No new tokens.

## Files in this bundle
- `Age Records Mobile.dc.html` — interactive design reference (tap-to-expand works).
  Prototype-only: the demo Tweaks props (`pairAge`, `singleGender`, `openByDefault`) —
  in the app these come from filter state.
- `data/normative-age-records.js` — real dataset (already in the app at
  `client/public/data/normative-age-records.js`).
- `support.js` — prototype runtime only; **ignore for implementation**.
