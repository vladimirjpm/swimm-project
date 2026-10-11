# Hero photo: desktop + mobile variants

Applies to every entity hero that renders `DeepHeroPhoto` (club, group; later swimmer).
Design reference: `Entity Hero Mobile.dc.html` (variants 1a / 1b, Tweaks → "Mobile photo uploaded").
The photo in the mockup is only an example: every club/group uploads its own, of any subject and composition.

## Recommended sizes

| Variant | Where it shows | Aspect | Recommended | Minimum | Rendered slot |
| --- | --- | --- | --- | --- | --- |
| Desktop (`hero_image_url`) | ≥960px, right column of the hero | **4:3** | **1600×1200** | 760×570 | 380px wide × hero height (~280–320px), `object-fit: cover` |
| Mobile (`hero_image_mobile_url`, new, optional) | <960px, full width above the name | **4:3** | **1200×900** | 780×585 | 100% width × 292px (at 390px wide), `object-fit: cover` |

- Format: JPEG or WebP, ≤ 500 KB after upload compression.
- Keep the main subject in the middle: edges get cropped on wider phones, and in variant 1b the lower ~30% is covered by the name overlay.
- Show these numbers as helper text next to each upload slot in Admin.

## Display rules

```
mobile  (<960px):
  if hero_image_mobile_url      → show it, height 292px (4:3 at 390px), object-position: center
  else if hero_image_url        → show the DESKTOP photo reduced to a strip:
                                  height 180px (variant 1a) / 210px (variant 1b),
                                  object-fit: cover, object-position: center
  else                          → no photo block at all (no placeholder for visitors)

desktop (≥960px):
  hero_image_url only (mobile photo is never used on desktop)
```

`show_hero_image = false` hides the photo on both breakpoints.

Implementation: one `<picture>` in `DeepHeroPhoto`:
`<source media="(max-width: 959px)" srcset={mobileUrl}>` when a mobile URL exists, plus the height switch via a modifier class (`.deep-hero-photo--strip` when falling back to the desktop image). No JS media queries.

## Data / API

- New fields on club and hub-group: `hero_image_mobile_url` (resolved by the server, like `hero_image_url`) and `hero_mobile_media_id` (optional pointer "take from media", same mechanism as `hero_media_id`).
- Returned by `GET /api/hub-groups/:slug` and the club overview endpoint.

## Admin (`DeepDisplaySettingsCard`)

Add a second slot under the existing hero photo:

- **Hero photo (desktop)**: "4:3 · recommended 1600×1200 · min 760×570"
- **Hero photo (mobile), optional**: "4:3 · recommended 1200×900 · min 780×585 · if empty, the desktop photo is cropped to a 180px strip"
- Each slot: upload / pick from media / remove, preview framed at the real mobile/desktop slot so the admin sees the crop.
- Warn (not block) when the uploaded image is below the minimum size.
