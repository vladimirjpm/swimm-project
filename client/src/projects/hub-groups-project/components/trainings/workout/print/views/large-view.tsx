import React, { useCallback, useEffect, useMemo } from 'react';
import { layoutCaption } from '../print-fit';
import { toSheetModel } from '../print-sheet-model';
import type { PrintSheetFit, WorkoutPrintSource, WorkoutPrintView } from '../print-types';
import { sheetVariants, type SheetVariant } from '../sheet-fit-measure';
import { useSheetFit } from '../use-sheet-fit';
import { LargeSheetFrame } from './large-sheet-frame';
import '../workout-print.css';
import './large-view.css';

/**
 * Вид печати `large` (docs/plans/workout-print-plan.md §3): один уровень на одном книжном листе A4
 * максимально крупным шрифтом. Раскладка — «главный справа» (§3.3) или «кубы со стрелками» (§3.4),
 * какая даёт шрифт крупнее по правилу `chooseLayout` (порог 14 pt, В16); подбор — `useSheetFit`.
 */

export const LargeSheet: React.FC<{ source: WorkoutPrintSource; onFit?: (fit: PrintSheetFit) => void }> = ({ source, onFit }) => {
  const model = useMemo(() => toSheetModel(source), [source]);
  const variants = useMemo(() => sheetVariants(model), [model]);
  const renderProbe = useCallback((v: SheetVariant) => <LargeSheetFrame model={model} variant={v} />, [model]);
  const { choice, probe } = useSheetFit(variants, renderProbe, model);

  useEffect(() => {
    if (choice) onFit?.({ caption: layoutCaption(choice), overflow: choice.overflow });
  }, [choice, onFit]);

  const variant: SheetVariant | null = !choice
    ? null
    : choice.layout === 'main'
      ? { layout: 'main' }
      : { layout: 'cubes', columns: choice.columns };
  return (
    <>
      {probe}
      <LargeSheetFrame model={model} variant={variant} size={choice?.size} overflow={choice?.overflow} />
    </>
  );
};

export const LARGE_VIEW: WorkoutPrintView = {
  id: 'large',
  label: 'Large',
  orientation: 'portrait',
  Sheet: LargeSheet,
};
