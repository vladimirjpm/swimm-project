/**
 * Модули страницы `/season-best` — один флаг на блок.
 *
 * Требование Влада: «всё модульно, всё можно легко включить/выключить». Держим флаги в
 * ОДНОМ месте, а не по компонентам: выключить колонку или целый блок должно быть правкой
 * одной строки здесь, без похода по вёрстке.
 *
 * Правило добавления: новый блок появляется вместе со своим флагом. Компонент читает флаг
 * пропом, а не импортом, — тогда его можно переиспользовать с другим набором.
 */
export interface SeasonBestModules {
  /** Общая шапка сайта. */
  topbar: boolean;
  /** Полоса-карусель сезонов над списком. */
  seasonCarousel: boolean;
  /** Шапка фильтров — полоса чипов с выбранными значениями (аналог ResultsFilteredInfo). */
  filterBar: boolean;
  /** Сайдбар фильтров (на мобайле — раскрывающийся блок). */
  filters: boolean;
  /** Карточка фильтра «Club». */
  filterClub: boolean;
  /** Тумблер «все заплывы / лучший на пловца». */
  bestPerSwimmerToggle: boolean;

  /** Показывать имя пловца латиницей (`name_en`), если оно есть.
   *  ВЫКЛЮЧЕНО: остальной продукт (таблица результатов, страница пловца) показывает имя
   *  ровно так, как оно напечатано в протоколе, и латиница здесь мешала бы узнать того же
   *  человека при переходе между экранами. */
  latinNames: boolean;
  /** Мягкая заливка строки по полу — как в таблице результатов
   *  (`Helper.getGenderBgClass` → `--theme-mode-row-male/female`). */
  genderTint: boolean;
  /** Клуб в строке. */
  clubInRow: boolean;
  /** Эмблема клуба слева от имени (рисует `UI_SwimmerNameCell`). У клубов без своего файла
   *  подставляется no-club.png — таких большинство, но ряд от этого не рассыпается. */
  clubLogoInRow: boolean;
  /** Название соревнования в строке. */
  meetInRow: boolean;
  /** Дата заплыва в строке. */
  dateInRow: boolean;
  /** Очки FINA в строке. */
  pointsInRow: boolean;
  /** Отставание от лидера в строке. */
  gapInRow: boolean;
  /** Номер попытки у повторных заплывов одного пловца («2nd swim»). */
  attemptInRow: boolean;

  /** Строка-оговорка под списком: места считаются среди импортированного. */
  footerNote: boolean;
}

export const SEASON_BEST_MODULES: SeasonBestModules = {
  topbar: true,
  seasonCarousel: true,
  filterBar: true,
  filters: true,
  filterClub: true,
  bestPerSwimmerToggle: true,

  latinNames: false,
  genderTint: true,
  clubInRow: true,
  clubLogoInRow: true,
  meetInRow: true,
  dateInRow: true,
  pointsInRow: true,
  gapInRow: true,
  attemptInRow: true,

  footerNote: true,
};

export default SEASON_BEST_MODULES;
