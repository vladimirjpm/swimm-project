/**
 * «Как напечатано» у клуба (Р97, план swimmer-club-change §5 Н9; решение Влада 18.11.2026 —
 * «Да, как напечатано»). Строка протокола и экраны соревнования пишут название клуба, как оно
 * стоит в протоколе (`club` строки), а если клуб с тех пор переименован — мелко «now: …».
 * Страница клуба, фильтры и сезонные сводки пишут текущее.
 *
 * Откуда текущее: у строки результата — `club_now`, у Overview — карта `club_now`
 * (id клуба → текущее название), у сводки клубов — `clubNow`. Сервер отдаёт их только для
 * строк, напечатанных под прежним названием.
 */

/** Карта «id клуба → текущее название» из ответа сервера (ключи JSON — строки). */
export type ClubNowMap = ReadonlyMap<number, string>;

/** Собрать карту из нескольких ответов (Overview события и выбранного дня). Пустые пропускает. */
export function mergeClubNow(...sources: (Record<string, string> | null | undefined)[]): ClubNowMap {
  const map = new Map<number, string>();
  for (const src of sources) {
    if (!src) continue;
    for (const [id, name] of Object.entries(src)) {
      const n = Number(id);
      if (Number.isFinite(n) && name?.trim()) map.set(n, name.trim());
    }
  }
  return map;
}

/**
 * Текущее название, которое стоит дописать «now: …» к напечатанному. null — подсказки нет:
 * текущего не знаем или оно то же, что напечатано. Явное `explicit` (поле строки) важнее карты.
 */
export function clubNowLabel(
  printed: string | null | undefined,
  explicit?: string | null,
  clubId?: number | null,
  map?: ClubNowMap,
): string | null {
  const now = (explicit ?? (clubId != null ? map?.get(clubId) : undefined) ?? '').trim();
  if (!now) return null;
  return now === (printed ?? '').trim() ? null : now;
}
