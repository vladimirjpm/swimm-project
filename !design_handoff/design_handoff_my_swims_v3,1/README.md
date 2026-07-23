# Handoff: "My media" v3 — swim-centric page (media.html)

## Overview
Redesign of the **My media** page for a competitive swimming results site (dark navy theme, Hebrew/RTL data, English-only UI). The page flips from a media-gallery into a **list of the user's swims grouped by competition**, where media (video links + photos) are attached per swim or per competition. This page becomes the ONLY place to add videos — public results pages lose that ability.

Key model decision: **share / publication status / withdraw / delete live on each media item (video or photo), not on the swim row.** A swim can have 0–3 videos and 0–N photos; each has its own publication lifecycle.

## About the Design Files
The files in this bundle are **design references created in HTML** (interactive prototypes showing intended look and behavior), NOT production code. The task is to **recreate these designs in the target codebase** (`client/` — React + Vite + TS) using its established patterns: theme tokens (`var(--theme-mode-*)`), existing modal patterns, existing API layer. Open `My Swims v3.dc.html` / `My Swims v3 Mobile.dc.html` in a browser to click through every interaction.

## Fidelity
**High-fidelity.** Colors, spacing, typography and interactions are final intent — BUT hex values in the prototype must be mapped to the codebase's paired theme tokens (see "Design Tokens & theming rule" below). Recreate layouts and behavior exactly; source colors from tokens.

## Page structure (desktop ≥1280px)

1. **Site header** — unchanged (sticky, `#161a2e`, logo + nav + user avatar).
2. **Title block** — breadcrumb `MY PROFILE · <name>` (11px, 800, uppercase, letter-spacing 0.28em, accent cyan), `My media` h1 (56px/900/-0.045em), nav pills: `Media` (active), `My groups ↗` (link), `Settings · soon` (disabled, dashed border).
3. **Tabs** — `My swims` (main) and `Moderation` (only for group admins; amber count badge with pending total). Tab: JetBrains Mono 13.5px/800, radius 12, active = cyan border + `rgba(125,211,252,0.14)` fill.
4. **Swimmer chips row** — `All · N` + one chip per favorite swimmer (17px round initial avatar + name + swim count). Right-aligned green primary button **`+ Add link`**.
5. **Primary filter row** (always visible):
   - **Segmented control (hero filter): `All swims · N | With video · N | Without video · N`** — counts update live.
   - **Season select** (`SEASON` mono label + native select, default = current season `2025–26`).
   - **`More filters ▾`** button with active-count badge (cyan circle, count of non-default filters).
   - Right-aligned result summary: `N swims · sorted by date ↓`.
6. **More filters dropdown** (anchored panel 620px, radius 16, bg gradient `#0e2138 → #081527`, shadow `0 30px 70px rgba(0,0,0,0.6)`):
   - **Competition** — chip list (Hebrew RTL names, `dir="auto"`), derived from the selected season's swims only.
   - **Style** — chips: All / Free / Back / Breast / Fly / IM.
   - **Distance** — chips: All / 50m / 100m / 200m / 400m.
   - **Date range** — two `<input type="date">` (dark `color-scheme`).
   - **Publication status** — All / Private / Pending / Published / Rejected — **rendered ONLY when segment = "With video"**.
   - Footer: `Reset filters` (ghost, left) / `Done` (cyan solid, right).
7. **Main list — swim rows grouped by competition** (cards radius 16, border `rgba(125,211,252,0.22)`, gradient fill, shadow `0 18px 44px rgba(2,10,24,0.45)`):
   - **Group header**: RTL competition name (15px/900), date (mono 11px cyan), city (11.5px muted), 🏅 icon if any podium finish inside, `⚡ REC` amber chip if any record; right cluster: meta `N swims · N videos` (correct singular/plural), **`📎 N ▾` competition-media counter/toggle**, **`+ Photo/Video`** dashed green button (attach media to the competition itself).
   - **Competition media panel** (expands under header): label `COMPETITION MEDIA · not tied to a swim`, then one line per item (same media-line component as swim media, below).
   - **Column header row**: `PLACE | SWIM | TIME | DATE | MEDIA` (mono 9px/800, letter-spacing 0.14em, 45% cyan).
   - **Swim row** (flex, fixed column widths so values align vertically):
     - medal column **26px**: 🥇/🥈/🥉 for places 1–3, ⚡ (amber) below medal if record (PB/CR);
     - place column **60px**: `#4` (12.5px/800) over `512 pts` (10.5px muted) — vertical stack;
     - swimmer avatar 24px (dimmed 50% when row has no video);
     - swim label (13.5px/800; muted `rgba(226,240,252,0.55)` when no video) + `RELAY` tag (mono 9px, cyan outline) for relays;
     - time **84px** mono 13.5px/800 cyan;
     - date **92px** mono 10.5px 45%;
     - **congrats button 52px**: `🎉 N` (mono 10.5/800; own vote toggles: amber border `rgba(255,202,122,0.55)` + bg `rgba(255,202,122,0.1)` + text `#ffca7a`, else muted) — column header `🎉`;
     - **MEDIA column, fixed 330px**: compact counters only — `▶ N` chip (cyan filled outline) if videos, `🖼 N` chip (dimmer) if photos, `+ Add video` dashed green button when 0 videos, chevron `▾/▴` at right edge when any media. Rows without video get bg `rgba(2,10,24,0.25)`.
   - **Expanded media panel** (details-style div under the row; toggled by counter chips or chevron): bg `rgba(2,10,24,0.4)`, padding-left 116px, one **media line** per item (videos first, then photos):
     - source chip 120px: `▶ YOUTUBE` / `▶ VIMEO` / `▶ OTHER` / `🖼 PHOTO` — **click = open lightbox/viewer (no separate Play button)**;
     - status pill 120px slot: `private`(gray) / `pending`(amber) / `published`(green) / `published 🌐`(green, 1.5px border when public) / `rejected`(red);
     - **like button**: `❤ N` per media item (toggle; liked = pink `#ff7d9c`, border `rgba(255,125,156,0.55)`, bg `rgba(255,125,156,0.1)`; else muted). Likes live on the media item; congrats live on the swim (result);
     - right cluster — **inline share, no popup**: `Group…` select + `Members / Public 🌐` select + `Share` button (disabled/dim until group chosen; on submit adds a `pending` publication immediately), then `Withdraw` (amber, only if item has publications) and `Delete` (red).
     - Footer: `+ Add media` dashed button (same add-video modal, swim pre-selected).
8. **Unlinked media section** (bottom, collapsed by default): header button `Unlinked media [N] · club videos and general footage not tied to any swim · ▼ expand`. Expanded: card per item (76px 16:9 thumb placeholder, swimmer avatar, label, source + added date, status pill, `Link to a swim` outline button, `Share`, `✕` delete) + `+ Add link without a swim` dashed button.
9. **Moderation tab** — unchanged from v2: group chips + Pending/Published/All segmented, decision rows (thumb, swimmer + result, owner email, RTL group, Members/`Public 🌐` level pill, age, Publish green / Reject red / Unpublish outline). Public+pending rows get amber border. Footer note: "Public = visible to everyone on the internet…".

## Modals / overlays

- **Add video (swim pre-selected)** — 480px modal: context card (swimmer initial, `50m breast · 0:39.12`, RTL competition + date), `PASTE A LINK` input, `detected · YOUTUBE/VIMEO/PHOTO/OTHER` green chip, Cancel / Save (green, disabled until URL). Also serves **competition media** (kind `addComp`): context card shows `Competition media` + 📎 + competition name.
- **Add link (global, 3 steps)** — step 1 pick swimmer (row buttons with avatar + hint), step 2 attach to swim (competition chips → swim list with time; `Skip — save as a general link` underlined), step 3 paste URL. Footer: `← Back` + `Next → / Save` (green, gated per step).
- **Share with a group (modal)** — still used from the Unlinked section on desktop and everywhere on mobile: group row-select, `Members | Public 🌐` segmented, amber warning `Public = visible to everyone on the internet after approval.`, `Submit for approval` (green, disabled until group picked). In-row DDL share (above) replaces this inside expanded panels on desktop.
- **Lightbox** — dimmed blurred overlay, 860px 16:9 embed placeholder (production: youtube-nocookie / vimeo player only, never raw URL iframes), title line, per-video tabs (`▶ 1 · YOUTUBE`) when a swim has several videos.

## Mobile (375px, separate file)

- Max-width 375 column; compact header; 34px h1.
- Swimmer chips scroll horizontally (`overflow-x:auto`, hidden scrollbar).
- Filters collapse: segmented `All | With video | No video` + **`Filters (n)`** button → **bottom sheet** (drag handle, radius 20 top, sections: Season / Competition / Style / Distance / Date range / Status(with-video only), `Reset`, sticky green **`Show N swims`** confirm with correct plural).
- Swim rows single-column: medal+⚡ mini-column 20px, label + RELAY, time + place/pts + tappable `🎉 N` chip (stopPropagation — doesn't open the sheet); right side: 🖼 icon + `▶ N` chip + one status pill per video (stacked).
- Actions-sheet media selector chips include likes: `▶ 1 · YOUTUBE · pending · ❤ 12`.
- **Row tap → actions bottom sheet**: title (swim + time), RTL competition subtitle, media selector chips when >1 item (`▶ 1 · YOUTUBE · pending`, `🖼 3 · PHOTO · private`), then `▶ Play`, `Share with a group`, `Withdraw from group` (when published/pending), `Delete video` — acting on the selected item.
- Floating green pill `+ Add link` fixed bottom-center.
- All hit targets ≥44px (min-height on buttons/inputs).
- NOTE: competition-level media (📎 / + Photo/Video) is **not yet designed on mobile** — follow the desktop pattern inside the group header + a bottom sheet listing if needed.

## Empty / loading states (both files, `pageState` prop demonstrates them)
- `loading` — skeleton group cards, shimmer bars (1.6s linear loop).
- `no-favorites` — ⭐ card "No favorite swimmers yet" + copy + green `Find swimmers →`; swimmer chips hidden.
- `empty-season` (also auto when a swimmer has no swims in season) — dashed card "No results in season 2025–26" + `Season 2024–25 →` outline button.
- filters-empty — dashed card "Nothing matches the filters" + `Clear all` (resets segment, swimmer, all more-filters).

## State Management
- `tab` (media|mod), `swimmer`, `seg` (all|with|without), `season`, `more{comp,style,dist,from,to,status}`, `moreOpen`.
- `expanded{swimId}`, `compExpanded{compId}` — details panels.
- `inlineShare{mediaKey:{group,level}}` — in-row share DDL state; cleared on submit.
- Per-media mutations keyed by **media key** (`swimId:index`, `swimId:pN` for photos, `compId:mN`, unlinked id): `deletedVid`, `withdrawn`, `addedPubs`, `extraVideos`, `extraCompMedia`.
- Aggregated media status = pending > published > rejected; empty pubs = private.
- `liked{mediaKey}`, `cheered{swimId}` — current user's like/congrat toggles (optimistic +1).
- Mobile adds: `sheetOpen`, `actionsFor` + `actionsVid` (selected media in actions sheet).

## Data model (existing + needed)
Media item: `{id, url, media_type: image|video, source_type: youtube|vimeo|other, swimmer_id, level: swimmer|competition|result, result_id?, competition_id?, created_at}`; publications `0..N {hub_group_name, level: members|public, status: pending|approved|rejected}`.

Server work needed (from v2 brief, still valid + additions):
1. `GET /api/me/media` + denormalized `competition_name, competition_date, season, club_name`; client-side filtering is fine (small volumes).
2. Swims list for this page: user's favorite swimmers' results per season with per-swim media (video/photo arrays incl. statuses) — new aggregate or client-side join.
3. `GET /api/swimmers/{id}/results-brief?competitionId=` — swim picker (distance, style, time, date, place, result_id).
4. **`level=competition` attach** — create/list media with `competition_id` and no `result_id` (the `📎 + Photo/Video` flow).
5. Consolidated moderation inbox `GET /api/me/moderation/media`.
6. Relay swims must be included in results (flagged `relay`).
7. **Reactions**: like count + my-like flag per media item (`POST/DELETE /api/media/{id}/like`), congrats count + my flag per result (`POST/DELETE /api/results/{id}/cheer`) — counts denormalized into the lists above.

## Design Tokens & theming rule
Prototype values (dark theme):
- Background gradient `#0d2036 → #0b1b31 → #050e1c` (160deg); page text `#f3f8fd`; muted text = `rgba(203,224,240, .4–.7)`.
- Accent cyan `#7dd3fc`; action green `#38ef8f` (text on green `#04101f`); warning amber `#ffca7a` (text `#3a2a08`); danger `#ef5350`; private gray `#94a3b8`; like pink `#ff7d9c`.
- Cards: radius 14–18, border `rgba(125,211,252,0.22)`, fill `linear-gradient(180deg, rgba(56,189,248,0.08), rgba(8,25,48,0.78))`.
- Status pills: mono 10.5px/800, radius 6, `color` + border `rgba(<color>,0.45)` + bg `rgba(<color>,0.08)`.
- Fonts: **Inter** (body) + **JetBrains Mono** (chips, buttons, numbers, labels). Section labels: 9–12px, 800, uppercase, letter-spacing 0.14–0.28em.
- Spacing: page padding 64px desktop / 16px mobile; card padding 20px; row padding 10px 20px; gaps 6/8/10/14.
- **MANDATORY paired-token rule from the codebase**: never hardcode text color on theme-driven surfaces; every surface has `--…-bg`/`--…-text` pairs overridden together in dark theme. Icons/SVG: `currentColor` only. Accent/link color from `--theme-mode-accent` (light cyan in dark theme — never light-theme `#1d4ed8`). Verify both themes, contrast ≥4.5:1.

## Layout notes
- The grouped list sits in an `overflow-x:auto` wrapper with `min-width:1100px` on the list — below ~1100px the list scrolls horizontally instead of collapsing the flexible SWIM column. Do NOT put `overflow-x:auto` on the page root (it breaks the sticky header's scroll context).
- Swim label cell: `min-width:120px; overflow:hidden`.

## RTL notes
Competition/group names are Hebrew: use `dir="rtl"` (or `dir="auto"`) on those spans with `text-align:left` inside LTR rows; ellipsize with `overflow:hidden;text-overflow:ellipsis;white-space:nowrap`.

## Files
- `My Swims v3.dc.html` — desktop prototype (all flows incl. competition media, inline share, moderation).
- `My Swims v3 Mobile.dc.html` — 375px prototype (bottom sheets, actions sheet, floating add).
- `support.js` — prototype runtime only; ignore for implementation.
- `../design_handoff_media_page/media-page-design-brief.md` — v2 brief with roles, data contracts and server checklist (still the source of truth for §2/§6).
