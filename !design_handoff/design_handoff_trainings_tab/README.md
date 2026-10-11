# Handoff: Group page → Trainings tab (`/groups/{slug}?tab=trainings`)

## Overview
Redesign of the Trainings tab. Problems solved: the coach's very long mobile page; trainings split between "Trainings & videos" and a separate "Training log" screen; a dense coach row; no calendar; inline forms that push the list around.

## About the design files
`Trainings Tab.dc.html` is a **design reference built in HTML**: a prototype of the intended look and behaviour, not code to copy. Rebuild it in the existing client (`client/src/projects/hub-groups-project/`, React + Tailwind) with the `theme-deep` / `theme-deep-light` tokens from `components/deep/deep-theme.css` and the shared `deep/*` components. Do not copy the prototype's inline styles or its `--bg/--acc/…` aliases: they map one to one to `--deep-*` (see Tokens).

## Fidelity
**High fidelity.** Colours, type, radii, spacing and copy are final. Match them using the codebase's tokens and recipes (`.deep-card`, `.deep-seg`, `.deep-filter-chip`, `.deep-tab--edit`, `.deep-edit-chip`).

## Where it lands in the code
| Change | File |
|---|---|
| Segmented view `Next · Trainings · Lanes`, `?view=` | `components/group-trainings-tab.tsx` (`NextSessionCard` splits up) |
| Answers for the next 3 trainings, quick notes, break row | `group-pool.tsx` (`RsvpSegment`, `RsvpNotes`, `MyBreakControl`) |
| Roll call: one status button, ⋮, filter chips, undo toast | `group-rsvp.tsx` (`RsvpPeopleList`), `person-status.tsx` |
| Mini pool / Your lane / Lanes editor | `group-pool.tsx` (`LanePool`), `lane-plans-api.ts` |
| Merged trainings timeline, Sets & times, add-video / new-training sheets | `group-sessions.tsx` (today the "Trainings & videos" card) on `use-training-sessions.ts` |
| Hero strip, banner, sticky chip marks (6a) | `group-hero.tsx`, `group-training-slots.tsx`, `group-rsvp-banner.tsx`, `group-rsvp.tsx` (`rsvpStickyChip`), `group-page.tsx` |
| Attention marks | new `use-training-marks.ts` (see `marks-spec.md`) |
| Types | `types.ts` (`TrainingSession`, `LanePlan`, `TrainingSessionList`) |

The "Training log" block in `group-trainings-tab.tsx` (the `🔒 Open training log →` link) and the "Trainings & videos" card both merge into the Trainings view.

### Sections in the file (newest at the top)
| Id | What | Build? |
|---|---|---|
| **6a** | Related places: hero next strip, "Are you coming?" banner, sticky-bar chip, Following status menu | yes |
| **6b** | Desktop lane editor + desktop modals | yes |
| **5a** | Break sheet, delete confirm, lightbox, Sets & times expanded | yes |
| **4a** | Empty states + guest lock | yes |
| **3a / 3b** | Desktop 1280 member / coach | yes |
| **2a** | **Final mobile structure**, member + coach, Next · Trainings · Lanes | yes |
| 1d / 1e | Mobile sheets / mobile lanes, used by 2a | yes |
| 1a / 1b / 1c | Explorations | no |

Theme: the `theme` prop (dark / light) switches everything. The light theme has no separate artboards.

---

## Structure

### Mobile (<640px): segmented switch, three short views
`Next | Trainings | Lanes`, stored in `?view=next|trainings|lanes` (default `next`). Members get the accent segment (`--deep-seg-*`); coaches get a purple selected pill (`--deep-ow` bg, `--deep-accent-ink` text).

- **Next**
  - Member: the "Your answers" card covers the next 3 trainings. Each row has three 40px buttons (✓ ? ✕); tap again to clear.
  - Member: the next-training card shows "Your lane" (level, workout, lane mates), a mini pool for all lanes (tap opens Lanes), counters and quick-note chips.
  - Member: a "Taking a break?" row opens the break sheet (5a).
  - Coach: date nav ‹ ›, a stacked bar, then the mini pool with "Draft · N unassigned → Lanes ›".
  - Coach: filter chips `All · — · ✓ · ? · ✕ · ⏸` with counts.
  - Coach: roll-call rows show avatar 28, name (`<bdi>`, truncate end), meta, **one status button** (min 100×40) and **⋮** (44px).
    - Tapping the status button cycles `— → ✓ → ? → ✕`.
    - ⋮ or long-press opens the person sheet (1d #3: answer + quick notes + status Active / On break + date / Doesn't train with us).
    - After every change, show a toast `<name> → ✓ Going · Undo` above the coach bar for 5 s.
  - Coach: a bottom coach bar shows `✎ YOU CAN EDIT` and `+ New training`.
- **Trainings**: one merged timeline that replaces "Trainings & videos" and the "Training log" screen.
  - Filters: `All · ⏱ Times · ▶ Videos`.
  - Entry: a date column (weekday and day) with the card on the right. The card holds:
    - the name (coach title, otherwise the imported workout) and meta;
    - if there are times: Volume (km) / Swimmers / Avg effort, plus a "You" line with your best swim;
    - a media strip of 86×60 tiles.
  - Entry actions: `+ Add video` and `Sets & times ›`.
    - `Sets & times` expands in place (5a #4): set headers, then rows `1.1 · name · time · V3 · level`, with an `All / Me` toggle and your rows tinted `--deep-accent-chip`.
  - Coach: `Edit` (purple); `Delete` (danger) only when the training has no times. Media ✕ on every tile for coaches, own tiles only for members.
  - Show 6, then `Older trainings ↓`.
- **Lanes**
  - Member: the published plan only, "Your lane" first (big), other lanes compact. A gold note appears when the coach moved you.
  - Coach editor (1e / 2a): one card per lane with a level select, workout textarea and swimmer chips. Tap a chip to get `Move <name> to [1][3][Unassigned]`. Toolbar: lanes −/+, Distribute, Copy from …. Then the Unassigned tray, Delete plan, and a sticky `Publish` in the bottom bar.

### Desktop (≥960px): no switch
- **3a member**: grid `minmax(0,1.1fr) minmax(0,1fr)`, gap 14.
  - Left, the Next card: answers for 3 trainings as 3 mini cards, quick notes, Break link, and the full pool (columns `110px 170px 1fr 40px`: lane · workout · swimmers · YOU).
  - Right: the Trainings timeline.
- **3b coach**:
  - The Trainings tab sits after a dashed `COACH TOOLS` divider, purple, with a red count badge.
  - Left: the pool with `✎ Edit lanes` + `Publish`, then roll call in **two columns** (status button 88×32 + ⋮).
  - Right: the timeline with `+ New training`.
- **6b**:
  - `✎ Edit lanes` swaps the panel for the editor: lanes as columns, a drag handle ⠿, a "Drop here" zone, and an Unassigned column of 220px. `Move to` via ⋮ stays as the non-drag path.
  - Forms are 480px centred modals with the same fields as the sheets.

### Forms
Mobile = bottom sheets (1d), desktop = centred modals (6b). Never inline.
- **New training**:
  - Fields: Date (default today; locked if the training has times), Start (optional), Name, Note.
  - Hint: "No times needed — add the training, then attach videos to it."
- **Add video**:
  - Fields: Training (picker, preselected from the entry), Link (https, source detected → chip), type `Video / Photo / Album` (guessed from the link), Caption. Shows a preview thumb when YouTube.
  - Hint: "Only the group sees it. The site keeps just the link." + quota `N of 10 yours`.
- **Delete**: a centred dialog with the object summary, focus on **Cancel**, Delete in danger outline. Copy says the links are removed while the videos stay at their source.
- **Break**: chips `1 week · 2 weeks · 1 month · Pick date`, the "Back on" date, the effects ("Skips N trainings", "Answering Going ends the break"), and `Start break`. On Next the result shows as `⏸ On break until Oct 14 · I'm back`.
- **Lightbox**: always dark regardless of theme; header with title and `1 / N`; 16:9 player; caption; "Added by"; dots; `Open on <source> ↗`; Delete (if allowed); swipe between the training's links.

---

## Attention marks (one per tab, most urgent wins)
| Mark | Meaning | Where |
|---|---|---|
| Red count pill (`--deep-danger` bg, `--deep-accent-ink` text, 18px, mono 10.5) | Coach: N people with no answer for the next training | Next segment, group tab "Trainings", hero strip, sticky chip |
| Red dot 7px | Member: no answer for the next training | Next segment, hero strip ("Coming?"), banner, sticky chip |
| Gold dot (`--deep-gold-bar`) | Attention, not urgent: lane plan is a draft or has unassigned swimmers (coach); the coach moved you (member) | Lanes segment, pool footer |
| Accent dot (`--deep-accent`) | New since the last visit (videos) | Trainings segment, entry "3 NEW" |

- No green marks: "all good" shows nothing.
- Red and gold persist until resolved. The accent dot clears when the view is opened.
- Red, gold and green already mean ✕ / ? / ✓ in answers, so the marks are always dots or pills, never filled buttons.

## Empty states (4a)
Shared pattern: a 16px-radius card with a dashed border, a 40px glyph circle, a 15px/800 title and one 12.5px line explaining where the content will come from. Coaches get actions; members get the explanation only.
- **Guest**: the tab shows 🔒, then "Members only", days and pool, and `Join group`.
- **No schedule**:
  - The Lanes segment is hidden.
  - Member: an explanation.
  - Coach: `Set schedule` (opens Admin → Training schedule) and `+ One-off training`.
- **No trainings**: "Trainings show up here after the coach adds one or imports times."
- **Training without videos**: a dashed 86×60 `+ Add video` tile plus the line "Members can add links too."

## Related places (6a)
- **Hero next strip**:
  - Member, unanswered: `Coming?` + red dot.
  - Member, answered: `✓ Going · lane 2`.
  - Coach: `5✓ 1? 2✕` + red count.
- **Banner "Are you coming?"**: above the photo, members only, while unanswered. Three 44px buttons. ✕ hides it until the next training.
- **Sticky chip**:
  - Member, unanswered: `● Answer · Wed 20:00` (danger border).
  - Member, answered: `✓ Going · Wed 20:00` (live border).
  - Coach: `5✓ 1? 2✕ ②` (ow border).
- **Following tab**: the status menu has `Active · ⏸ On break · Doesn't train with us · Inactive — left the group`, each with a one-line description. The level select sits next to it.

## Tokens
Only from `deep-theme.css`:
- Answers: going `--deep-live`; not sure `--deep-gold` / `--deep-gold-bar` / `--deep-gold-chip`; can't `--deep-danger` / `--deep-danger-soft`.
- Coach / editable: `--deep-ow`, `--deep-ow-chip`, `--deep-ow-soft`, `--deep-ow-border`.
- Water: `--deep-pool-water`, `--deep-pool-border`, `--deep-pool-label`, `--deep-pool-rope` (dashed lane ropes).
- Gender: `--deep-male`, `--deep-female`.
- Sheets, modals and their backdrop: **no new tokens**. Use the existing roles: `--t-scrim` for the backdrop (page bg at 72%) and `--t-surface-strong` (= `--deep-panel-bg`, dark `#0c2a3f` / light `#fff`) for the sheet or dialog surface. `club-media.tsx` falls back to `var(--deep-scrim, rgba(0,0,0,.55))`; switch it to `--t-scrim`.

### Light theme (`theme-deep-light`)
The prototype's light values match `deep-theme.css` one to one. When building:
- Muted text goes through `--t-text-3`, not `--deep-text-faint` / `--deep-text-ghost`. In light, the role collapses into `--deep-text-mute`, because faint and ghost on white fall below 4.5:1.
- There are no glows: `--deep-accent-glow` and `--deep-tab-text-glow` already carry light values or `none`. Do not add box-shadows of your own.
- Count pills use `--deep-danger` + `--deep-accent-ink`, which is white on `#b23b3b` in light. This passes, so the pill needs no override.
- The coach segment uses `--deep-ow` + `--deep-accent-ink`, white on `#7c3aed` in light.
- The `--t-scrim` backdrop is light in light (page bg at 72%), so the sheet needs its `--deep-card-border` edge and `--deep-card-shadow`.
- The lightbox stays dark in both themes. Wrap it in `.theme-deep` explicitly, because it is a portal and needs the class passed to it.
- `--thumb` in the prototype is a placeholder gradient only; real thumbs come from the source.

Type: Inter 700/800, `--deep-font-display` for titles and big numbers, JetBrains Mono for times, dates and counts. Radii: card 16, tile 14, row 12, buttons 10, chips 999.

## Rules
- Hebrew names: `dir="auto"` + `<bdi>`, truncate at the end only. UI strings stay English.
- Mobile hit targets are ≥44px (answer buttons 40px plus row padding; ⋮ is 44px tall).
- Everything here is private to the group.
- No `scrollIntoView`. "All N" links use `window.scrollTo`.

## State
```
view: 'next'|'trainings'|'lanes'          // mobile only, ?view=
filter: 'all'|'times'|'videos'            // trainings
rollFilter: 'all'|'none'|'go'|'maybe'|'no'|'break'
expanded: trainingId | null               // Sets & times
sheet: null|'newTraining'|'addVideo'|'person'|'break'|'delete'
undo: { personId, prev } | null           // 5 s
lastSeenTrainingsAt: timestamp            // accent dot
```

## Data (new or changed)
Built on the existing endpoints (`types.ts`):
- `GET /api/hub-groups/{id}/sessions` (`TrainingSessionList`) is the merged timeline. Add `stats: {km, swimmers, effort} | null` and `my_best` to `TrainingSession`. `has_results` already decides Delete and the date lock.
- `GET /api/hub-groups/{id}/sessions/{sid}/sets` (new, lazy) feeds Sets & times.
- `PUT /api/hub-groups/{id}/rsvp/{session}` is already keyed by session, so members can answer the next 3. The page needs `next_trainings: NextTraining[]` (3) beside `next_training`.
- Attention marks: see **`marks-spec.md`**. Marks are derived on the client from rsvp, lane plan and sessions. The backend adds `moved_from_lane`, `new_media_count` / `is_new`, and `PUT …/sessions/seen`. The `/me/marks` endpoint is dropped.

## Files
- `Trainings Tab.dc.html`: the design reference; open it in a browser.
- `marks-spec.md`: the data rules for attention marks.
- `support.js`: the prototype runtime only.
