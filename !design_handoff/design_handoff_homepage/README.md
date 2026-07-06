# Handoff: SwimHub Homepage ("Cinematic Dive")

## Overview
Redesign of the site homepage (`home.html` / `src/projects/home-project/home.tsx`). A full-bleed dark "night arena" photo hero with giant typography, four glass destination cards (Competitions, Normatives, Records, Countries-soon), and an auto-scrolling record ticker pinned to the bottom. Includes a mobile layout with a working burger menu, hover/focus card states, and a "no live competition" state.

## About the Design Files
The files in this bundle are **design references created in HTML** (a design-canvas prototype), not production code. The task is to **recreate this design in the existing codebase**: React 18 + Vite + Tailwind, multi-page setup (`home.html` → `src/pages/home-page.tsx` → `src/projects/home-project/home.tsx` + `home.css`). Follow the existing project conventions: Tailwind utility classes inline, only multi-layer backgrounds kept in a small CSS file.

## Fidelity
**High-fidelity.** Colors, typography, spacing, and interaction specs below are final. Recreate pixel-perfectly with Tailwind.

## Reference file
`Homepage Options.dc.html` (open in a browser). Relevant frames on the canvas:
- **1a** — final desktop design (1440×1000)
- **2a** — mobile design (390px wide) with WORKING burger menu (click it)
- **2b** — card states (default / hover / focus-visible) + "no live" variants
- Frames 1b and 1c are rejected explorations — ignore them.

## Branding
Project name is **SwimHub**. Logo lockup: 36×36 rounded square (radius 11px, gradient `linear-gradient(140deg,#38bdf8,#0369a1)`, letter "S" 900 17px `#06263f`) + wordmark `SWIMHUB`, 14px, weight 900, letter-spacing .22em, where "HUB" is `#7dd3fc`.

## Screens / Views

### Desktop homepage (≥1024px), frame 1a
Full-viewport (`min-h-screen`) dark page. NO light theme — homepage is always dark by design decision (see Interactions). No theme toggle on the homepage.

**Background** (keep in `home.css` like the current multi-layer background):
```css
background:
  linear-gradient(160deg, rgba(4,16,34,.72) 0%, rgba(6,20,42,.55) 45%, rgba(2,10,24,.92) 100%),
  url('/images/mix/bg-2.jpg') center 30% / cover no-repeat;
```
Optional "caustic light sweep" overlay layer: absolutely-positioned inset-0, `linear-gradient(105deg, transparent 40%, rgba(125,211,252,.10) 50%, transparent 60%)`, `background-size: 300% 100%`, animation `shimmer 9s linear infinite` (keyframes: background-position -150% 0 → 250% 0), `pointer-events: none`.

**Layout, top to bottom:**

1. **Navbar** — flex row space-between, padding 34px 64px.
   - Left: logo lockup (above).
   - Right: links `Home · Competitions · Normatives · Records · About`, 14px weight 700, color `#c9dcee`, gap 34px; active link `#7dd3fc`.
2. **Hero** — padding 46px 64px 0.
   - Kicker: `2026 SEASON · ISRAEL`, 15px, weight 800, letter-spacing .3em, uppercase, `#7dd3fc`, margin-bottom 18px.
   - Headline: `Every hundredth counts.` — 148px, weight 900, line-height .88, letter-spacing -.045em, white `#f3f8fd`; the word `counts.` is italic with gradient text `linear-gradient(92deg,#7dd3fc,#e0f2fe 70%)` (background-clip: text).
   - Sub: max-width 560px, margin-top 26px, 18px/1.6, `rgba(226,240,252,.82)`. Copy: "Results, records and normatives of the swimming season — live from the pool. Pick a destination below and dive in."
3. **Destination cards** — grid 4 columns, gap 18px, padding 56px 64px 0. Each card is an `<a>`, min-height 190px, border-radius 24px, padding 26px, flex column space-between, `backdrop-filter: blur(14px)`, shadow `0 24px 60px rgba(2,10,24,.5)`.
   - **Competitions** (highlighted): background `linear-gradient(180deg, rgba(56,189,248,.16), rgba(8,25,48,.78))`, border `1px solid rgba(125,211,252,.35)`. Header row: title + LIVE badge (mono 11px 800 `#38ef8f` with 7px pulsing dot, `blink` 1.4s). Body: "Dolphin & All Masters, Youth 8–11, Junior 11–15" (13px, `rgba(203,224,240,.75)`), link line "4 events →" (14px 800 `#7dd3fc`, margin-top 12px). Links to the competitions index (the 4 existing result pages).
   - **Normatives**: same but background gradient starts at `rgba(56,189,248,.08)`, border `rgba(125,211,252,.22)`. Body: "Youth-3 to MSMK grids for every stroke & distance", link "Open grid →".
   - **Records**: same style as Normatives. Body: "Age records, all-time bests, world & national marks", link "Explore →". Optional badge `★ 3 NEW` (mono 11px 800 `#fbbf24`) when new records exist since last visit.
   - **Countries (disabled/soon)**: NOT a link. Background `linear-gradient(180deg, rgba(148,163,184,.06), rgba(8,25,48,.72))`, border `1px dashed rgba(148,163,184,.35)`; title color `rgba(226,240,252,.7)`; `SOON` chip (mono 11px 800 `#94a3b8`, 1px border `rgba(148,163,184,.4)`, padding 3px 8px, radius 7px); footer "Coming 2026" in `rgba(125,211,252,.5)`.
   - Card titles: 26px, weight 900, letter-spacing -.02em.
4. **Record ticker** — fixed to viewport bottom (or absolute bottom of page container): full-width bar, background `rgba(2,10,24,.82)`, top border `1px solid rgba(125,211,252,.25)`, `backdrop-filter: blur(10px)`, padding 16px 0, `overflow: hidden`.
   - Content: a flex row (`gap: 56px`, `width: max-content`, `white-space: nowrap`) of items, DUPLICATED twice, animated `translateX(0 → -50%)` linear infinite, ~26s (speed proportional to content width). Font: JetBrains Mono 14px 700 `#bfe3f7`. Item accents: `NEW RECORD` label `#facc15`; `LIVE` label `#38ef8f`; times `#7dd3fc`.
   - Data source: latest results/records from the existing JSON data (do not hardcode names).

### Mobile homepage (<640px), frame 2a
Same background/palette. Vertical layout, ticker pinned bottom:
1. **Top bar** — padding 18px 20px; logo 30×30 + wordmark 12px; right — 44×44 burger button (two right-aligned bars 22px/15px × 2.5px, `#cfe6f6`, radius 2px, gap 5px).
2. **Hero** — padding 26px 20px 0; kicker 11px/.28em; headline 52px/.92, same gradient on "counts."; sub 14.5px/1.55, "Results, records and normatives — live from the pool."
3. **Cards stacked** — column, gap 12px, padding 26px 16px 0; radius 18px, padding 18px; titles 21px 900; same variants as desktop (Competitions with LIVE, Records with ★ 3 NEW, Countries dashed + SOON). Whole card is the tap target (>44px).
4. **Ticker** — same as desktop, 11.5px, gap 44px, ~22s.

### Mobile burger menu (interactive in frame 2a)
- Tap burger → dropdown panel; burger icon becomes `✕` (20px 700 `#cfe6f6`). Tap `✕` or any item → closes.
- Panel: absolute, top ≈66px (below top bar), left/right 12px, z-index above content; radius 20px; background `rgba(4,16,32,.92)`; border `1px solid rgba(125,211,252,.35)`; `backdrop-filter: blur(18px)`; shadow `0 28px 60px rgba(2,10,24,.7)`; padding 10px.
- Entry animation: `menuIn` 220ms ease-out — opacity 0→1, translateY(-14px)→0.
- Items (flex space-between, padding 15px 16px, radius 13px, 17px weight 800, hover/press bg `rgba(56,189,248,.12)`):
  - Competitions — trailing `● LIVE` (mono 10px 800 `#38ef8f`)
  - Normatives — trailing `→` `#7dd3fc`
  - Records — trailing `★ 3 NEW` (mono 10px 800 `#fbbf24`)
  - About — trailing `→`
- Footer row (separated by 1px top border `rgba(125,211,252,.18)`): "Countries — coming 2026" (11px 700 `rgba(203,224,240,.55)`) + `SOON` chip. Not clickable.

## Interactions & Behavior
- **Card hover** (desktop): translateY(-8px), border-color → `rgba(125,211,252,.8)`, bg gradient slightly brighter, shadow `0 28px 60px rgba(2,10,24,.65)`; transition 180ms ease-out on transform/border/shadow. Countries card: no hover lift (not a link).
- **Card focus-visible**: same visual as hover + `outline: 3px solid #7dd3fc; outline-offset: 3px`.
- **LIVE dot**: `blink` keyframes 1.4s ease infinite (opacity 1 → .25 → 1).
- **Ticker**: pause on hover is nice-to-have; must respect `prefers-reduced-motion: reduce` → static row, no marquee (also disable shimmer and blink).
- **No live competition state** (frame 2b, bottom row):
  - Competitions card: LIVE badge → outlined chip `NEXT · <date>` (mono 10-11px 800 `#7dd3fc`, border `rgba(125,211,252,.4)`, radius 7px, `white-space: nowrap`). Body → "Last: <meet name> — results ready"; link → "View results →" pointing at the latest finished meet.
  - Ticker content → countdown item `NEXT MEET IN 11D 06H · <meet>` (accent `#7dd3fc`) + season bests + `RECORD OF THE MONTH` (`#facc15`).
- **Theme**: homepage is ALWAYS dark; do NOT render the theme toggle here. Theme switching lives on inner pages only (existing `useTheme`); preference persists via localStorage and applies across inner pages.

## State Management
- `menuOpen: boolean` — mobile burger (local state).
- `hasLive: boolean` + `nextMeet {name, date}` — derived from competitions data; switches Competitions card badge/link and ticker content.
- `tickerItems: Array<{kind: 'record'|'result'|'live'|'countdown', label, time?}>` — built from existing results/records JSON.
- No Redux needed beyond what home already uses; data can be fetched from `/data/*.js`/JSON like other pages.

## Design Tokens
- **Colors**: page base `#020a18`–`#06142a` (via overlay); text primary `#f3f8fd`; text secondary `rgba(226,240,252,.82)`; muted `rgba(203,224,240,.75)`; accent sky `#7dd3fc` / `#38bdf8` / `#0ea5e9`; deep `#0369a1`; live green `#38ef8f`; record amber `#fbbf24` / `#facc15`; disabled slate `#94a3b8`.
- **Fonts**: Inter (400/600/700/800/900) — UI & display; JetBrains Mono (500/700/800) — times, badges, ticker. Both from Google Fonts.
- **Radii**: cards 24px (desktop) / 18px (mobile); chips 7px; menu panel 20px; menu items 13px; logo 11px.
- **Shadows**: card `0 24px 60px rgba(2,10,24,.5)`; hover `0 28px 60px rgba(2,10,24,.65)`; menu `0 28px 60px rgba(2,10,24,.7)`.
- **Motion**: hover 180ms ease-out; menu 220ms ease-out; ticker 22–30s linear infinite; blink 1.4s.

## Assets
- `assets/bg-2.jpg` — night arena pool photo. Already exists in the codebase at `client/public/images/mix/bg-2.jpg`; reference it as `/images/mix/bg-2.jpg`.
- No other images; icons are text glyphs (→, ✕, ●, ★).

## Screenshots
In `screenshots/` (quick visual reference; the live HTML canvas is the source of truth — screenshots have minor capture artifacts around `backdrop-filter` blur and the marquee overflow):
- `desktop-1a.png` — desktop homepage
- `mobile-2a-top.png` / `mobile-2a-bottom.png` — mobile layout
- `mobile-2a-menu-open.png` — burger menu open state
- `states-2b.png` — card hover/focus states + "no live" variants

## Files
- `Homepage Options.dc.html` — the design canvas (frames 1a, 2a, 2b are the spec; 1b/1c rejected).
- `assets/bg-2.jpg` — background photo.
- Target files in the codebase: `client/src/projects/home-project/home.tsx`, `home.css`, `client/home.html`.
