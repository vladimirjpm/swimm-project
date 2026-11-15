/**
 * «Чья» дорожка на экране участника (Т5 + режим «👁 View» управляющего, решение Влада 15.11.2026).
 * Участнику — та, где он стоит (`is_me`). Управляющему в режиме View — выбранная в ряду дорожек;
 * не выбрана (или такой дорожки больше нет) — своя, иначе первая с людьми, иначе первая.
 * Чистые функции — тесты в `lane-preview.test.mjs`.
 */

interface LaneLike {
  lane_no: number;
  people: { is_me?: boolean }[];
}

/** Номер своей дорожки участника: та, где среди людей есть `is_me`; нет — null. */
export function ownLaneNo(lanes: readonly LaneLike[]): number | null {
  return lanes.find((l) => l.people.some((p) => p.is_me))?.lane_no ?? null;
}

/** Номер дорожки, которую показывает превью управляющего. Дорожек нет — null. */
export function previewLaneNo(lanes: readonly LaneLike[], picked: number | null): number | null {
  if (picked != null && lanes.some((l) => l.lane_no === picked)) return picked;
  return ownLaneNo(lanes)
    ?? lanes.find((l) => l.people.length > 0)?.lane_no
    ?? lanes[0]?.lane_no
    ?? null;
}
