# Handoff: Normative Info popup — redesign

## Overview
Redesign of the **Normative Info** popup (opens on click of a swimmer's **Level** badge in the
results table). It shows the qualification-time matrix for the selected event: rows = distances,
columns = **World record**, **ISR record**, then the level ladder (MSMK · MS · KMS · I · II · III ·
1y · 2y · 3y). Every cell shows two times: **Men (top, blue)** / **Women (bottom, pink)**.

The redesign delivers, in **two visual variants** the user can choose between:
1. A polished, responsive **desktop matrix** with the swimmer's current level marked and the
   **next (faster) level = "TARGET" column glowing**, plus the swimmer's event row highlighted.
2. A dedicated **mobile compare view**: two columns — the swimmer's current level and the next
   faster one — with **arrow pager** to step across neighbouring levels, the swimmer's distance on
   top and other distances collapsed, and a **"to reach next level" delta** (seconds to cut).
- Light **and** dark theme.

## About the Design Files
The files here are a **design reference built in HTML** — a prototype of look & behaviour, **not**
production code to paste. Recreate it inside the existing **React + TypeScript + Tailwind** app,
reusing its patterns (CSS vars `--theme-mode-*`, existing `Helper`, level/stroke icon components).
`support.js` is the prototype runtime only — **ignore it**; read the `<x-dc>` markup and the
`class Component` logic for intent, layout, and exact values.

## Target file
Rework **`client/src/projects/components/popup/popup-content-normative.tsx`**.
Keep its data plumbing **unchanged**:
- Reads `window.normative` (thresholds) and `window.normative_record` (WR/ISR records).
- Props from `state.popUpObj`: `levelName, styleName, styleLen, poolLen, poolType, isMasters,
  normativeAgeGroup`. `levelName` = swimmer's current level → drives "YOU"/"TARGET".
  `styleLen` = swimmer's distance → highlighted row / mobile top distance.
- Existing helpers `Helper.formatSecondsToTimeString`, `parseTimeToSeconds`, `resolvePoolType`,
  `resolveGender`, and the pool/stroke/age-group state — reuse as-is.
- Masters mode (`isMasters`) and the age-group selector must keep working; the redesign applies to
  the normal (age-level) matrix. Masters can reuse the same table styling.
Only the **presentation** (table markup, mobile view, styling, highlight logic) changes.

## Fidelity
**High-fidelity.** All colours / spacing / radii / type below are final. Reproduce closely with
Tailwind arbitrary values where no token exists. Two variants are both intended deliverables — ship
whichever the user picks (default recommendation: **Variant A**, softer/cleaner).

---

## Data structures (unchanged)
**Thresholds** — `window.normative.normatives[gender][pool][stroke][dist][levelKey] = "M:SS.xx"`
(string, e.g. `"0:59.05"`, `"1:33.10"`; sub-minute may be `"29.05"`).
`gender ∈ {male,female}`, `pool ∈ {25m_pool,50m_pool}`, `dist` like `"50m"`,
`levelKey ∈ {MSMK, MS, KMS, I_adult, II_adult, III_adult, I_youth, II_youth, III_youth}`.

**Records** — `window.normative_record.normatives[gender][pool][stroke][dist].{WR,ISR} =
{time, name, country, record_date}`. Fall back to `50m_pool` if the pool node is missing.

Parse to seconds by splitting on `:` (supports `SS.xx`, `M:SS.xx`, `H:MM:SS.xx`). Format back:
`m>0 ? m + ":" + ss.toFixed(2).padStart(5,"0") : ss.toFixed(2)`. Use existing `Helper` equivalents.

## Level ladder (strong → weak, left → right)
`MSMK, MS, KMS` = **pro** · `I, II, III` (adult) = **adult** · `1y, 2y, 3y` (youth) = **youth**.
Labels: `I_adult→I, II_adult→II, III_adult→III, I_youth→1y, II_youth→2y, III_youth→3y`.
- **current** = `levelName` (case-insensitive match). Badge **"YOU"**. This is the visual focus:
  its **whole column glows** — a pulsing accent border down the column (medallion + cell left/right
  edges) via animated `box-shadow`, plus the category `soft` column tint.
- **current** = `levelName` (case-insensitive). Its header medallion keeps a small **"YOU"** badge
  (no glow). The **only glowing element is the single intersection cell** at (swimmer's distance ×
  current level): category `soft` bg + pulsing 2px accent inset ring (`cellGlow` keyframe, element
  sets `--gc: accent`), radius 9, Men/Women times bold.
- **target** and the rest of the current column get **no styling**.
- The swimmer's distance **row** still gets its amber wash + left accent bar (separate from the cell).

---

## View 1 — Desktop matrix
Column order: **Distance · WR · ISR · [ladder strong→weak]**. Horizontal scroll on overflow;
Distance column is `position:sticky; left:0`.

- **Header medallions**: rounded 11px, `min-width 46px`, `height 36px`, weight 900, 15px,
  category-coloured. The current level shows a small "YOU" badge above it — no medallion glow.
- **Highlight = one cell only**: the (current distance × current level) cell gets category `soft` bg,
  radius 9, and a pulsing 2px accent inset ring (`cellGlow`: `0/100% inset 0 0 0 2px var(--gc),
  0 0 10px -3px var(--gc); 50% …,0 0 22px 1px var(--gc)`). Nothing else in the matrix is tinted or
  bordered per-level.
- **Swimmer's distance row**: distance cell tinted amber (`#fffbeb` / dark `rgba(245,184,0,.10)`) +
  `border-left: 3px solid` accent + small "YOUR EVENT" caption; the row's cells get a faint amber wash.
- **Cross cell** (swimmer distance × current/target level): `inset 0 0 0 2px accent` ring.
- **Cell content**: Men pill (top) then Women pill (bottom), stacked gap 4px. Pill: radius 8px,
  padding `5px 4px`, 13px/700, `tabular-nums`, centered. Target-column times go weight 900.
- **WR / ISR** cells: same Men/Women pill pattern; header medallions neutral (`rec` palette) with
  captions "World" / "Israel".

## View 2 — Mobile compare (`<760px` or Mobile toggle)
Popup max-width ~420px.
- **Top**: swimmer distance big (30px/900) + `stroke · pool`.
- **Pager row**: `‹` button · centered label `LEVEL → FASTER` · `›` button. `‹` steps toward slower,
  `›` toward faster; disable at ends (opacity .4). Buttons 40×40, radius 11, subtle bg, active scale .9.
- **Two column cards** (grid 1fr/1fr, gap 12): left = current pair level, right = faster (target).
  - Right (target) card: 2px accent border + soft accent glow shadow; tag `FASTER →` (filled accent).
  - Left (current) card: tag `YOU`/`CURRENT` (soft accent); its medallion carries the same pulsing
    `levelGlow` border as the desktop YOU medallion. Level medallion `min-width 60`, `height 44`, 18px.
  - Under medallion: Men pill (♂ + time) and Women pill (♀ + time) for the swimmer's distance.
    Pill: space-between, radius 9, padding `8px 11px`, time 17px/800 tabular-nums.
- **Delta**: the seconds-to-cut (current threshold − target threshold) is shown **inside the faster
  (right) column**, small and right-aligned directly under each time (e.g. `26.85` with `−2.20`
  beneath it, per gender). No separate "To reach" block.
- **Other distances**: collapsible header (subtle bg, chevron rotates). Expanded: one card per other
  distance, each with the two level columns' Men/Women times side by side.

## Variants
- **Variant A ("Aurora")** — level medallions are **soft-tinted chips** (category `soft` bg,
  `deep` text, `border`). Airy, premium. Target = glow ring + tinted column.
- **Variant B ("Podium")** — medallions are **solid accent fills** (accent bg, white text). Bolder,
  higher-energy. Same glow/target logic. Everything else identical.
Implement as a single component with a `variant` switch controlling only the medallion fill.

## Interactions & Behavior
- Pool (25m/50m) and stroke (5 options) selectors re-query the data and re-render — existing state.
- Desktop↔mobile is **responsive** (breakpoint ~760px); the prototype's top "Preview / Swimmer"
  control bar is **demo-only — do NOT port it** (variant, device, theme, my-level, my-distance are
  fed by app state / real viewport in production).
- Mobile pager updates the compared pair via local state (`pairIdx`), clamped to `[1, ladder.length-1]`.
- **Pager transition**: on next/prev the two-column grid replays a directional slide-in
  (`translateX(±40px)` + `opacity .45→1`, `.44s cubic-bezier(.4,0,.2,1)` — even Material ease, no
  overshoot) — enters from the right when going faster (`›`), from the left when going slower (`‹`).
  Pager buttons also scale to .9 on `:active`.
- Transitions ~.2–.25s on cells/medallions; target glow is the only looping animation.

## State Management
Local UI state only: `poolType, stroke` (existing), plus new `pairIdx` (mobile pager) and
`othersOpen` (mobile collapse). Everything else derives from props + the two globals + app theme.
Read light/dark from the app's existing theme mechanism (CSS vars `--theme-mode-*` already in use).

## Design Tokens

### Category palette — LIGHT
| cat | accent | deep (text) | soft (bg) | border |
|---|---|---|---|---|
| pro (MSMK/MS/KMS) | `#ea580c` | `#7c2d12` | `#fff0e6` | `#fcd5bb` |
| adult (I/II/III) | `#4f46e5` | `#312e81` | `#eeecfe` | `#d6d1fb` |
| youth (1y/2y/3y) | `#16a34a` | `#14532d` | `#e7f6ed` | `#c2ebd1` |
| records (WR/ISR) | `#475569` | `#0f172a` | `#eef1f6` | `#d7dde6` |

### Category palette — DARK
| cat | accent | deep | soft | border |
|---|---|---|---|---|
| pro | `#ff8a4c` | `#ffd9c2` | `rgba(255,138,76,.16)` | `#5a3320` |
| adult | `#8b83ff` | `#dcd8ff` | `rgba(139,131,255,.17)` | `#332f5c` |
| youth | `#3ecb7f` | `#c7f0d8` | `rgba(62,203,127,.15)` | `#245038` |
| records | `#94a3b8` | `#e2e8f0` | `rgba(148,163,184,.14)` | `#2b3442` |

### Men / Women pills
| | light | dark |
|---|---|---|
| Men bg / text | `#e6f0fd` / `#1d4ed8` | `rgba(90,162,245,.16)` / `#9cc4fb` |
| Women bg / text | `#fdeaf2` / `#be185d` | `rgba(240,114,166,.16)` / `#f6a9cd` |

### Surface / UI
| token | light | dark |
|---|---|---|
| page | `#e9ebef` | `#0d1017` |
| card | `#ffffff` | `#151a22` |
| card border | `#e6e9ee` | `#232b36` |
| subtle / control bg | `#f1f3f7` | `#1c232e` |
| grid line | `#eceff4` | `#232b36` |
| text | `#141824` | `#eef2f7` |
| label / muted | `#8a93a2` | `#8794a4` |
| card shadow | `0 20px 60px rgba(20,28,45,.14)` | `0 20px 60px rgba(0,0,0,.5)` |
| your-event row wash | `#fffbeb` | `rgba(245,184,0,.10)` |

### Misc
- Font: Inter (use the app's font).
- Radii: medallion 11, cell pill 8, cards 16, popup 22, control pills 8–9.
- Glow keyframes (element sets `--gc: <accent>`):
  - `levelGlow` (YOU medallion): `0/100% box-shadow:0 0 0 1.5px var(--gc),0 0 9px -1px var(--gc);
    50% box-shadow:0 0 0 1.5px var(--gc),0 0 22px 3px var(--gc)`.
  - `colGlow` (YOU column cells/header): `0/100% box-shadow:inset 2px 0 0 var(--gc),
    inset -2px 0 0 var(--gc),0 0 10px -3px var(--gc); 50% …,0 0 20px 0 var(--gc)`.

## Assets
Inline emoji `📊`, `♂`, `♀`, arrows `‹ ›`. Reuse existing `UI_NormativeLevelIcon` /
`UI_SwimmStyleIcon` if you prefer the app's medal icons over text medallions.

## Files in this bundle
- `Normative Info.dc.html` — the prototype (read markup + `class Component`; the `cat()`, `ui`,
  `headMed()`, and `renderVals()` methods hold every value above).
- `data/normative.js`, `data/normative-records.js` — the real datasets (already in the app under
  `client/public/data/`).
- `assets/style-*.png` — the site's stroke icons (freestyle, backstroke, breaststroke, butterfly,
  medley) used by the Swimming Style filter. Reuse the app's existing icon files if identical.
- `support.js` — prototype runtime, **ignore for implementation**.

---

# UPDATE — July 2026 revision (implement on top of everything above)

## 1. Swimming Style filter → combined dropdown (replaces the stroke pill row)
The row of stroke text-pills is **removed**. In its place, next to the 25m/50m pool toggle:
- **Collapsed control** (clickable, `border-radius: 13px`, subtle bg `#f1f3f7` light / `#1c232e`
  dark, padding `11px 18px`): `Swimming Style` (16px, weight 800, text color) + **current stroke
  icon** (72×36, `background-size: contain`, from `assets/style-<stroke>.png`) + `· 50m`
  (16px, weight 800, adult accent color) + chevron `▲` (rotates 180° when closed, `.2s ease`).
- **Expanded panel** (absolute, below control, `z-index: 30`, card bg, 1px card border,
  `border-radius: 16px`, card shadow, padding 14px, flex row, gap 14px):
  - **Left column**: 5 stroke buttons stacked (gap 8px), each `118×52px`, `border-radius: 13px`,
    icon centered as background-image (`auto 36px`). Inactive: `1.5px solid` card-border,
    subtle bg. **Active**: `1.5px solid` adult accent, soft accent bg, ring
    `box-shadow: 0 0 0 2px <accent>33`. Right edge: `1px dashed` grid-line divider.
  - **Right column**: distance chips stacked (gap 8px), padding `9px 18px`,
    `border-radius: 11px`, 13px/800. Inactive: card-border + subtle bg + text color.
    **Active**: accent bg, white text.
  - Selecting a stroke keeps the panel open; if the current distance doesn't exist for the new
    stroke, auto-select the first available distance.
- Header subtitle now shows **stroke name only** (no `· 25m pool` — the pool toggle sits below).

## 2. Legend moved below the table
`In each cell: top = Men / bottom = Women` now renders **under** the desktop matrix
(padding `12px 22px 18px`), not above it. Desktop only.

## 3. Mobile compare — accent inverted (current level = focus)
- **Current level (left) card**: 2px **accent border + glow** (`box-shadow: 0 0 0 1px <accent>,
  0 8px 26px -8px <accent>`), tag `YOU`/`CURRENT` **filled** (white on accent).
- **Next level (right) card**: plain card border, tag `NEXT →` **muted** (label color on subtle
  bg, 1px card border), medallion at `opacity: .85`.
- The old "FASTER →" glowing treatment on the right card is **dropped**.

## 4. Mobile compare — row alignment
The ♂/♀ value stacks inside both cards get `min-height: 30px; justify-content: center` so the
times sit on the same baseline whether or not a delta line (`−2.20`) is present.

## 5. Mobile header
The standalone distance icon/heading above the pager was **removed** (the Swimming Style dropdown
already communicates stroke + distance).
