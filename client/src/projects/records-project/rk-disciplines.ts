/**
 * Дисциплины страницы `/records` и её мелкие подписи.
 *
 * **Лестница дисциплин client-only, и это осознанно.** Программа плавания не меняется —
 * это не данные, а справочник вида «какие заплывы бывают», такой же, как канонические
 * ключи категорий в `results-categories.ts`. Отдельного эндпоинта под неё не заводили:
 * он бы отдавал одно и то же годами, а страница получила бы лишний запрос перед первым
 * показом. Спросили дисциплину, которой в справочнике нет — API вернёт пустой рейтинг,
 * а не ошибку.
 */

export interface RkFilters {
  stroke: string | null;
  /** Форма справочника: «50m», «4X100m» (заглавная X у эстафет). */
  distance: string | null;
  gender: 'male' | 'female' | null;
  poolType: '25m' | '50m' | null;
  /** Какую страну подсветить; на состав рейтинга не влияет. */
  highlight: string | null;
}

/** Страна, чей рейтинг нам домашний, — её строка подсвечена всегда. */
export const HOME_REGION = 'ISR';

export interface RkStroke {
  key: string;
  label: string;
  /** Личные дистанции этого стиля — в том виде, в каком они лежат в справочнике. */
  distances: string[];
  /** Эстафетные — отдельно: смешивать их с личными в одном ряду кнопок нельзя. */
  relays: string[];
}

/**
 * Что реально лежит в `Records` по оси country / <код> / open (замер 16.09.2026, 23 связки
 * стиль × дистанция). Комплекс на 4×100 и 4×50 — это комбинированная эстафета: своего
 * ключа стиля у неё нет, и в справочнике она записана как `individual_medley`.
 */
export const RK_STROKES: RkStroke[] = [
  { key: 'freestyle', label: 'Freestyle',
    distances: ['50m', '100m', '200m', '400m', '800m', '1500m'],
    relays: ['4X50m', '4X100m', '4X200m'] },
  { key: 'backstroke', label: 'Backstroke',
    distances: ['50m', '100m', '200m'], relays: [] },
  { key: 'breaststroke', label: 'Breaststroke',
    distances: ['50m', '100m', '200m'], relays: [] },
  { key: 'butterfly', label: 'Butterfly',
    distances: ['50m', '100m', '200m'], relays: [] },
  { key: 'individual_medley', label: 'Medley',
    distances: ['100m', '200m', '400m'], relays: ['4X50m', '4X100m'] },
];

/** Дисциплина, с которой страница открывается, когда адрес ничего не просит. */
export const RK_DEFAULT: Required<Pick<RkFilters, 'stroke' | 'distance' | 'gender' | 'poolType'>> = {
  stroke: 'freestyle',
  distance: '50m',
  gender: 'male',
  poolType: '50m',
};

export const strokeByKey = (key: string | null) =>
  RK_STROKES.find((s) => s.key === key) ?? null;

/** «4X100m» → «4×100m»: латинская X в справочнике машинная, на витрине нужен знак умножения. */
export const distanceLabel = (distance: string) => distance.replace(/X/g, '×');

export const isRelay = (distance: string | null) => Boolean(distance && /^4X/i.test(distance));

/** «individual_medley» → «Medley»; неизвестный ключ печатаем как есть, без подчёркиваний. */
export const strokeLabel = (stroke: string | null) =>
  strokeByKey(stroke)?.label ?? (stroke ?? '').replace(/_/g, ' ');

/**
 * Подпись дисциплины целиком: «Freestyle 50m · 50m pool · men».
 * Одна на заголовок, на пустое состояние и на `document.title` — иначе разъедутся.
 */
export function disciplineLabel(f: Pick<RkFilters, 'stroke' | 'distance' | 'gender' | 'poolType'>) {
  const parts = [strokeLabel(f.stroke), f.distance ? distanceLabel(f.distance) : null]
    .filter(Boolean).join(' ');
  const pool = f.poolType ? `${f.poolType} pool` : null;
  const who = f.gender === 'female' ? 'women' : f.gender === 'male' ? 'men' : null;
  return [parts, pool, who].filter(Boolean).join(' · ');
}

/**
 * Отставание от мирового словами: «+0.08» / «−0.65».
 *
 * Минус здесь не опечатка и не форматируется в ноль: рекорд страны, который быстрее
 * мирового, — аномалия справочника (И-22, И-23 в docs/data-integrity.md), и витрина
 * обязана её показать, а не спрятать.
 */
export function behindLabel(ms: number | null | undefined): string | null {
  if (ms == null) return null;
  if (ms === 0) return '=';
  const sign = ms > 0 ? '+' : '−';
  return `${sign}${(Math.abs(ms) / 1000).toFixed(2)}`;
}
