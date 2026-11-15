import type { WorkoutCell, WorkoutLine, WorkoutSet } from '../../../types';

/**
 * Подписи и поля workout (docs/plans/trainings-coach-flow-plan.md, §5.5, CF6): «4×100», «3× 2:10»,
 * сводка строки, подпись уровня над колонкой. Чистые функции — тесты в `workout-format.test.mjs`.
 */

/** «4×100», «100», «4×» (повторы без дистанции), пусто. */
export function repsDistanceLabel(reps: number, distance: number | null | undefined): string {
  if (distance == null) return reps > 1 ? `${reps}×` : '';
  return reps > 1 ? `${reps}×${distance}` : String(distance);
}

/**
 * Поле «повторы × дистанция»: «4x100», «4×100», «100», «100x4» (как на листе — дистанция первой:
 * дистанция — число, кратное 25). Пусто — `{ reps: 1, distance: null }`; не разобрать — null.
 */
export function parseRepsDistance(raw: string): { reps: number; distance: number | null } | null {
  const text = raw.trim().replace(/\s+/g, '');
  if (text === '') return { reps: 1, distance: null };
  const single = /^(\d{1,5})$/.exec(text);
  if (single) return { reps: 1, distance: Number(single[1]) };
  const pair = /^(\d{1,5})[x×X*](\d{1,5})?$/.exec(text);
  if (!pair) return null;
  const a = Number(pair[1]);
  if (pair[2] == null) return a >= 1 && a <= 99 ? { reps: a, distance: null } : null;
  const b = Number(pair[2]);
  // Повторы × дистанция; но «100x4» с листа — дистанция первой (кратна 25, а второе — нет).
  const [reps, distance] = a % 25 === 0 && b % 25 !== 0 ? [b, a] : [a, b];
  return reps >= 1 && reps <= 99 ? { reps, distance } : null;
}

/** Ячейка уровня строкой: «2:10», со своими повторами — «3× 2:10». */
export function cellLabel(cell: WorkoutCell | null | undefined): string {
  if (!cell) return '';
  const sendOff = cell.send_off ?? '';
  return cell.reps != null ? `${cell.reps}× ${sendOff}`.trim() : sendOff;
}

/** Обратно: «3× 2:10» / «3x2:10» — свои повторы и время; «2:10» — только время. */
export function parseCellLabel(raw: string): WorkoutCell {
  const text = raw.trim();
  const own = /^(\d{1,2})\s*[x×X]\s*(.*)$/.exec(text);
  if (own && Number(own[1]) >= 1) {
    return { reps: Number(own[1]), send_off: own[2].trim() || null };
  }
  return { reps: null, send_off: text || null };
}

/** Сводка строки: «4×100 free · 2-3V». */
export function lineSummary(line: WorkoutLine, reps: number = line.reps): string {
  return [repsDistanceLabel(reps, line.distance), line.text ?? '']
    .filter(Boolean).join(' ')
    + (line.intensity ? ` · ${line.intensity}` : '');
}

/** Короткая сводка сета для свёрнутой строки: «4×25 breast · 4×100 free · 50 easy». */
export function setSummary(set: WorkoutSet): string {
  return set.lines.map((line) => lineSummary(line)).filter(Boolean).join(' · ');
}

/**
 * Сокращение уровня над колонкой (решение владельца 15.11.2026: ранг + сокращение, «1 Adv»):
 * первые три буквы первого слова. Полное название — в `title`.
 */
export function levelAbbr(name: string): string {
  const word = name.trim().split(/\s+/)[0] ?? '';
  return word.length <= 4 ? word : word.slice(0, 3);
}

/** Кому сет: «Everyone», «Advanced only», «Adv, Int». */
export function setAudience(set: WorkoutSet, levels: ReadonlyArray<{ id: number; name: string }>): string {
  if (set.level_ids == null) return 'Everyone';
  const named = levels.filter((l) => set.level_ids!.includes(l.id));
  if (named.length === 0) return 'Nobody';
  if (named.length === levels.length) return 'Everyone';
  if (named.length === 1) return `${named[0].name} only`;
  return named.map((l) => levelAbbr(l.name)).join(', ');
}
