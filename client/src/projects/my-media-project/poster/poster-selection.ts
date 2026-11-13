// Какие заплывы идут на плакат (решение Влада 12.11.2026): пользователь отмечает их галочками,
// по умолчанию отмечены все; у раскладки есть предел строк, и если заплывов больше — лишние по
// умолчанию сняты. Чистые функции, тест — `poster-selection.test.mjs`.

import type { PosterLayout, PosterSwimSource } from './poster-data';

/** Сколько строк помещается: A — список рядом с фото, B — таблица, C — две ленты по пять. */
export const POSTER_LIMIT: Record<PosterLayout, number> = { A: 8, B: 9, C: 10 };

/** Заплыв «с достижением» — с медалью или меткой: такие снимаются последними. */
const notable = (s: PosterSwimSource) => s.medal || s.mark != null;

/**
 * Оставляет из `ids` не больше `limit` заплывов. Снимает сперва заплывы без медали и без
 * метки, начиная с поздних; когда такие кончились — поздние из остальных. `swims` — в порядке
 * дней (`posterSwims`).
 */
export function clampSelection(swims: PosterSwimSource[], ids: ReadonlySet<number>, limit: number): Set<number> {
  const picked = swims.filter((s) => ids.has(s.id));
  if (picked.length <= limit) return new Set(picked.map((s) => s.id));
  const keep = [...picked.filter(notable), ...picked.filter((s) => !notable(s))].slice(0, limit);
  return new Set(keep.map((s) => s.id));
}

/** Выбор по умолчанию: все заплывы, сколько помещается в раскладку. */
export function defaultSelection(swims: PosterSwimSource[], layout: PosterLayout): Set<number> {
  return clampSelection(swims, new Set(swims.map((s) => s.id)), POSTER_LIMIT[layout]);
}

/** Переключает галочку; сверх предела раскладки ещё один заплыв не отмечается. */
export function toggleSelection(ids: ReadonlySet<number>, id: number, limit: number): Set<number> {
  const next = new Set(ids);
  if (next.has(id)) next.delete(id);
  else if (next.size < limit) next.add(id);
  return next;
}
