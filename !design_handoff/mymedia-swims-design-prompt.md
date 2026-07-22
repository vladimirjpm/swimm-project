# Design prompt — "My media" v3: swim-centric page

Design a redesigned "My media" page for a competitive swimming results site. The page flips from a media-gallery into a **list of the user's swims**, where video links are attached per swim. This becomes the ONLY place to add videos (the public results pages lose that ability).

## Visual style (must match existing site)
Dark navy theme: background gradient `#0d2036 → #0b1b31 → #050e1c`, text `#f3f8fd`, accent cyan `#7dd3fc`, action green `#38ef8f` (dark text `#04101f` on green), warning amber `#ffca7a`. Cards: rounded 14–18px, border `rgba(125,211,252,0.22)`, subtle gradient fills `rgba(56,189,248,0.08) → rgba(8,25,48,0.78)`. Monospace-style bold labels for chips/buttons, uppercase tracking for section labels. Desktop and mobile (mobile uses a bottom-sheet for secondary filters).

## Page structure
1. **Header**: "My media" title, profile breadcrumb, nav pills (Media active / My groups link / Settings soon). Keep as is.
2. **Tabs**: "My swims" (main) and "Moderation" (unchanged, with amber pending badge).
3. **Swimmer chips row**: All + one chip per swimmer (initial avatar + name + count), with "+ Add link" green button on the right.
4. **Primary filter row** (always visible on desktop):
   - Segmented control: **All swims / With video / Without video** — this is the hero filter.
   - **Season** select (year), defaulting to the current season.
   - "More filters" dropdown button with active-count badge.
5. **More filters** (dropdown on desktop, bottom-sheet on mobile): Competition (may contain Hebrew RTL names), Style (Free/Back/Breast/Fly/IM), Distance, Date range (from–to), and Publication status (Private/Pending/Published/Rejected) — status shown only when segment = "With video".
6. **Main list = swim rows, grouped by competition** (competition header: name, date, city; rows under it). The competition header also hosts **competition-level media**: photos/videos attached to the whole competition (podium, team photo, venue) shown as a compact thumbnail strip on the header row (with a "+" tile to add), each with the same per-item status pill and actions as swim videos. Each swim row: distance + style, time, place/points, date. Two row states:
   - **Without video**: muted row with a compact "+ Add video" affordance on the right.
   - **With video**: thumbnail or play-icon chip, publication status pill (private=gray / pending=amber / published=green / rejected=red), and actions: play (opens lightbox), share with a group, withdraw, delete. A swim can have 2–3 videos — show as a small stack/counter ("▶ 2") that expands; actions (play / share / withdraw / delete) and the publication status pill belong to EACH video, not to the swim row — e.g. one video published to a group while another stays private.
   - Relay swims appear too (marked with a small "relay" tag), video attaches the same way.
7. **"Unlinked media" section** at the bottom (collapsed by default, with count): media links not tied to any swim (club videos, general footage). Card per item with the same actions + a "Link to a swim" action. Also an "Add link without a swim" secondary button here.
8. **Empty states**: (a) no favorite swimmers yet — prompt to add favorites; (b) swimmer has no results in the selected season; (c) filters match nothing — "Clear all"; (d) loading skeleton for the grouped list.

## Interactions to show
- Add video flow entry points: "+ Add video" on a swim row (swim pre-selected, only paste URL), "+" tile on a competition header (attach a photo/video to the whole competition), and global "+ Add link" (pick swimmer → attach to a specific swim, to the whole competition, or to neither → URL).
- Share-with-group modal (group select + level select Members/Public + submit) — keep the existing simple modal pattern.
- Mobile: chips scroll horizontally; filters collapse into one "Filters (n)" button opening a bottom sheet with all filters and a "Show N swims" confirm button; swim rows stay single-column, actions behind the row tap or a compact kebab.

## Future social layer (design placeholders now, feature ships later)
Reserve space in the layouts for lightweight reactions — render them in mockups as small muted counters so the grid doesn't reflow when they arrive:
- **Like counter on media** (video/photo): small "♥ 12" chip on the media thumbnail/card. Only published media can be liked, so in My media it's a read-only counter for the owner.
- **Congrats counter on a swim row**: "🎉 5" chip next to the time. On swims that set a record (row already carries a record pill), the congrats icon is emphasized/highlighted — any signed-in visitor on the public pages can tap it; here the owner just sees the count.
Both are single-tap reactions (no comments). Show one row/card variant with these counters present.

## Deliverables
Desktop (≥1280px) and mobile (375px) mockups for: the main list with mixed with/without-video rows, the More-filters open state, the mobile bottom sheet, one empty state, and the Unlinked media section. All UI text in English.
