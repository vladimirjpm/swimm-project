import { Result } from '../interfaces/results';

/**
 * Пересчитывает позиции по лучшему результату каждого спортсмена
 * за все дни соревнования (по каждой дисциплине отдельно).
 *
 * Добавляет к каждому результату:
 *   - position_recalc:  пересчитанная позиция (по best time среди всех дней)
 *   - best_time:        лучшее время спортсмена в этой дисциплине
 *   - is_best_result:   true если данный ряд — лучший (fastest) для этого спортсмена
 */

export interface RecalcFields {
  position_recalc: number | null;
  best_time: string | null;
  is_best_result: boolean;
}

export type ResultWithRecalc = Result & RecalcFields;

// ─── Публичная функция ───────────────────────────────────────────────

export function recalculatePositions(results: Result[]): ResultWithRecalc[] {
  if (!results || results.length === 0) return [];

  // 1. Группируем по дисциплине
  const eventGroups = new Map<string, Result[]>();

  for (const res of results) {
    const key = buildEventKey(res);
    if (!eventGroups.has(key)) eventGroups.set(key, []);
    eventGroups.get(key)!.push(res);
  }

  // 2. Для каждой группы находим лучший результат каждого спортсмена
  const enriched = new Map<Result, RecalcFields>();

  for (const [, group] of eventGroups) {
    // swimmerKey → { time, timeMs, ref (ссылка на result-объект) }
    const swimmerBest = new Map<string, { time: string; timeMs: number; ref: Result }>();

    for (const res of group) {
      if (isInvalidTime(res)) continue;

      const swimmerKey = buildSwimmerKey(res);
      const timeMs = timeToMs(res.time);

      const existing = swimmerBest.get(swimmerKey);
      if (!existing || timeMs < existing.timeMs) {
        swimmerBest.set(swimmerKey, { time: res.time, timeMs, ref: res });
      }
    }

    // Сортируем спортсменов по лучшему времени
    const sorted = [...swimmerBest.entries()]
      .sort((a, b) => a[1].timeMs - b[1].timeMs);

    // Создаём карту swimmerKey → position_recalc
    const positionMap = new Map<string, number>();
    sorted.forEach(([key], idx) => positionMap.set(key, idx + 1));

    // Присваиваем position_recalc каждому результату группы
    for (const res of group) {
      const swimmerKey = buildSwimmerKey(res);
      const best = swimmerBest.get(swimmerKey);

      enriched.set(res, {
        position_recalc: isInvalidTime(res) ? null : (positionMap.get(swimmerKey) ?? null),
        best_time: best?.time ?? null,
        is_best_result: best?.ref === res,
      });
    }
  }

  // 3. Возвращаем обогащённый массив (тот же порядок, что и вход)
  return results.map(res => ({
    ...res,
    ...(enriched.get(res) ?? { position_recalc: null, best_time: null, is_best_result: false }),
  }));
}

// ─── Вспомогательные ─────────────────────────────────────────────────

/**
 * Ключ дисциплины. Последняя ось — КАТЕГОРИЯ ЗАПЛЫВА, если она известна, иначе возраст
 * пловца (как было до появления поля). Держать синхронно с серверным
 * CombinedPlaceCalculator.EventKeyOf — иначе объединённые места на клиенте и сервере
 * разойдутся.
 *
 * Почему категория: в открытом заплыве («- Men») плывут разные возрасты и ранжируются
 * одной таблицей, а паралимпийская программа («- Men Para») не должна смешиваться с
 * основной, хотя возраст там встречается тот же. Фоллбек на возраст обязателен: у старых
 * строк категории нет, и без него все возрасты сводного соревнования (8–11) схлопнулись бы
 * в один зачёт.
 */
function buildEventKey(res: Result): string {
  return [
    res.event_style_name ?? '',
    res.event_style_len ?? '',
    res.pool_type ?? '',
    res.event_style_gender ?? '',
    res.event_category || res.event_style_age || '',
  ].join('|');
}

function buildSwimmerKey(res: Result): string {
  return [
    (res.first_name ?? '').trim(),
    (res.last_name ?? '').trim(),
    res.birth_year ?? '',
    res.club ?? '',
  ].join('|');
}

/** Парсит "MM:SS.ms" или "SS.ms" в миллисекунды */
export function timeToMs(time: string): number {
  if (!time || typeof time !== 'string') return Infinity;

  const parts = time.split(':');
  let seconds: number;

  if (parts.length === 2) {
    seconds = parseInt(parts[0], 10) * 60 + parseFloat(parts[1]);
  } else {
    seconds = parseFloat(parts[0]);
  }

  return isNaN(seconds) ? Infinity : Math.round(seconds * 1000);
}

function isInvalidTime(res: Result): boolean {
  return (
    !res.time ||
    res.time_fail === true ||
    (res.time_fail as any) === 'true' ||
    ['DQ', 'DNS', 'DNF', 'DSQ'].includes(String(res.time).toUpperCase())
  );
}
