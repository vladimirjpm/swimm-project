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
 * Подпись сезона для UI: 2025 → «2025/26». Сезон идёт через границу года, поэтому одним
 * числом его не назвать: старт 31/10/2025 и старт 15/02/2026 — ОДИН сезон.
 *
 * Единственное место формата на клиенте: копии жили в club-standings («2025/26») и в
 * my-media («2025–26»), и три разных написания в одном продукте — уже беда.
 *
 * ⚠ Формат обязан совпадать с серверным `SeasonMath.Label` (решение Влада 2026-08-25):
 * один и тот же сезон приходил как «2025/26» с сервера и рисовался как «25-26» на клиенте,
 * и на стыке экранов это читалось как разные сезоны. Меняешь здесь — меняй и там.
 *
 * Исключение ровно одно и оно осознанное: колесо сезонов (`deep/season-carousel`) рисует
 * «25/26» — это циферблат в слоте 52px, полная запись туда не влезает (макет 4c §4).
 * Читаемая подпись у него есть в `aria-label` и она полная.
 */
export function seasonLabel(startYear: number): string {
  const short = (y: number) => String(y % 100).padStart(2, '0');
  return `${startYear}/${short(startYear + 1)}`;
}

/**
 * Подпись круга сверстников: «girls 12», «men 21+», «masters 45-49». Та же формула, что у
 * серверного `SwimmerPageBuilder` — и единственная на клиенте: подпись появляется уже на трёх
 * экранах (`/season-best`, панель Season best, дельты личников), и три копии одних и тех же
 * существительных разошлись бы на первой же правке.
 *
 * Три оси возраста дают три разные подписи: год («girls 12»), взрослый хвост («men 21+») и
 * мастерская группа («masters 45-49»), где пол в подпись не идёт — группа уже сама себе круг.
 */
export function peerGroupLabel(input: {
  age?: number | null;
  /** Задана — возрастной хвост «21+» (верхняя граница есть только у него). */
  ageTo?: number | null;
  /** Возрастная группа мастерского протокола («45-49»). */
  ageGroup?: string | null;
  gender?: 'male' | 'female' | null;
}): string | null {
  if (input.ageGroup) return `masters ${input.ageGroup}`;
  const { age, gender } = input;
  if (age == null) return null;
  const label = input.ageTo != null ? `${age}+` : String(age);
  if (gender == null) return `age ${label}`;
  const adult = age >= 18;
  const noun = gender === 'female' ? (adult ? 'women' : 'girls') : (adult ? 'men' : 'boys');
  return `${noun} ${label}`;
}

/**
 * Заметка витрины «новый сезон уже идёт, но season best откроется после зимнего чемпионата»
 * (`season_notice` в ответах API, docs/season-boundary-rule.md).
 *
 * Приходит ТОЛЬКО в окне между началом календарного сезона и последним зимним чемпионатом;
 * вне окна сервер шлёт null, и витрины о границе молчат.
 */
export interface ShowcaseSeasonNotice {
  /** Сезон, который витрина показывает вместо нового. */
  showing_season: number;
  showing_label: string;
  /** Сезон, который уже идёт по календарю, но ещё не открыт. */
  pending_season: number;
  pending_label: string;
  /**
   * dd/MM/yyyy ближайшего зимнего чемпионата ждущего сезона; null — расписания ещё нет.
   * ⚠ Это НЕ дата переключения витрины: переключает её ПОСЛЕДНИЙ чемпионат всех ступеней.
   */
  winter_starts: string | null;
}

/**
 * Что сказать пользователю про сезон, на котором он стоит:
 *  • `holding` — витрина показывает прошлый сезон, потому что новый ещё не открыт;
 *  • `pending` — он сам выбрал новый сезон, и данных там пока нет и не будет до чемпионата;
 *  • `null`   — объяснять нечего (сезон открыт или выбран какой-то третий, старый).
 *
 * `season === null` значит «сезон выбрала витрина» — это всегда показываемый (`holding`).
 */
export function showcaseNoticeKind(
  notice: ShowcaseSeasonNotice | null | undefined,
  season?: number | null,
): 'holding' | 'pending' | null {
  if (!notice) return null;
  if (season == null || season === notice.showing_season) return 'holding';
  if (season === notice.pending_season) return 'pending';
  return null;
}

/**
 * Готовая фраза заметки — ОДНА на все витрины season best. Держим её строкой, а не разметкой,
 * потому что она нужна и как текст плашки, и как нативный тултип у плитки шапки клуба; две
 * формулировки одного факта разошлись бы на первой правке.
 *
 * Дату называем «когда начинаются чемпионаты», а не «когда откроется»: переключает витрину
 * ПОСЛЕДНИЙ зимний чемпионат всех ступеней, а они плывут врозь (мастерс в январе, возрастные
 * в феврале) — точного дня мы не знаем и не обещаем.
 */
export function showcaseNoticeText(
  notice: ShowcaseSeasonNotice | null | undefined,
  season?: number | null,
): string | null {
  const kind = showcaseNoticeKind(notice, season);
  if (!kind || !notice) return null;

  const when = notice.winter_starts ? ` (they start ${notice.winter_starts})` : '';

  return kind === 'holding'
    ? `Season ${notice.pending_label} has started, but season bests open only after the winter `
      + `championships${when} — showing ${notice.showing_label} until then.`
    : `Season bests for ${notice.pending_label} open after the winter championships${when}. `
      + `Until then the season on show is ${notice.showing_label}.`;
}
