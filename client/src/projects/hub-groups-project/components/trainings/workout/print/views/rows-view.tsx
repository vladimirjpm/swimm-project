import React from 'react';
import type { PrintSheetFit, WorkoutPrintSource, WorkoutPrintView } from '../print-types';
import { RowsSheets } from './rows-sheets';

/**
 * Вид печати `rows` (хендофф дизайнера 27.11.2026, docs/plans/workout-print-plan.md §4.3): все сеты
 * одним книжным листом сверху вниз в порядке выполнения, у строки три колонки «дистанция · текст ·
 * время», общие на весь лист. Доступен всегда.
 */
export const RowsSheet: React.FC<{ source: WorkoutPrintSource; onFit?: (fit: PrintSheetFit) => void }> = ({ source, onFit }) => (
  <RowsSheets source={source} kind="rows" label="Rows" onFit={onFit} />
);

export const ROWS_VIEW: WorkoutPrintView = {
  id: 'rows',
  label: 'Rows',
  orientation: 'portrait',
  Sheet: RowsSheet,
};
