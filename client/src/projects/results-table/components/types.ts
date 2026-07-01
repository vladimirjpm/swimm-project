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
 * Единый gridTemplateColumns для хедера И строк результатов — чтобы колонки
 * (условные: club/style/date/pts) всегда были выровнены. Порядок:
 * POS · SWIMMER · [CLUB] · [STYLE] · TIME · LEVEL · [DATE] · [PTS].
 * Хедер и строки ОБЯЗАНЫ рендерить ячейки в этом же порядке с теми же условиями.
 */
export function buildResultsGridTemplate(f: ResultsGridFlags): string {
  const cols: string[] = ['56px', 'minmax(0,1fr)']; // POS, SWIMMER
  if (f.showClub) cols.push('auto');                 // CLUB
  if (f.showEvent || f.showPoolType) cols.push('minmax(90px,auto)'); // STYLE
  cols.push('auto');                                 // TIME
  cols.push('88px');                                 // LEVEL (gauge)
  if (f.showDate) cols.push('auto');                 // DATE
  if (f.hasInternationalPoints) cols.push('62px');   // PTS
  return cols.join(' ');
}
