import { Result } from '../../../utils/interfaces/results';
import { NormativeLevelInfo } from '../../../utils/interfaces/normative-level-info';
import { FilterSelected } from '../../../utils/interfaces/filter-selected';

export interface ResultsTableRowProps {
  res: Result;
  index: number;
  showClub: boolean;
  showEvent: boolean;
  showDate: boolean;
  showAge: boolean;
  showPoolType: boolean;
  hasInternationalPoints: boolean;
  clubPoints?: number;
  levelInfo: NormativeLevelInfo;
  updateFilter: (filter: Partial<FilterSelected>) => void;
  isMastersResult?: boolean;
  isAwardSource?: boolean;
  isRecordHolder?: boolean;
  isRecordTime?: boolean;
  /** Пловец является primary-фаворитом текущего пользователя */
  isPrimaryFavorite?: boolean;
  /** Пловец в избранном (не обязательно primary) */
  isFavorite?: boolean;
  /** Колбэк для переключения избранного/primary */
  onToggleFavorite?: (swimmerId: number) => void;
  onTogglePrimary?: (swimmerId: number) => void;
  /** Мобильный вид: развёрнута ли нижняя строка (level/pts/date) */
  isExpanded?: boolean;
  /** Мобильный вид: переключить развёрнутость нижней строки */
  onToggleExpand?: () => void;
}

/** Флаги видимости колонок таблицы результатов. */
export interface ResultsGridFlags {
  showClub: boolean;
  showEvent: boolean;
  showPoolType: boolean;
  showDate: boolean;
  hasInternationalPoints: boolean;
}

/**
 * Единый gridTemplateColumns для хедера И строк результатов (как в прототипе).
 * Порядок: FAV · POS · SWIMMER · [CLUB] · [STYLE] · TIME · LEVEL · [PTS].
 * Иконка клуба — отдельной колонкой после имени; название клуба остаётся текстом
 * под именем. FAV — ведущая колонка сердечка/звезды (всегда зарезервирована для
 * выравнивания, пустая для неавторизованных). Хедер и строки ОБЯЗАНЫ рендерить
 * ячейки в этом же порядке с теми же условиями.
 */
export function buildResultsGridTemplate(f: ResultsGridFlags): string {
  // Детали теперь попапом → таблица во всю ширину, поэтому фикс-ширины (как в
  // прототипе). Порядок: FAV · POS · SWIMMER · [CLUB] · [STYLE] · TIME · LEVEL · [PTS].
  const cols: string[] = ['28px', '60px', 'minmax(0,1fr)'];        // FAV, POS, SWIMMER
  if (f.showClub) cols.push('44px');                               // CLUB (иконка)
  if (f.showEvent || f.showPoolType) cols.push('96px');            // STYLE (иконка)
  cols.push('112px');                                              // TIME
  cols.push('96px');                                               // LEVEL (gauge)
  if (f.hasInternationalPoints) cols.push('64px');                 // PTS
  return cols.join(' ');
}
