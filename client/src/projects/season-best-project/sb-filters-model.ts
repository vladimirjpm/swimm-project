import type { SeasonBestModules } from './season-best-modules';

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
  /** Верхняя граница возраста; задана только у «21+» (взрослые в обычных стартах). */
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

/** Возраста, доступные в один тап. Ровно те ступени, где у нас есть сколько-нибудь данных. */
export const SB_AGES = [8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20];

/**
 * Кнопка «21+» — взрослые в ОБЫЧНЫХ (не мастерских) стартах: их за сезон около 1300 заплывов,
 * и до правки 2026-08-26 они были видны только под «All». Отдельных кнопок 21…39 нет
 * намеренно: после 22 лет в списке единицы людей, и лестница из двух десятков почти пустых
 * ступеней читалась бы хуже, чем один хвост.
 */
export const SB_AGE_ADULT = '21+';
export const SB_ADULT_FROM = 21;
export const SB_ADULT_TO = 99;

export type { SeasonBestModules };
