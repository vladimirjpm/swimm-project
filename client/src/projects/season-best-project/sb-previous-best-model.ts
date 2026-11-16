/**
 * Карточка «лучший прошлого сезона в этой категории» на странице `/season-best`.
 *
 * Категория — срез страницы (дисциплина × бассейн × возраст/группа × пол), но БЕЗ клуба:
 * вопрос «какое время было лучшим у десятилеток год назад» — про страну, а не про клуб.
 * Возраст сезонный, поэтому «10 лет» прошлого сезона — родившиеся на год раньше нынешних
 * десятилеток: сравниваются ступени, не люди (решение Влада 16.11.2026).
 *
 * Пол не выбран — в срезе оба пола, и у каждого свой лидер: одна строка на пол.
 *
 * Модуль без импортов — его гоняет `node --test` напрямую (sb-previous-best-model.test.mjs).
 */

export type SbGender = 'male' | 'female';

/** Минимум полей строки `/api/season-best/list`, нужный карточке. */
export interface SbLeaderRow {
  gender: SbGender;
  time_ms?: number | null;
}

/** Минимум полей ответа — чтобы узнать, про тот ли он срез. */
export interface SbSliceEcho {
  season: number;
  style: string;
  distance: string;
  pool_type?: string | null;
  gender?: string | null;
  age?: number | null;
  age_to?: number | null;
  age_group?: string | null;
}

/** Ожидаемый срез: сезон и фильтры страницы. */
export interface SbSliceWanted {
  season: number;
  style: string;
  distance: string;
  poolType: string | null;
  gender: string | null;
  age: number | null;
  ageTo: number | null;
  ageGroup: string | null;
}

/** Строка карточки: лидер прошлого сезона и разница с нынешним лидером того же пола. */
export interface SbPreviousLine<T extends SbLeaderRow> {
  gender: SbGender;
  previous: T;
  /** Нынешний − прошлый, мс: < 0 — в этом сезоне быстрее. null — сравнить нечем. */
  deltaMs: number | null;
}

/**
 * Ответ про ТОТ ЖЕ срез? Хук списка держит прошлый ответ, пока грузится новый, и без этой
 * проверки после смены фильтра карточка секунду показывала бы чужую категорию.
 */
export function isSameSlice(echo: SbSliceEcho, wanted: SbSliceWanted): boolean {
  return echo.season === wanted.season
    && echo.style === wanted.style
    && echo.distance === String(wanted.distance).replace(/m$/i, '')
    && (echo.pool_type ?? null) === (wanted.poolType ?? null)
    && (echo.gender ?? null) === (wanted.gender ?? null)
    && (echo.age ?? null) === (wanted.age ?? null)
    && (echo.age_to ?? null) === (wanted.ageTo ?? null)
    && (echo.age_group ?? null) === (wanted.ageGroup ?? null);
}

/** Первая (самая быстрая) строка каждого пола; строки уже отсортированы сервером по времени. */
export function leadersByGender<T extends SbLeaderRow>(rows: readonly T[]): Partial<Record<SbGender, T>> {
  const out: Partial<Record<SbGender, T>> = {};
  for (const row of rows) {
    if (!out[row.gender]) out[row.gender] = row;
    if (out.male && out.female) break;
  }
  return out;
}

/**
 * Разница со знаком: «−0.42» (в этом сезоне быстрее) / «+0.15» (медленнее) / «=».
 * Знак обязателен — цифра без него не говорит, в чью пользу она.
 */
export function deltaLabel(ms: number): string {
  return ms === 0 ? '=' : `${ms < 0 ? '−' : '+'}${(Math.abs(ms) / 1000).toFixed(2)}`;
}

/**
 * Строки карточки: ♂ потом ♀, только полы, у которых в прошлом сезоне кто-то плыл.
 * gender — фильтр страницы: выбран пол — строка одна.
 */
export function previousBestLines<T extends SbLeaderRow>(
  currentRows: readonly SbLeaderRow[],
  previousRows: readonly T[],
  gender: string | null,
): SbPreviousLine<T>[] {
  const now = leadersByGender(currentRows);
  const before = leadersByGender(previousRows);
  const genders: SbGender[] = gender === 'male' || gender === 'female' ? [gender] : ['male', 'female'];

  return genders.flatMap((g) => {
    const previous = before[g];
    if (!previous) return [];
    const nowMs = now[g]?.time_ms;
    const deltaMs = nowMs != null && previous.time_ms != null ? nowMs - previous.time_ms : null;
    return [{ gender: g, previous, deltaMs }];
  });
}
