# Handoff: ISR Age Records — redesign

## Overview
Redesign of the **ISR Age Records** strip that appears above the results table on the
competition results screen. It shows the national age-group records for the currently
selected event (stroke + distance + pool), in one of two states:

- **All ages** — every age group's record (e.g. 10y…18y) shown at once.
- **Single age** — the record for one specific age group, with full holder detail.

Both states render once **per gender** (♂ and ♀), stacked, when gender is "all".

The current implementation renders these as a single cramped line of text
(`10y: 29.81 | 11y: 28.01 | …`). The redesign replaces that with a tile grid (all-ages)
and a proper record card (single-age), adds a **hover tooltip** that surfaces the holder /
club / date (currently only visible in single-age mode), and adds **dark-theme** support.

## About the Design Files
The files in this bundle are a **design reference created in HTML** — a prototype showing
the intended look and behavior, **not** production code to copy verbatim. The task is to
**recreate this design inside the existing React/TypeScript codebase**, reusing its
established patterns (Tailwind classes, the app's theme mechanism, existing helpers).

The prototype file (`Age Records.dc.html`) uses a small in-house template runtime
(`support.js`) purely so it can run standalone — ignore that runtime; read the markup and
the logic class for the design intent, colors, and layout.

## Target file
Rework **`client/src/projects/results-table/components/normative-age-records.tsx`**.
Keep its public API and data logic **unchanged**:
- Props: `gender, poolType, styleName, styleLen, age` — unchanged.
- Helpers `getDistanceData`, `birthYearToAge`, `resolveGenderKeys`, `Helper.resolvePoolType`,
  `Helper.resolveGender` — unchanged.
- Data source `window.normative_age_record` — unchanged (see structure below).
Only the two render functions (`renderAllAges`, `renderSingleAge`) and their styling change.

## Fidelity
**High-fidelity.** Colors, spacing, radii, and typography below are final. Recreate
pixel-close using the codebase's Tailwind setup (or arbitrary values where no token exists).

---

## Data structure (unchanged)
`window.normative_age_record.normatives[gender][poolKey][styleName][distanceKey][ageKey]`
where `gender ∈ {male, female, mix}`, `poolKey ∈ {25m_pool, 50m_pool}`,
`distanceKey` like `"50m"`, `ageKey` like `"10".."18"`. Each leaf record:
```json
{ "time": "29.81", "name": "מיכאל סמירנוב", "club": "הפועל בית שמש",
  "country": "ISR", "record_date": "21/12/2011" }
```
`name` / `club` are Hebrew → render **RTL** (`dir="rtl"`, isolate).
`record_date` may be `""` → fall back to `"—"`.

---

## Screens / Views

### 1. All-ages card (state: `age === 'all'`)
One card per gender.

**Card container**
- Background: `surface` token (light `#ffffff`, dark `#161b24`)
- Border: `1px solid` gender border token (see tokens)
- Radius: `16px`, padding `16px 18px 18px`, bottom margin `16px`
- Shadow: `0 1px 3px rgba(20,28,45,0.05)`

**Header row** (flex, gap 10px, align center, margin-bottom 14px)
- Medal chip: `30×30`, radius `9px`, background = gender `soft` token, centered `🏅` (16px)
- Title `ISR Age Records`: 15px / weight 800 / letter-spacing -0.2px / color gender `deep`
- Gender pill: text `♂ Man` / `♀ Woman`, 11px / weight 700, color gender `accent`,
  background gender `soft`, padding `3px 9px`, radius `999px`

**Tile grid**
- `display:grid; grid-template-columns: repeat(auto-fill, minmax(84px,1fr)); gap:8px`
- Each **tile** (one age):
  - Background gender `chipBg`, border `1px solid` gender `chipBorder`, radius `11px`,
    padding `9px 6px 10px`, center text
  - Age line `{age}y`: 11px / weight 700 / color gender `accent`
  - Time: 19px / weight 800 / `tabular-nums` / letter-spacing -0.5px / color gender `deep`
  - Hover: `translateY(-3px)`, transition `transform .12s ease`
  - **Tooltip** (absolute, above tile, fades in on hover; `opacity 0→1`, `translateY 4px→0`, .14s):
    - Background `tip` token (light `#1f2733`, dark `#0b0f15`), `1px` border = `tipBorder`,
      color `#fff`, padding `8px 11px`, radius `10px`, shadow `0 8px 22px rgba(20,28,40,0.3)`,
      `white-space:nowrap`, `pointer-events:none`, small pointer triangle below
    - Line 1: holder `name` (RTL) 12.5px / weight 700
    - Line 2: `club` (RTL) 11px / color `#c4cbd6`
    - Line 3: `record_date` 10.5px / color `#8b95a3` / tabular-nums

**Hint footer** (10.5px, color `label` token, margin-top 11px): info-circle icon +
`Hover an age for the record holder, club and date`.

### 2. Single-age card (state: `age !== 'all'`, resolve via `birthYearToAge`)
One card per gender (flex row, gap 22px, align center).

- Container: same surface/border/shadow as above, radius `18px`, padding `20px 22px`,
  `position:relative; overflow:hidden`
- **Accent edge**: absolute left, `width:5px`, full height, background gender `accent`
- **Left block** (column, center, gap 6px): `🏅` (30px) + pill `♂ Man · 16y`
  (11px/700, color `accent`, bg `soft`, radius 999px)
- Vertical divider: `1px`, background gender border, stretch
- **Time block**: eyebrow `NATIONAL RECORD` (10px / weight 700 / letter-spacing .09em /
  uppercase / color `#9098a4`) + time (40px / weight 800 / tabular-nums /
  letter-spacing -1.5px / color gender `deep`)
- **Holder block** (flex 1, left dashed border `1px` gender border, padding-left 18px):
  - `name` (RTL) 19px / weight 700 / color `deep`
  - `club` (RTL) 13px / color `#6b7280`, margin-top 3px
  - Date row (margin-top 8px, 12px / weight 600 / color `#9098a4` / tabular-nums):
    calendar icon + `record_date`

---

## Interactions & Behavior
- **Tooltip** on tile hover only (all-ages). No click behavior.
- **Tile lift** on hover (`translateY(-3px)`).
- Component returns `null` when: no `styleName`/`styleLen`, no data, or no matching
  distance data (unchanged from current logic).
- The prototype's top control bar (View / gender / age / theme toggle) is **demo-only** —
  do NOT port it. In the real app these come from the existing filter state and the app's
  global theme.

## State Management
None internal. Everything derives from props + `window.normative_age_record`.
Dark vs light must read the **app's existing theme** (detect how the codebase does it —
`dark` class on `<html>`, a theme context/store, or media query — and follow that).

## Design Tokens

### Gender — LIGHT
| token | ♂ male | ♀ female |
|---|---|---|
| accent | `#1e6fd6` | `#d6417f` |
| deep (text) | `#123a70` | `#7a1f4b` |
| soft (pill bg) | `#eaf2fd` | `#fdeff5` |
| border | `#d3e3f8` | `#f6d3e3` |
| chipBg | `#f6faff` | `#fff7fb` |
| chipBorder | `#e3eefb` | `#f9e2ee` |
| label | `♂ Man` | `♀ Woman` |

### Gender — DARK
| token | ♂ male | ♀ female |
|---|---|---|
| accent | `#5aa2f5` | `#f072a6` |
| deep (text) | `#dbe8fb` | `#fbdcec` |
| soft (pill bg) | `rgba(90,162,245,0.16)` | `rgba(240,114,166,0.16)` |
| border | `#28344a` | `#412234` |
| chipBg | `#1a2436` | `#2a1a24` |
| chipBorder | `#26344d` | `#40263a` |

### UI surface tokens
| token | light | dark |
|---|---|---|
| page | `#eef0f3` | `#0f131a` |
| card / surface | `#ffffff` | `#161b24` |
| label text | `#9098a4` | `#8b95a3` |
| muted text | `#6b7280` | `#6b7280` |
| divider | `#e3e7ec` | `#28303c` |
| tooltip bg | `#1f2733` | `#0b0f15` |
| tooltip border | `#1f2733` | `#2a323f` |

### Type / misc
- Font: Inter (UI), Heebo for Hebrew names/clubs (or the app's existing Hebrew font).
- Radii: tile 11px, all-ages card 16px, single card 18px, pills 999px.
- Shadow: `0 1px 3px rgba(20,28,45,0.05)`.

## Assets
None beyond inline SVG (info-circle, calendar) and the 🏅 emoji. Reuse the codebase's
icon library if one exists.

## Files in this bundle
- `Age Records.dc.html` — the design prototype (read markup + `class Component` logic).
- `data/normative-age-records.js` — the real records dataset (already in the app at
  `client/public/data/normative-age-records.js`).
- `support.js` — prototype runtime only; **ignore for implementation**.
