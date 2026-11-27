import React from 'react';
import { toSheetModel } from '../print-sheet-model';
import type { PrintSheetFit, WorkoutPrintSource, WorkoutPrintView } from '../print-types';
import { splitTwoSides } from './numbers-pages';
import { NumbersSheets } from './numbers-sheets';

/**
 * Вид печати `numbers-two-sides`, в сегменте — «Two sides» (хендофф дизайнера 27.11.2026, вторая
 * версия, §6; docs/plans/workout-print-plan.md §4.3): тот же «лист кубов», но два листа — лицо (сеты до
 * первого главного, внизу «Next ④ Main set … page 2 →») и оборот (главный и всё после). Время на ступень
 * мельче, чем в `numbers`. Рассчитан на двустороннюю печать с переворотом по длинному краю; без неё —
 * просто два листа (метки «1 / 2», «2 / 2» верны в обоих случаях). Доступен, когда у уровня есть
 * главный сет (`main`) и он не первый.
 */
export const NumbersTwoSidesSheet: React.FC<{ source: WorkoutPrintSource; onFit?: (fit: PrintSheetFit) => void }> = ({
  source, onFit,
}) => <NumbersSheets source={source} kind="numbers-two-sides" label="Two sides" onFit={onFit} />;

/** Делится ли версия уровня на две стороны: главный сет есть и перед ним хотя бы один сет. */
export const twoSidesAvailable = (source: WorkoutPrintSource) => splitTwoSides(toSheetModel(source).cubes) != null;

export const NUMBERS_TWO_SIDES_VIEW: WorkoutPrintView = {
  id: 'numbers-two-sides',
  label: 'Two sides',
  orientation: 'portrait',
  duplex: true,
  available: twoSidesAvailable,
  Sheet: NumbersTwoSidesSheet,
};
