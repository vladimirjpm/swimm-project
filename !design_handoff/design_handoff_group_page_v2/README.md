# Handoff: Group page redesign (`/groups/{slug}`)

## Overview
Redesign of the hub-group page (`client/src/projects/hub-groups-project/groups.tsx`). Today it is one long column (sidebar roster + season standings → records → gallery → from members → publication requests → recent swims). Most visitors come from a phone, the page is too long, and it lacks the swim elements other pages already have (position badge, pool chip, gap, record tile).

New structure (chosen variant **2a** in `Group Page v2.dc.html`):
1. Hero (from 1c): avatar, name, actions, **Training info** card, next-training strip, 3 KPIs, **group photo** on the right.
2. **Folder tabs** — the same `DeepTabs` used by the club and swimmer pages.
3. Tab panel. Overview = Group records → Last start + Members/Next meet → Media.

Tabs: `Overview · Results · Media · Swimmers · Trainings · Admin`. Records and Competitions live **inside Results**, not as separate tabs. Admin (publication requests) is its own tab, visible only to the group creator/coach.

## About the design files
`Group Page v2.dc.html` is an HTML **design reference**, not production code. Recreate it in the existing React/Tailwind codebase using the existing `theme-deep` tokens and the shared `deep/*` components. Variants 1a, 1b, 1c in the file are earlier explorations — build **2a** only.

## Fidelity
High-fidelity. Colours, type and spacing are taken from `deep-theme.css`; replicate 1:1 with the tokens, not with hex values.

---

## ⚠ One component set for club, swimmer and group

**Hard requirement from the product owner:** the club page (`/clubs/:id`), the swimmer page (`/swimmers/:id`) and this group page must render **the same tab component and the same card primitives**. No page may keep its own copy of tabs, cards, record tiles or swim rows — one implementation, three consumers.

What already exists and MUST be reused (do not fork):

| Piece | Source of truth | Used by |
| --- | --- | --- |
| Folder tabs | `projects/components/deep/tabs.tsx` (`DeepTabs`) + `.deep-folder` / `.deep-tabs-panel` in `deep-theme.css` | club ✓, swimmer ✓, **group — adopt** |
| Card shell | `.deep-card`, `.deep-card-title`, `.deep-card-sub` (`deep-theme.css`) | club ✓, swimmer ✓ (partly own CSS in `swimmer-page.css` — see below), **group — adopt** |
| Record tile / record card | `club-project/components/club-record-card.tsx` (`ClubRecordCard`, `ClubRecordTile`, `ClubRecordSection`) | club ✓, **group — adopt** |
| Pills / segmented control | `.deep-pill`, `.deep-seg` | club ✓, **group — adopt** (All / 25m / 50m) |
| Time | `mix/swim-time/swim-time.tsx` (`UI_SwimTime`) — the only allowed way to print a time | all |
| Hero + KPI | `club-project/components/club-hero.tsx` (`Kpi`, `Badge`) | club ✓, **group — extract and reuse** |
| Theme class | `deep/use-deep-theme-class.ts` | all |

Refactor steps that come with this task:
1. Move `club-record-card.tsx` and the `Kpi`/`Badge` helpers from `club-hero.tsx` into `projects/components/deep/` so they are neutral shared components (`deep/record-card.tsx`, `deep/kpi.tsx`). Update the club page imports.
2. Where `swimmer-page.css` re-declares card/row styles that duplicate `.deep-card` / `.deep-card-bg-row` recipes, replace them with the shared classes.
3. Delete the group page's private `hp-*` list/table styling for members and records; render them with the shared primitives listed above.
4. Add a shared **swim row** (`deep/swim-row.tsx`) used by "Last start" here and by Recent swims / Results on all three pages: `[position badge] [swimmer] [event] [UI_SwimTime + gap] [pts]`. Grid `24px 1fr 130px 100px 46px`, row bg `--deep-card-bg-row`, radius `--deep-radius-row`, padding `8px 12px`, gap `12px`.

If a piece is needed that does not exist yet (attendance bar, days-of-week chips, next-training strip), create it in `projects/components/deep/` — never inside a page folder.

---

## Screens

### Desktop (≥960px) — `#2a` desktop artboard, 1180px content width

**Top bar** — existing `AppTopbar` with `active="groups"`.

**Hero** — full-width band, `background: var(--deep-hero-grad)`, `border-bottom: 1px solid var(--deep-card-border)`, padding `22px 28px 24px`.
Grid `minmax(0,1fr) 380px`, gap 24px, `align-items: stretch`.

Left column (gap 18px):
- Row: avatar 64×64, radius 16, `linear-gradient(160deg, var(--deep-accent), #0e7490)`, letter in `--deep-font-display` 30px `--deep-accent-ink`. Existing `UI_ClubLogo`-style initials are the normal state (no photo data).
- `h1` group name (Hebrew, `dir="rtl"`, left-aligned) `--deep-font-display` 32px / 1.1. Sub-line 12.5px/700 `--deep-text-mute`: `Dolphin Netanya Masters · 🇮🇱 נתניה · Masters`.
- Actions right-aligned: `+ Add video` (accent outline chip: `border 1px var(--deep-accent-border)`, bg `--deep-accent-chip`, text `--deep-accent`, 12.5px/800, padding 8×14, radius 10) and `Leave group` (border `--deep-card-border`, text `--deep-text-mute`). For non-members the second button is `Join group`.
- **Training info card** — `.deep-card` (padding 16×18), inner grid `minmax(0,1fr) 220px`, gap 20.
  - Left: title `Training info` (`.deep-card-title` 15px), then label/value rows (12.5px, label `--deep-text-mute`/700, value 800): Coach · Days · Time · Pool · Avg. attendance.
    - Days: 7 chips 22×22, radius 6, 10px/800; active = `bg --deep-accent-chip, color --deep-accent, border --deep-accent-border`; inactive = `transparent, --deep-text-ghost, --deep-card-border`. Letters S M T W T F S (Sunday first — Israel).
    - Time in `hp-mono`/JetBrains Mono 800: `20:00–21:30`.
    - Attendance: 80×6 bar, track `--deep-seg-track`, fill `--deep-accent` at `avg/roster`, then mono `12/19` (`/19` muted).
  - Right: **Next training** strip — `border 1px var(--deep-live-border)`, radius 12, padding 12; green dot 8px `--deep-live` with `box-shadow 0 0 10px`; label `NEXT` 9.5px uppercase `--deep-live`; value `Thu 10 · 20:00` 13px/800; right link `Going` / `I'm going` in accent. Below: 3 KPIs (`Kpi` component, value 24px display, label 9.5px uppercase muted): Records 20 · Gold 3 · Swimmers 19.

Right column: **group photo** slot, radius 16, `border 1px var(--deep-card-border)`, min-height 240, `object-fit: cover`. New field `hero_image_url` on the group; editable by the creator. Empty state: dashed border + "Add a group photo" for creator, hidden for others (collapse the column → left column full-width).

**Folder tabs** — `<div class="deep-folder"><DeepTabs …/><div class="deep-tabs-panel">…</div></div>` exactly as on the club page. **Change to the shared CSS (applies to all three pages):** the product owner asked to remove the dark strip behind the tab row — set `.deep-folder { background: transparent }` and drop the top hairline; the active tab still merges with the panel (`margin-bottom:-1px`, `z-index:1`).

Tab data (live numbers, never hard-coded):
```
{ id:'overview',  icon:'▦', label:'Overview',  sub:'last start · records' }
{ id:'results',   icon:'≡', label:'Results',   sub:`${swims} swims · ${records} records · ${meets} meets` }
{ id:'media',     icon:'▶', label:'Media',     sub:`🔒 ${members} · public ${pub}` }
{ id:'swimmers',  icon:'🏊', label:'Swimmers', sub:`${roster} · coach` }
{ id:'trainings', icon:'◷', label:'Trainings', sub:'🔒 Sun · Tue · Thu' }   // from schedule
{ id:'admin',     icon:'⚙', label:'Admin',     sub:`${requests} requests` }  // creator/coach only
```
Active tab lives in `?tab=` like the club page (`overview` = no param).

**Overview panel** — grid `minmax(0,2fr) minmax(0,1fr)`, gap 14, padding 18.
1. **Group records** (span 2) — `ClubRecordCard` shell: title, sub `best times by event · both pools`, count badge `20 RECORDS`, `.deep-seg` All/25m/50m. Body: 4 `ClubRecordTile`s in a 4-col grid (event, ♀/♂ in `--deep-female`/`--deep-male`, time 22px display via `UI_SwimTime`, pool chip `25m/50m` accent, holder, `date · pts`). Link `All 20 records →` opens Results tab, Records section.
2. **Last start** — `.deep-card`. Eyebrow `LAST START` 11px uppercase muted; meet name 17px display; mono meta `09/07/2026 · 50m · 3 swimmers · 11 swims`. Right: chips `● 3 gold` (gold chip tokens) and `1 DSQ` (neutral). Body: 5 shared swim rows sorted by position; position badge 24px circle — 1st: `--deep-gold-chip / --deep-gold / --deep-gold-border`; other: transparent / muted / `--deep-card-border`; DSQ: `✕`, `--deep-danger` text+border, time text `DSQ` in danger. Time colour `--deep-accent`, gap `+4.12` 10.5px ghost right after time (`gapMs` of `UI_SwimTime`). Link `All 11 swims →`.
3. Right stack: **Members** (`.deep-card`): coach first + top 3 by points; row = avatar 26px (bg `--deep-male`/`--deep-female`, ink `--deep-accent-ink`), name, `COACH` chip (accent, 9.5px uppercase), pts mono. Footer italic 10.5px ghost: `Roster is maintained by the group creator; not an official club entry.` Link `all 19 →` → Swimmers tab. **Next meet** card: name 13px/800, mono accent `10/01/2027 · in 124 days`, link `Competitions →` → Results tab, Meets section.
4. **Media** (span 2) — 5 thumbnails 16:10, radius 12, play button 24px top-right `rgba(0,0,0,.55)`, duration chip bottom-left mono 10px; caption: swimmer 12px/800, `event · date` 10.5px muted. Header note `🔒 9 from members · 1 public`. Link → Media tab.

### Mobile (<640px) — `#2a` phone artboard, 390px
- Top bar: `← Groups` left, logo centre, avatar right.
- Photo full-width 150px above the hero content; then avatar 48 + name 20px + meta 11px; `+` button.
- Training info card stacked (Coach / Days / Pool · time / Avg. attendance) + Next strip.
- **Tabs**: same `DeepTabs`; the existing `@media (max-width:639px)` rules apply (5 equal columns, icon 17px above 9.5px display label, no `sub`). Per product owner: keep the **folder tongue** on mobile too (active tab bg `--deep-panel-bg`, border `--deep-panel-border`, no bottom border, `margin-bottom:-1px`) — i.e. remove the mobile override that switches to an underline, and remove the dark strip as on desktop. Use `shortLabel` where needed (`Swimmers`, `Trainings` fit at 9.5px in 5 columns of ~70px).
- Panel padding 12; cards padding 12; records in a 2-col grid; swim rows compact (`22px 1fr auto`, name over event); media as a horizontal scroll strip of 140px cards.
- Six tabs on a 390px screen: show 5 (`Overview Results Media Swimmers Trainings`); `Admin` appears as a 6th column only for creator/coach (`--deep-tabs-count: 6`).

---

## Interactions & behaviour
- Tabs: click → `setTab`, `history.replaceState` with `?tab=`, as in `club-project.tsx`.
- `I'm going` / `Going`: toggles attendance intent for the next training (`POST /api/hub-groups/:slug/trainings/:id/rsvp`); optimistic; count `14 going` updates.
- Pool segmented control filters records client-side (`pool` in `ClubRecordCard`).
- `Show all` / `All N →` links switch tab and scroll the panel top into view (no `scrollIntoView` — use `window.scrollTo`).
- Locked content (Trainings tab, members-only media, Admin) for non-members: render the tab, show the existing lock notice inside the panel instead of hiding the tab.
- Loading: keep data on screen when switching tabs; first load shows the club-style `Notice`.
- Hover: rows `--deep-card-bg` (as `.deep-meet-row:hover`), chips `--deep-accent-hover` text.
- Focus: rely on the existing `:focus-visible` accent ring.

## State
```
tab: 'overview'|'results'|'media'|'swimmers'|'trainings'|'admin'
pool: 'all'|'25m'|'50m'
rsvp: boolean (next training)
```
Data:
- `GET /api/hub-groups/:slug` — profile, roster, records, coach. **New fields needed:** `schedule: {days:[0,2,4], start:'20:00', end:'21:30'}`, `pool: {name, length}`, `attendance_avg`, `hero_image_url`, `next_training`, `next_competition`.
- `GET /api/hub-groups/:slug/results?page…` — last start = latest competition in the result set (group by competition, take max date).
- `GET /api/hub-groups/:slug/trainings` — existing.
- Media and publication requests — existing endpoints used by the current page.

## Design tokens
All from `deep-theme.css` (`theme-deep` / `theme-deep-light`). No new colours. Used here: `--deep-page-bg, --deep-hero-grad, --deep-card-bg(-raised/-row), --deep-card-border, --deep-text(-mute/-ghost), --deep-accent(-chip/-border/-ink), --deep-gold(-chip/-border), --deep-male, --deep-female, --deep-danger, --deep-live(-border), --deep-panel-bg, --deep-panel-border, --deep-seg-track/-active-*, --deep-font-display, --deep-radius-card/tile/row/pill`.
Type: Inter 700/800 body, `--deep-font-display` (Archivo Black) for titles and big numbers, JetBrains Mono (`hp-mono`) for times, dates, counts.
Radii: card 16, tile 14, row 12, chips 6–10, pill 999.

## Assets
- Group photo: user upload (`hero_image_url`); design shows an empty slot.
- Icons: existing glyph set used by `DeepTabs` (▦ ≡ ▶ 🏊 ◷ ⚙).
- Media thumbnails: YouTube thumbnails via existing media links.

## Files
- `Group Page v2.dc.html` — design reference. Section **2a** = final. 1a/1b/1c = explorations, ignore.
- `image-slot.js`, `support.js` — runtime for the prototype only.
- Reference source in the app: `club-project/club-project.tsx`, `components/deep/tabs.tsx`, `components/deep/deep-theme.css`, `club-project/components/club-record-card.tsx`, `club-project/components/club-hero.tsx`, `mix/swim-time/swim-time.tsx`.
