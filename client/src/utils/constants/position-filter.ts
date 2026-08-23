import { TOP_N_POSITIONS } from './filter-constants';

/** Значения фильтра Position. Один словарь на сайдбар и на полосу выбранных фильтров. */
export type PositionFilterValue = 'all' | 'top' | 'podium';

export interface PositionOption {
  value: PositionFilterValue;
  label: string;
}

export const POSITION_OPTIONS: PositionOption[] = [
  { value: 'all', label: 'All' },
  { value: 'top', label: `Top ${TOP_N_POSITIONS}` },
  { value: 'podium', label: '🥇🥈🥉 1-2-3' },
];

export const getPositionLabel = (value: PositionFilterValue): string =>
  POSITION_OPTIONS.find((o) => o.value === value)?.label ?? 'All';

/** Клик по чипу Position прокручивает значения по кругу: all → top → podium → all. */
export const nextPositionValue = (value: PositionFilterValue): PositionFilterValue => {
  const i = POSITION_OPTIONS.findIndex((o) => o.value === value);
  return POSITION_OPTIONS[(i + 1) % POSITION_OPTIONS.length].value;
};
