import React from 'react';
import type { PrintSheetFit, WorkoutPrintSource, WorkoutPrintView } from '../print-types';
import { NumbersSheets } from './numbers-sheets';

/**
 * Вид печати `numbers` (хендофф дизайнера 27.11.2026, вторая версия; docs/plans/workout-print-plan.md
 * §4.3): все сеты одним книжным листом — куб во всю ширину на сет, сверху вниз в порядке выполнения;
 * дистанция — самое крупное на листе, у строки три колонки «дистанция · текст · время», общие на весь
 * лист. Доступен всегда.
 */
export const NumbersSheet: React.FC<{ source: WorkoutPrintSource; onFit?: (fit: PrintSheetFit) => void }> = ({ source, onFit }) => (
  <NumbersSheets source={source} kind="numbers" label="Numbers" onFit={onFit} />
);

export const NUMBERS_VIEW: WorkoutPrintView = {
  id: 'numbers',
  label: 'Numbers',
  orientation: 'portrait',
  Sheet: NumbersSheet,
};
