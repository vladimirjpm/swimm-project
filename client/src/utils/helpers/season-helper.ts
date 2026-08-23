/**
 * Календарь сезона на клиенте — зеркало серверного `SeasonMath` (Swimm.Domain/SeasonMath.cs).
 *
 * Сезон = 1 сентября — 31 августа, метка по году НАЧАЛА: 15/02/2026 лежит в сезоне 2025
 * («2025/26»). Держим ровно одну копию правила: возраст пловца — понятие сезона, и любая
 * вторая формула рано или поздно разъедется с сервером.
 *
 * Правило и разбор случая — docs/season-boundary-rule.md и docs/data-integrity.md §13.
 */

/** Месяц, с которого начинается сезон (сентябрь; 1-based, как в протоколах). */
export const SEASON_START_MONTH = 9;

/** Год НАЧАЛА сезона, которому принадлежит дата. 01/09/2025 → 2025, 31/08/2025 → 2024. */
export function seasonStartYear(date: Date = new Date()): number {
  return date.getMonth() + 1 >= SEASON_START_MONTH ? date.getFullYear() : date.getFullYear() - 1;
}

/**
 * Возраст пловца В СЕЗОНЕ: сколько ему исполняется в году ОКОНЧАНИЯ сезона. Один и тот же
 * на все старты сезона — так считает федерация.
 *
 * ⚠ Это НЕ «текущий год минус год рождения»: с сентября по декабрь такой счёт даёт возраст
 * на год младше и утаскивает витрину рекордов на ступень ниже (пловчиха 2015 г.р. на старте
 * 31/10/2025 — это сезон 2025/26, ей 11, а не 10).
 *
 * null — год рождения не задан или бессмыслен.
 */
export function ageInSeason(birthYear: number | string, date: Date = new Date()): number | null {
  const year = Number(birthYear);
  if (!year || year < 1900 || year > 2100) return null;

  const age = seasonStartYear(date) + 1 - year;
  return age > 0 ? age : null;
}

/**
 * Возраст для СТУПЕНИ В СПРАВОЧНИКЕ РЕКОРДОВ — «год заплыва минус год рождения»
 * (для витрины — текущий календарный год).
 *
 * ⚠ Это НЕ возраст пловца: у себя мы считаем возраст по сезону (ageInSeason). Но ступени
 * Age N в справочнике ведёт федерация, и она проставляет их календарно — проверено на всей
 * базе рекордов (docs/data-integrity.md §13). Витрина показывает ЧУЖУЮ таблицу, поэтому
 * обязана говорить в её системе координат, иначе с сентября по декабрь покажет чужую строку.
 *
 * На сервере тем же управляет настройка RecordAgeAxis (/Admin/Settings); витрина всегда
 * следует федерации, потому что просто отображает справочник.
 */
export function recordStepAge(birthYear: number | string, date: Date = new Date()): number | null {
  const year = Number(birthYear);
  if (!year || year < 1900 || year > 2100) return null;

  const age = date.getFullYear() - year;
  return age > 0 ? age : null;
}

/**
 * Подпись сезона для UI: 2025 → «25-26». Сезон идёт через границу года, поэтому одним
 * числом его не назвать: старт 31/10/2025 и старт 15/02/2026 — ОДИН сезон.
 *
 * Единственное место формата на клиенте: до этого копии жили в club-standings («2025/26»)
 * и в my-media («2025–26»), и три разных написания в одном продукте — уже беда.
 */
export function seasonLabel(startYear: number): string {
  const short = (y: number) => String(y % 100).padStart(2, '0');
  return `${short(startYear)}-${short(startYear + 1)}`;
}
