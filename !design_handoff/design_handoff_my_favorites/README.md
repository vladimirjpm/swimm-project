# Handoff: My Favorites — option 1b "One switch per swimmer"

## Overview
Profile page where the user manages favorite swimmers and clubs. Every favorite swimmer has one of three levels:

- **Me** (★, accent/blurple). Only one allowed. Means "this swimmer is me". Top of every list.
- **Family** (gold heart). Up to 4, not counting Me. Listed right after Me in start lists, head-to-head and the favorites card. It changes order only and gives no extra access.
- **Favorite** (red heart). Every other followed swimmer.

Option **1b** puts all swimmers in one flat list. Each row has a three-way segmented switch **Me / Family / Favorite** plus a remove (×) button, and the row's heart icon reflects the current level. Favorite clubs sit below in their own list; clubs only have the Favorite level.

## About the Design Files
The files in this bundle are **design references created in HTML**: prototypes that show the intended look and behavior. They are not production code to copy directly. Recreate them in the target codebase's existing environment (React, Vue, etc.) using its established patterns and libraries.

`My Favorites.dc.html` holds three options (1a, 1b, 1c). **Implement only 1b** (the section with `id="1b"`). Open the file in a browser; the demo is interactive, and "Reset demo" restores the initial state.

## Fidelity
**High-fidelity** for structure, behavior and states. Visual styling uses the Nocturne dark design system (the tokens are below). If the production app has its own theme (the current app is light), keep the layout, sizes and behavior and map colors to the app's theme. The **level colors (accent/gold/red) must stay distinct**.

## Screens / Views

### Desktop (reference width 760px card)
Card: `background: --color-surface (#232532)`, radius 14px, padding 28px, `box-shadow: 0 0 0 1px #3f424d`.

1. **Header row**, flex with space-between and baseline alignment, margin-bottom 12px
   - Left: "Swimmers" in 18px/500 plus the count in 14px `--color-neutral-400`.
   - Right: "Me {n}/1 · Family {n}/4" in 12px `--color-neutral-400`.
2. **Swimmer row** (repeated). Flex, align center, gap 12px, padding 10px 0, border-bottom 1px `--color-neutral-800`. Order is **Me → Family → Favorite**, and original order within each level. Contents from left to right:
   - Level icon, 18px, fixed 20px width. Me uses the filled star `ph-fill ph-star` in `--color-accent #9184d9`. Family uses the filled heart `ph-fill ph-heart` in gold `oklch(0.82 0.14 85)`. Favorite uses the filled heart in red `oklch(0.66 0.2 22)`.
   - Club avatar: a 32px circle with `--color-neutral-800` background and a `--color-neutral-200` 13px letter. This is a placeholder; use the real club logo.
   - Name block, flex 1 with min-width 0. Name is 14px/500 and club is 12px `--color-neutral-400`. Both are `dir="auto"` and left-aligned, because names are Hebrew.
   - **Segmented switch**. The container is flex with padding 3px, gap 2px, radius 8px, background `--color-neutral-900` and a 1px `#3f424d` ring. It has three buttons:
     - Each button: padding 5px 10px, radius 6px, 12px text, icon and label with a 5px gap.
     - **Active** button: 1px border in the level color, background = level color at 16% (`color-mix(in oklch, <color> 16%, transparent)`), `--color-text` label, filled icon.
     - **Inactive** button: transparent border and background, `--color-neutral-400` label, outline icon (`ph ph-star` / `ph ph-heart`).
     - **Disabled** button (Family while 4/4 are used): opacity 0.45, tooltip "Family is full (4/4)".
     - Labels: "Me", "Family", "Favorite".
   - Remove: a ghost icon button with `ph ph-x`, tooltip "Remove from favorites".
3. **Clubs**. Heading "Clubs" plus count, with margin-top 36px. Each row has the red filled heart (20px column), the club avatar, the name (14px/500) and the × remove button. Clubs have no switch.

### Mobile (390px wide)
- Page padding is 16px. Top shows "‹ My profile" (13px `--color-neutral-400`), then the H1 "My favorites" (26px/500), then "Me n/1 · Family n/4" (12px muted).
- Swimmer row: padding 10px 0 12px with a bottom border.
  - Line 1 holds the level icon (18px), the name (15px/500) with the club (12px) under it, and the × button (44×44).
  - Line 2 is the segmented switch at **full width**: a 3-column grid of equal columns. Each button is at least 40px tall with 13px text. Everything else matches desktop.
- Clubs: heart, name (15px/500) and a 44×44 × button.
- Hit targets are at least 44px.

## Interactions & Behavior
- **Switch → Me:** the swimmer becomes Me. The previous Me, if any, drops to **Favorite**. If the swimmer was Family, it leaves Family.
- **Switch → Family:** only allowed when fewer than 4 family members exist, otherwise the button is disabled. If the swimmer was Me, Me becomes empty.
- **Switch → Favorite:** removes the swimmer from Me or Family.
- **× Remove:** takes the swimmer off favorites entirely and clears Me/Family if it held either. Consider adding a confirmation or an undo toast; the prototype removes immediately.
- After any change the list re-sorts right away (Me, Family, Favorite).
- Hover, pressed and focus states follow the design system: hover tint from the accent ramp, and focus `outline: 2px solid var(--color-accent); outline-offset: 2px`.
- Empty states are not designed. Suggested: no swimmers → a short line with a link to search; Me empty → the counter shows 0/1.

## State Management
```
favorites: Swimmer[]    // {id, name, clubName, clubLogo}
meId: id | null         // max 1
familyIds: id[]         // max 4, excludes meId
favoriteClubs: Club[]
```
Derived: `level(s) = s.id === meId ? 'me' : familyIds.includes(s.id) ? 'family' : 'fav'` and `familyFull = familyIds.length >= 4`.
The backend should enforce the same rules: one Me per user, at most 4 family, and Me not counted in family. Persist each change (optimistic update, rollback on error).

## Design Tokens (Nocturne)
- `--color-bg` #161826 · `--color-surface` #232532 · `--color-text` #e9e9ed
- `--color-accent` #9184d9 (Me) · `--color-accent-300` for accent text
- Gold (Family) `oklch(0.82 0.14 85)` · Red (Favorite) `oklch(0.66 0.2 22)`
- Neutrals: `--color-neutral-200/400/800/900`. See `styles.css` for the hex values.
- Radius: sm 4px · md 8px · lg 14px
- Shadow: sm `0 0 0 1px #3f424d`
- Font: Inter; headings 500 weight (never bolder)

## Assets
- Icons: Phosphor (https://phosphoricons.com): `star`, `heart` (regular and fill), `x`, `caret-left`.
- Club logos: placeholders in the prototype. Use real club logos.

## Files
- `My Favorites.dc.html`: the prototype (section `#1b`). Logic is in the `class Component` script: `setLevel`, `remove`, `renderVals`.
- `support.js`: the runtime needed to open the prototype.
- `_ds/.../styles.css`, `_ds_bundle.js`: Nocturne tokens and components.
