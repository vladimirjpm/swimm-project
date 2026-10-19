import type { SeasonBestModules } from './season-best-modules';
import { SEASON_ADULT_AGE } from '../../utils/helpers/season-helper';

/**
 * Модель фильтров страницы `/season-best` и её мелкие подписи.
 *
 * Жила внутри `components/sb-filters.tsx`, пока панель была своя; после Ф4 панель общая с
 * results, а модель осталась — она про ЭТУ страницу: срез, который несёт адрес.
 */
export interface SbFilters {
  season: number | null;
  stroke: string | null;
  distance: string | null;
  /** '25m' | '50m' | null — null значит «оба бассейна в одной выборке». */
  poolType: string | null;
  gender: 'male' | 'female' | null;
  /** Возраст В СЕЗОНЕ. */
  age: number | null;
  /** Верхняя граница возраста; задана только у «18+» (взрослые в обычных стартах). */
  ageTo: number | null;
  /**
   * Возрастная ГРУППА мастерского протокола («25-29»). Задана — срез идёт по мастерским
   * стартам: у них свои соревнования и свой круг ровесников, и смешивать их с юниорскими
   * нельзя. Отсюда правило: группа задана ⇒ мастерский режим, снята ⇒ обычный.
   */
  ageGroup: string | null;
  clubId: number | null;
  /** true — по одному лучшему заплыву на пловца; false — все заплывы подряд. */
  bestPerSwimmer: boolean;
}

/** «individual_medley» → «individual medley»: ключ стиля приходит машинным. */
export const strokeLabel = (stroke: string) => stroke.replace(/_/g, ' ');

/**
 * Имя клуба для показа — как в протоколе (решение Влада 2026-08-26): строка списка тоже
 * печатает оригинал, и два написания одного клуба на одном экране читались бы как два клуба.
 * Латинский вариант доступен флагом `latinNames`, тем же, что у имён пловцов.
 */
export const clubLabel = (name?: string | null, nameEn?: string | null, latin = false) =>
  ((latin && nameEn && nameEn.trim()) ? nameEn : name) ?? '';

/** Возраста, доступные в один тап: по году — до конца Juniors (17), дальше хвост «18+». */
export const SB_AGES = [8, 9, 10, 11, 12, 13, 14, 15, 16, 17];

/**
 * Кнопка «18+» — взрослые в ОБЫЧНЫХ (не мастерских) стартах. После Juniors немастерские
 * старты — одна категория Adults, поэтому и круг один (решение Влада 19.10.2026, Р84; до
 * того хвост был «21+», а 18–20 шли по году). Граница общая с местом на странице пловца и
 * с бейджем SB — `SEASON_ADULT_AGE` / серверный `SeasonMath.AdultPeerAge`.
 */
export const SB_AGE_ADULT = `${SEASON_ADULT_AGE}+`;
export const SB_ADULT_FROM = SEASON_ADULT_AGE;
export const SB_ADULT_TO = 99;

/**
 * Возраст из адреса → срез страницы. Любой возраст от 18 (одиночный `age=24` из старой
 * ссылки со страницы пловца или прежний хвост `age=21&age_to=99`) — это взрослый круг
 * «18+»: отдельного списка «только 24-летние» у нас больше нет (Р84).
 */
export function normalizeAdultAge(
  age: number | null,
  ageTo: number | null,
): { age: number | null; ageTo: number | null } {
  if (age == null || age < SEASON_ADULT_AGE) return { age, ageTo };
  return { age: SB_ADULT_FROM, ageTo: SB_ADULT_TO };
}

export type { SeasonBestModules };
