# Handoff: My Media page (media.html)

## Overview
User-profile page for managing personal media links (videos + photos) across the user's swimmers: filtering, linking to swims, sharing with hub-groups, tracking publication status — plus a Moderation tab for group admins (approve/reject publication requests). Based on `docs/design_handoff/media-page-design-brief.md` (v2) in the swimm-project repo.

## About the Design Files
The files in this bundle are **design references created in HTML** (Design Component prototypes showing intended look and behavior) — NOT production code. The task is to **recreate these designs in the swimm-project client** (React + TypeScript + Tailwind v4 + Vite, page pattern: `client/media.html` + `client/src/pages/media-page.tsx` + `client/src/projects/user-media-project/`), reusing existing components: `AppTopbar`, `UI_SwimmerGallery` (lightbox), `HelperMedia.resolveThumbUrl`, `RecordTicker` (optional).

- `My Media Page.dc.html` — desktop, all states interactive
- `My Media Mobile.dc.html` — mobile (390px), bottom sheets
- `media-page-design-brief.md` — original product brief (data contracts, server work §6)

## Fidelity
**High-fidelity.** Visual language is lifted from the real `groups.html` code (`hub-groups-project/groups.tsx`, `home.css`, `app-topbar.tsx`) — recreate pixel-perfectly with Tailwind arbitrary values, same as the groups page does. All UI copy is English (hard project rule); data (names, groups, competitions) may be Hebrew — RTL handled per-element.

## Page structure

```
AppTopbar (existing component, active=none)
└ Profile header
  ├ eyebrow: "MY PROFILE · {userName}"
  ├ h1 "My media" + profile section chips: [Media·active] [My groups ↗ → groups.html] [Settings · soon, disabled]
  └ subtitle line
└ Tab row: [My media · N] [Moderation (badge: pending count)]  ← Moderation tab rendered ONLY if user admins ≥1 group (or site admin)
└ Tab panel (one of):
  ├ MY MEDIA: pending banner (admins only) → swimmer chips + Add link → status segmented control + More filters + sort label → card grid → empty states
  └ MODERATION: group chips + Pending/Published/All segments → decision rows → empty state
Add link modal (desktop) / bottom sheet (mobile), 3 steps
```

## Screens / Views

### 1. My media tab (desktop)
- Page bg: `linear-gradient(160deg,#0d2036 0%,#0b1b31 45%,#050e1c 100%)` — in real code reuse `.home-page` bg + `.hp-shimmer` overlay from `home.css`.
- Content padding: 64px horizontal (lg), sections stack with 16px gaps.
- **Pending banner** (only if isAdmin && pending>0): amber card — border `rgba(255,202,122,0.4)`, bg `linear-gradient(180deg,rgba(255,202,122,0.1),rgba(8,25,48,0.6))`, radius 14px, padding 12/16. Contents: count badge (bg #ffca7a, text #3a2a08), text "requests are waiting for your approval" (#ffe3b8, 13.5px/700), right-aligned `Review →` button (bg #ffca7a) → switches to Moderation tab.
- **Swimmer chips row**: `All · N`, then one chip per swimmer with 17px initials avatar. Chip (JetBrains Mono, 12px/800, radius 8, padding 7×13, nowrap): inactive = border `rgba(125,211,252,0.4)`, text #7dd3fc, transparent bg; active = bg #7dd3fc, text #04101f. Right-aligned primary button **+ Add link**: bg #38ef8f, text #04101f, radius 10, padding 9×18, mono 13px/800.
- **Status segmented control**: All·N / Private / Pending / Published / Rejected. Container: radius 10, border `rgba(125,211,252,0.35)`, segments divided by `rgba(125,211,252,0.25)`; active segment bg #7dd3fc text #04101f; inactive text `rgba(125,211,252,0.75)`; mono 11.5px/800, padding 7×12. Beside it: `More filters ▾` ghost button (Season / Competition / Club / Date range live behind it — dropdowns, same chip styling). Right: `sorted by swim date ↓` label 11.5px `rgba(203,224,240,0.5)`.
- **Card grid**: `grid-template-columns:repeat(auto-fill,minmax(250px,1fr))`, gap 14.

#### Media card
- Radius 16, border `rgba(125,211,252,0.22)`, bg `linear-gradient(180deg,rgba(56,189,248,0.08),rgba(8,25,48,0.78))` (= `.hp-card-std`), shadow `0 24px 60px rgba(2,10,24,0.5)`.
- Thumb: 16:9. Real thumbnails via `HelperMedia.resolveThumbUrl` (youtube/vimeo); photos: the image itself; `other`: placeholder gradient `linear-gradient(140deg,#12314f,#0a1c33)`. Center play/photo button 44px circle (border `rgba(125,211,252,0.4)`, bg `rgba(2,10,24,0.65)`, glyph ▶ or 🖼 #7dd3fc). Top-left source tag: YOUTUBE / VIMEO / PHOTO / OTHER — mono 9.5px/800, bg `rgba(2,10,24,0.7)`. Click thumb → lightbox (`UI_SwimmerGallery`, canonical embed only — youtube-nocookie/vimeo player, never raw URL in iframe).
- Body (padding 12×14, gap 9): swimmer avatar 24px (#2c3d52 bg, #bfe0f5 initials 9.5px/900) + either:
  - linked: title `50m breast · Feb 16, 2026` 13px/800 #f3f8fd; below, competition name `dir="rtl"` 11.5px `rgba(203,224,240,0.55)`, ellipsized;
  - not linked: italic "Not linked to a swim" 12.5px `rgba(203,224,240,0.5)` + link-button "Link to a swim →" 11.5px/800 #7dd3fc (opens the same swim picker as Add link step 3).
- Publication chips (wrap, gap 5): one per publication `{group} · {status}` (+ 🌐 suffix when level=public; public chips get 1.5px border). No publications → single `private` chip. Chip: mono 10.5px/800, radius 6, padding 2.5×8. Colors below.
- Footer (dashed top border `rgba(125,211,252,0.18)`): "Share with a group" text-button #7dd3fc 11.5px/800 (disabled + tooltip "The swimmer must be in the group's roster" if no eligible groups) | `⋯` overflow (Delete with confirm, Withdraw per publication).

#### Empty states
- No media at all: centered card (hp-card-std, padding 56×40): 🎬 40px, "No media yet" 17px/900, hint "Add a YouTube or Vimeo link — or add videos straight from a swim row on the results page." 13px, + Add link button.
- Filters matched nothing: dashed-border box "Nothing matches the filters" + `Clear all` ghost button.

### 2. Moderation tab (desktop)
- **Group chips**: `All my groups · N(pending)` + chip per managed group (Hebrew names, `dir` auto) with pending counts. Right: Pending·N / Published / All segments + Level dropdown (Members/Public).
- **Decision rows** (flex, gap 14, padding 10×14, radius 14, hp-card-std bg): thumb 88px 16:9 (click → lightbox — decisions must not be made without watching) | swimmer name 13.5px/800 `dir="auto"` + result_label 11.5px | owner email 11.5px | group name `dir="rtl"` | level chip (`Members` gray / `Public 🌐` amber **with highlighted amber row border** `rgba(255,202,122,0.45)` — public = whole internet) | date mono 10.5px | actions: pending → **Publish** (bg #38ef8f) + **Reject** (ghost, border/text #ef5350); published → **Unpublish** (ghost #7dd3fc).
- On decide: row animates out of the Pending view (prototype removes instantly; add ~200ms collapse/fade).
- Empty: "No pending requests 🎉" in dashed green box.
- Footnote: "Public = visible to everyone on the internet. Click a thumbnail to watch before deciding."

### 3. Add link flow — modal (desktop 540px) / bottom sheet (mobile)
Header: "Add link" + 3 progress dots (active dot 18×7 #7dd3fc pill) + `step n / 3` + ✕.
1. **Paste a link**: mono input (radius 10, border `rgba(125,211,252,0.35)`, bg `rgba(2,10,24,0.5)`). Auto-detect on input: youtube/youtu.be → YOUTUBE; vimeo → VIMEO; image URL (`.jpe?g|.png|.webp|.gif|.heic`, imgur, googleusercontent, photos.app) → PHOTO; else OTHER + Video/Photo radio toggle. Show green `detected · TYPE` chip + 16:9 preview (real thumb for youtube/vimeo, the image for photos). Next disabled until URL non-empty.
2. **Whose swim is it?** — radio rows (48px min): avatar + name + hint (primary swimmer / your own results). Swimmer list = primary + favorites + swimmers who already have my media.
3. **Link to a swim · optional**: competition chips (RTL names + date) → swim rows (`{distance style}` left, mono time right; server: `GET /api/swimmers/{id}/results-brief?competitionId=`). Footer: `Skip — save as general` (ghost) | `Save` (green, enabled after a swim is picked).
Save → item prepends to grid with `private` chip; filters reset so the user sees it.

### 4. Mobile (390px, see My Media Mobile.dc.html)
- Header compact: h1 32px, profile chips in horizontal scroll row.
- Tabs: two equal-width 44px pills.
- Controls: `⚙ Filters` button (badge = active filter count) + `+ Add link` (both min-height 44).
- Filters = bottom sheet: grab handle, Swimmer chip group, Status chip group (chips 44px tall, wrap), "Clear all", full-width CTA `Show N items` (bg #7dd3fc). Season/Competition/Club appear in this sheet when implemented.
- Grid: 2 columns, gap 10. Card shows only first publication chip + `+N` counter; card actions collapse into `⋯` → action sheet: Open / Share with a group / Link to a swim (if unlinked) / Delete (red) / Cancel — rows 48px.
- Moderation: stacked cards (thumb 96px + stacked fields, meta row, then full-width 44px Publish/Reject buttons).
- All sheets: radius 20 top, bg `linear-gradient(180deg,#0e2138,#081527)`, overlay `rgba(2,10,24,0.72)` + blur.

## Interactions & Behavior
- Tabs switch panels; Moderation tab exists only for group admins / site admin. Badge = pending count across all managed groups (site admin: all groups).
- All filtering client-side (volumes are small; server returns denormalized fields — brief §6.1).
- Swimmer chips, status segments, group chips: single-select, instant apply.
- Publish/Reject/Unpublish: optimistic update, row leaves the filtered list (animate ~200ms). Withdraw (×) on own publication chips with confirm.
- Delete media: confirm dialog.
- Lightbox for every thumb click; keyboard: Esc closes modal/sheets; overlay click closes.
- Empty/loading: skeleton cards on load (reuse groups.html "Loading…" pattern or shimmer cards).
- Prototype tweak props: `isGroupAdmin` (hides Moderation tab + banner), `emptyState`, `userName`.

## State Management
- `tab: 'media' | 'moderation'`
- media filters: `swimmerId | 'all'`, `status: all|private|pending|published|rejected` (+ later season/competition/club/dateRange)
- moderation filters: `groupId | 'all'`, `modStatus: pending|published|all`, `level`
- `addFlow: null | {step: 1|2|3, url, detectedType, kind: video|photo, swimmerId, competitionId, resultId}`
- Data: `GET /api/me/media` (extend per brief §6.1), `GET /api/me/moderation/media` (new, §6.2), `GET /api/swimmers/{id}/results-brief` (new, §6.3), existing submit/withdraw/decide endpoints.
- Derived item status: no publications → private; any pending → pending; any approved → published; else rejected.

## Design Tokens
Colors (from groups.html source):
- Page text: `#f3f8fd`; secondary `#cbe0f0` at 45–75% alpha; body copy `rgba(226,240,252,0.82)`
- Accent (chips, links, headers): `#7dd3fc`; hover fill `rgba(56,189,248,0.12)`
- Primary CTA green: `#38ef8f` on `#04101f`
- Status: pending `#ffca7a`, published `#38ef8f`, rejected `#ef5350`, private `#94a3b8` — each chip: text=color, border=color@45%, bg=color@8% (color+word, never color alone)
- Card: bg `linear-gradient(180deg,rgba(56,189,248,0.08),rgba(8,25,48,0.78))`, border `rgba(125,211,252,0.22)`, shadow `0 24px 60px rgba(2,10,24,0.5)`, backdrop-blur 14px
- Topbar: existing `--theme-topbar-*` tokens (AppTopbar component as-is)
- Type: Inter (UI) / JetBrains Mono (chips, counts, times, buttons). Weights: 900 headings, 800 chips/buttons, 700 labels.
- Radii: cards 16–18 (lg 24), buttons/inputs 10–12, chips 8, status chips 5–6, sheets 20 top.
- Theme note: prototype is dark-only (groups.html look). If media.html must follow site light/dark (`--theme-mode-*`), map surfaces to those tokens instead of hard-coded rgba — brief §7.

## Assets
No new assets. Reuse: bg image + shimmer from `home.css`, club/flag icons, `HelperMedia` thumbnails, `UI_SwimmerGallery` lightbox.

## Files
- `My Media Page.dc.html` — desktop reference (open in browser; interactive)
- `My Media Mobile.dc.html` — mobile reference
- `media-page-design-brief.md` — product brief v2 (roles, data contracts, server TODO §6)
