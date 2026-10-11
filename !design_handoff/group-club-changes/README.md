# Handoff: Entity page (group / club): mobile hero, tabs by role, next-training RSVP, desktop

## Overview
Rework of the shared entity page shell used by `/groups/{slug}` and `/clubs/{id}` (`components/deep/entity-page.tsx`, `hero-band.tsx`, `tabs.tsx`, `group-hero.tsx`, `club-hero.tsx`, `group-training-slots.tsx`, `group-page.tsx`).
Problems it fixes (from production screenshots):
- Mobile hero took ~780px before the tabs: flag+city circle on its own row, 4 schedule chips, 2 big buttons, 3 tall KPIs.
- Hebrew names were cut **from the start** (`truncate` + RTL → "…פין נתניה מסטרס").
- 8 tabs in 390px (~45px columns).
- No way to answer "am I coming to the next training".
- On desktop the action buttons took ~330px of the name row.

**Final variants in `Entity Hero Mobile.dc.html`: mobile `2a`, desktop `4a`** (with 5c applied). Everything else in the file is exploration history. Only `3b` is reused: it goes into the Trainings tab.
See also `DECISIONS.md` (short list of decisions) and `HERO-PHOTO.md` (photo sizes + admin slot).

## About the design files
The `.dc.html` file is an **HTML design reference**. It is not production code. Recreate it in the existing React + Tailwind codebase with the `theme-deep` / `theme-deep-light` tokens (`deep-theme.css`) and the shared `deep/*` components. Do not copy the inline styles. The mock hard-codes light-theme values, so map every value to its token (table below). All changes go into **shared** components so club, group and (later) swimmer stay on one implementation (hard rule from the previous handoff, `design_handoff_group_page/README.md`).

## Fidelity
High-fidelity for layout, sizes, copy and behavior. Colors must come from the tokens, not from hex values.

---

## 1. Hero: mobile (<960px), variant 2a

Order inside `DeepHeroBand` on mobile, top to bottom:

1. **[Optional] RSVP banner**: see §4, mode "top".
2. **Photo**: full width, no side padding.
   - A dedicated mobile photo exists: height 292px (4:3 at 390px).
   - Otherwise the desktop photo is shown as a strip: height **180px**, `object-fit: cover`, `object-position: center`.
   - No photo at all: no block. There is no placeholder for visitors.
   - Details in `HERO-PHOTO.md`.
3. **Identity row** (`display:flex; align-items:flex-start; gap:14px`, hero padding `14px 16px`):
   - **Avatar / logo 72×72**, radius 18, `--deep-accent-grad`, letter `--deep-font-display` 32px (group) or `UI_ClubLogo` size 72 (club).
   - **Name `h1`**: `dir="rtl"`, `text-align:left`, `--deep-font-display` 22px / 1.2, `overflow-wrap:anywhere`.
     - **No `truncate`.** Wrap up to 2 lines (`-webkit-line-clamp:2` as a safety net).
     - Same fix on desktop (34px group / 40px club).
   - **Sub-line**: 12px / 700 `--deep-text-mute`: `{name_en} · {flag} {city}`. Wrap city in `<bdi>`. This replaces the separate flag/city badge.
   - **Membership button** (8px below the sub-line), height 32, radius 10, border `--deep-card-border`, bg `--deep-card-bg`, `hp-mono` 12px / 800 `--deep-text-mute`.
     - Content: `✓ Member` + an inner 22×22 radius 6 box with `⋯` (bg `--deep-divider`).
     - **The whole button** opens the menu. The menu is a popover under the button (`top:38px; left:0`, width 170, radius 12, `--deep-card-shadow`) with one item, `Leave group` (danger tint bg, `--deep-danger` text).
     - Guest: `+ Join group` instead (accent chip style); click → login modal.
     - Club: the same slot holds `✓ Following` / `+ Follow club` (`FollowClubButton`), with ⋯ → Unfollow.
4. **Training block**: see §4.
5. **KPI row**: `grid-template-columns: repeat(3, minmax(0,1fr)); gap:6px`. Each tile has padding `10px 10px 9px`: value `--deep-font-display` 24px, label 9.5px / 800 uppercase `--deep-text-mute`, hint 9.5px / 700 `--deep-text-ghost`.
   - Group: `Swimmers` (roster count), `Records` (bests count), **`Competitions →`**.
     - The Competitions tile is a link to `routes.groupResults(slug)`. It replaces the old "Competitions →" button.
     - Tile style: radius 12, border `--deep-accent-border`, bg `--deep-accent-chip`, all text `--deep-accent`, hint `all meets`.
     - Value = number of competitions in the group's results (new field `competitions_count`).
   - Hide any KPI whose value is 0 (e.g. "Gold 0").
   - Club: 5 KPIs in one bordered strip `repeat(5,1fr)` with short labels (`Champs · Wins · Records · SB 25/26 · Swimmers`). Hints move to `title`. See variant `1g` in the file.

## 2. Hero: desktop (≥960px), variant 4a + 5c
- `DeepHeroBand` grid `minmax(0,1fr) 380px`, gap 24, padding 24. The photo on the right stretches to the hero height (min 300).
- Identity row: avatar 80×80 radius 18, then name 34px (2 lines max, same RTL fix).
- Under the name, one flex row (`gap:12px; wrap`): the sub-line text, then the membership button (height 32, same as mobile; on desktop ⋯ may stay a separate 32×32 button, as in 4a).
- **Nothing sits to the right of the name.** The action cluster is removed.
- Training block (§4), laid out wide: `grid-template-columns: minmax(0,1fr) auto`. Left: NEXT info. Right: 3 buttons (member) or avatars + `Who's coming →` (coach). Full-width bar + counts below.
- KPI row: `gap:32px`, value 34px (existing `DeepKpi`), then the **Competitions tile** (same accent chip style, `padding:8px 12px; margin:-8px -12px`, radius 12).

## 3. Tabs

### Set (group)
`Overview (Home) · Season · Results · Swimmers (Team) · Media · Trainings (Train) · Admin`

- **Results** = one tab with chips inside, exactly like the swimmer page (`ResultsFilters` / `.deep-filter-chip`, `swimmer-panels.tsx`):
  - `≡ Results`: meets & swims, newest first;
  - `🏅 Records [20]`: best per event, both pools;
  - `☀ Season bests`: best per event this season.
  
  Chips wrap and never scroll. The old separate Records tab is removed.
- **Lanes is no longer a tab.** It lives inside Trainings as a segmented control `Sessions | Lanes`.
- Club uses the same vocabulary: `Overview · Season · Results · Swimmers · Media · History · Admin`.

### Roles
| Role | Top tabs | Extra |
| --- | --- | --- |
| Guest | Home · Season · Results · Team · Media · Train 🔒 | Trainings shows the lock notice (tab visible, content locked) |
| Member | Home · Season · Results · Team · Media · Train | Trainings open |
| Admin / coach | Home · Season · Results · Team ✎ · Media ✎ | **Trainings + Admin pinned** (below) |

- **Purple = "you can edit"**: tokens `--deep-ow`, `--deep-ow-chip`, `--deep-ow-border` (already in the theme).
  - Only users with edit rights see purple, and always together with a `✎` mark or a text label, never color alone.
  - Editable top tabs (Team: levels; Media: gallery, publication requests) have `✎` at top-right 9px; inactive text `--deep-ow`; active tongue border `--deep-ow-border`.
  - Panel header of an editable view shows a chip `✎ YOU CAN EDIT` (height 22, pill, ow tokens, 10px / 800 uppercase).
- **Mobile, admin/coach**: `Trainings` and `Admin` are a bar **pinned to the bottom of the viewport**:
  - bg `rgba(255,255,255,.96)`, top border `--deep-ow-border`, `box-shadow 0 -6px 20px rgba(76,29,149,.10)`, padding `8px 10px 14px` (+ safe-area-inset-bottom);
  - label `COACH TOOLS` 9px / 800 uppercase `--deep-ow`;
  - 2 equal buttons height 46, radius 12: inactive bg `--deep-ow-chip`, text `--deep-ow`; active bg `--deep-ow`, text white. Admin shows `· N new` (pending publication requests);
  - Page content gets `padding-bottom: 96px` so nothing hides under the bar.
- **Desktop, admin/coach**: no bottom bar. Trainings + Admin sit at the **end of the tab row**, separated by `margin-left:10px; padding-left:12px; border-left:2px dashed --deep-ow-border`, with the label `COACH TOOLS` above them, purple folder tongues.
- **More**: only if a role ends up with **>6** top tabs. Then 5 are visible and the rest go into a bottom sheet (variant `1e`). The current sets need no More.

### Mobile tab strip
- 6 or 5 equal columns (`--deep-tabs-count`), icon 17px above a 10px / 800 label.
- Keep the folder tongue (active tab: bg `--deep-panel-bg`, border `--deep-panel-border`, no bottom border, `margin-bottom:-1px`).
- If a strip ever scrolls, hide the scrollbar (`scrollbar-width:none` + `::-webkit-scrollbar{display:none}`).

## 4. Next training / RSVP block (group only)

Server already provides `training_schedule` and `next_training`. **New:** RSVP per session.

### Guest (not logged in): no NEXT
Card: border `--deep-card-border`, radius 12, padding `10px 12px`.
- Label `TRAININGS` 9.5px / 800 uppercase `--deep-text-mute`.
- Days with hours, mono 12px: `Tue 20:00–21:00 · Sat 10:00–12:00 · Sun 20:00–21:00` (day bold `--deep-text`).
- `📍 {place} · {pool_type} pool` (place in `<bdi>`).

### Member
Card: border `--deep-live-border`, radius 12, padding `10px 12px`, gap 10.
- Row: green dot 8px `--deep-live` (ring `0 0 0 3px` of live at 14%), then `NEXT` 9.5px uppercase `--deep-live`, `Tue 29 Sep · 20:00` mono 13px / 800, and `tomorrow` / `today` 11.5px mute.
- Second line mono 11px mute: `Tue · Sat · Sun · {place} · 25m`.
- **Bar** 6px radius 3, track `--deep-divider`. Segments: going `--deep-live`, not sure `--deep-gold-bar`, not going `--deep-danger` at 75%. Each width = count / roster. The grey remainder = no answer.
- **Counts** mono 11.5px / 800 with 7px dots: `13 going` (live) · `2 not sure` (gold) · `3 not going` (danger). No "no answer" label.
- **3 buttons**, `grid repeat(3,1fr); gap:6px`, height 40 (desktop 38), radius 10, 12.5px / 800:
  - `✓ Going`: idle border live-border, text live. Selected: fill `--deep-live`, white text.
  - `? Not sure`: idle border gold-border, text `--deep-gold`. Selected: fill `--deep-gold-bar`, white text.
  - `✕ Can't come`: idle border danger 30%, text danger. Selected: fill danger, white text.
  - The selected button **stays pressed**. Tapping it again clears the answer. Counts update optimistically.

### Admin / coach
- Same NEXT row, bar and counts. No own buttons.
- Instead, a row under a hairline: avatar stack (24px, `margin-right:-6px`, 2px white ring, male/female token bg), `+N`, and `Who's coming →` (ow chip).
- **Who's coming** opens the Trainings tab → Sessions → next session:
  - groups Going / Not sure / Can't come / **No answer**, with notes;
  - `Remind N` for non-responders;
  - coach can change a member's answer;
  - after the session the same list becomes the attendance check.

### Mode "top" (option, keep behind a flag)
- If the viewer is a **member with no answer**, the RSVP card moves **above the photo** (right under the top bar) as a full-width card: inset top line 3px `--deep-live`, label `NEXT TRAINING · TOMORROW`, date mono 15px, `📍 place`, `Are you coming?`, and 3 buttons, height 42.
- The NEXT block is hidden from the hero while the banner shows.
- After answering, the banner disappears and the normal block (with the pressed button) returns to the hero.
- Never shown to guests or coaches.
- Desktop: one full-width strip above the hero (inset left line 4px live, buttons on the right).

## 4b. Club page (same shell, read-only), variants 6a / 6b / 7a
- The club page has **no admin or coach roles**: no Admin tab, no pinned coach bar, no ✎, no purple. Coaches, trainings, RSVP and management live in the club's **official group**.
- No training / RSVP block. The season strip (44px, carousel + All) stays between the hero and the tabs.
- Tabs: `Overview · Season · Results · Swimmers · Media · History` (Results with the same chips).
- KPIs: 5 + a link tile `28 Competitions →` (opens History). Mobile grid is 3×2.
- No club photo → mobile: no photo block; desktop: the left column goes full width. No placeholder on the page; the club photo is set by the site admin in the back office.
- **Mobile hero (7a)**:
  - The logo column is 84px wide: logo 72px, and under it a `✓ Following` button (height 26, radius 8, mono 10.5px / 800, accent chip) → menu with `Unfollow`. Guest sees `+ Follow` (filled accent) → login.
  - Right column: name (2 lines, RTL fix), sub-line `{name_en} · {flag} {country} · since {year}`, then an **"Official group →"** text link (12px / 800 accent, 18px mini group avatar, `title` = group name).
- **Official group link is rendered ONLY if the club has an official group** (`club.official_group_slug` is set). Otherwise render nothing: no link, no placeholder, no gap.
- Desktop (6b): the same layout. The logo column is 112px wide: logo 96px, and under it a `✓ Following` button (height 30, radius 9, mono 12px). Next to it: name 40px, sub-line 13px, then the "Official group →" text link (13px, 20px mini avatar), with the same conditional rule.

## 5. Sticky header
- Mobile: after `scrollTop > 300`, show a 48px bar under the app top bar.
- Desktop: after `> 200`, show a 52px bar.
- Bar: bg page color at 97%, bottom border, `0 4px 14px rgba(8,60,84,.08)`.
- Content: avatar 28 (desktop 32), name one line (`dir="rtl"`, ellipsis; RTL puts the ellipsis at the logical end), desktop also `name_en`. Right side, a status chip, height 28 / 30, mono 11–12px / 800:
  - member answered: filled in the answer color: `✓ Going · Tue 20:00` / `? Not sure · Tue` / `✕ Can't · Tue`;
  - member not answered: outlined live `Tue 20:00 · going?`;
  - coach: ow chip `13 ✓ · 2 ? · 3 ✕`;
  - guest: `Tue · Sat · Sun`.
- The tab row is `position: sticky` directly under the bar. Tapping the bar scrolls to top (`window.scrollTo`, never `scrollIntoView`).

## 6. Trainings tab: lane view (variant 3b)
Inside Trainings → Sessions, for the next session:
- Pool block: radius 14, border `#b9dcea`-ish (map to a new `--deep-pool-*` pair if needed), gradient water.
- Lanes 40px high, separated by dashed "ropes"; lane label mono 9.5px (`1 · fast`).
- Swimmers as 26px circles: solid = going, dashed gold border = not sure. The viewer's circle has a live ring.
- Counts row `N in the water · N maybe · N out`.
- A 3-way segmented control with a sliding colored thumb (`transition: left .2s ease`).
- After answering Going / Not sure: quick-note chips `Late ~10 min · 1st hour only · Leaving early` (one at a time, sent with the RSVP).
- Overflow: more than ~5 per lane → `+N` circle.

## Interactions & state
```
tab: 'overview'|'season'|'results'|'swimmers'|'media'|'trainings'|'admin'   // ?tab=, as today
resultsView: 'results'|'records'|'season-best'                               // ?view= like swimmer page
trainingsView: 'sessions'|'lanes'
rsvp: null|'yes'|'maybe'|'no'          // viewer, next session
rsvpNote: null|'late'|'first-hour'|'leaving-early'
memberMenuOpen: boolean
stuck: boolean                         // scroll threshold
```
API (new):
- `GET /api/hub-groups/:slug`: add `competitions_count`, `next_training.id`, `next_training.rsvp: {yes, maybe, no, mine, going_preview:[{id,initial,gender}]}`, `hero_image_mobile_url`, `hero_mobile_media_id`.
- `POST /api/hub-groups/:slug/trainings/:id/rsvp` `{answer: 'yes'|'maybe'|'no'|null, note?}`: members only. Optimistic.
- `GET /api/hub-groups/:slug/trainings/:id/rsvp`: full list (coach/admin; members if the group allows).
- `POST …/rsvp/remind`: coach/admin.
- Optional later: answer cut-off N hours before start; attendance lock after the coach marks attendance.

## Design tokens (all existing, `deep-theme.css`)
Surfaces `--deep-page-bg, --deep-hero-grad, --deep-card-bg(-raised), --deep-card-border, --deep-divider`. Text `--deep-text, -mute, -faint, -ghost`. Accent `--deep-accent, -chip, -border, -grad`. Edit role `--deep-ow, -chip, -border`. Live `--deep-live, --deep-live-border`. Gold `--deep-gold, -bar, -border`. Danger `--deep-danger(-soft)`. Gender `--deep-male, --deep-female`. Panels `--deep-panel-bg, --deep-panel-border`.
Type: Archivo Black (`--deep-font-display`) for names, KPIs and titles; Inter 700/800 for UI; JetBrains Mono (`hp-mono`) for times, dates, counts and buttons.
Radii: tiles/cards 12–16, buttons 10, chips 9, pills 999.
Light-theme hex values used in the mock: page `#f4f9fc`, text `#04283e`, mute `#7b95a3`, accent `#0e7490`, ow `#7c3aed`, live `#0f7a52`, gold `#b17d11` / bar `#d9a420`, danger `#b23b3b`.

## Assets
- `assets/group-photo.png`: sample group photo (cropped from a screenshot). Placeholder only; real photos come from `hero_image_url`.
- `assets/club-logo.png`: sample club logo. Production uses `UI_ClubLogo`.
- Icons: the existing glyph set (▦ 🗓 ⏱ 🏊 ▶ ◷ ⚙ ✎).

## Files
- `Entity Hero Mobile.dc.html`: the design. Open it in a browser and use the role switcher in `2a` / `4a`.
  - **Final:** `2a` (group mobile), `4a` (group desktop), `5c` (desktop button decision, already applied in 4a), `3b` (→ Trainings tab), `6a` + `7a` (club mobile), `6b` (club desktop).
  - **History, ignore:** 1a–1i, 3a, 5a, 5b, 7b, 7c.
- `DECISIONS.md`: one-page decision log.
- `HERO-PHOTO.md`: photo sizes, fallback rules, admin slot.
- `support.js`: runtime needed to open the `.dc.html` file.
