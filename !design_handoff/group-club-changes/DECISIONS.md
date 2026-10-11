# Decisions log: entity hero / group page

- **Hero mobile photo**: see HERO-PHOTO.md (desktop 4:3 1600×1200; mobile 4:3 1200×900 optional; fallback = desktop photo cropped to 180px strip).
- **Tabs**: Home/Overview · Season · Results · Team · Media (+ Trainings). Results holds chips Results · Records · Season bests (as on the swimmer page). Lanes lives inside Trainings (Sessions | Lanes). More appears only when >6 top tabs.
- **Roles**: admin/coach see Trainings + Admin pinned to the bottom (mobile) in purple `--deep-ow-*`; editable top tabs (Team, Media) purple + ✎.
- **Member menu**: "✓ Member" + ⋯ → Leave group.
- **Next training (chosen: 2a)**: guest = schedule + place only, no NEXT. Member = NEXT + bar + counts (going · not sure · not going) + 3 buttons Going / Not sure / Can't come, pressed state stays. Coach = counts + avatars + "Who's coming →". Option: if member hasn't answered, card sits above the photo; after answering it returns to the hero.
- **Sticky header** after ~300px scroll: 48px bar (avatar, name one line, RSVP status / coach counts), tabs stick below it.
- **Lane view (3b "pool by lanes")**: NOT for the hero. Use it inside the **Trainings tab** (Sessions view of the next session): lanes with swimmer circles, solid = going, dashed = not sure, quick notes (late / 1st hour / leaving early).
- **Club page is read-only**: no admin/coach roles, no Admin tab, no ✎/purple. Everything manageable lives in the club's official group.
- **Club mobile hero (7a)**: `✓ Following` is a small 26px button under the 72px logo (tap → menu with Unfollow; guest: `+ Follow`). "Official group →" is a text link with a mini group avatar under the sub-line.
- **Official group link is rendered ONLY if the club has an official group** (`club.official_group_slug` is set). No group → no link, no empty placeholder, no extra spacing.
