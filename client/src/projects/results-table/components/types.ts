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
}
