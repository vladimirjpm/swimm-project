import React, { useCallback, useEffect, useMemo } from 'react';
import { fitNumbersSheet, type NumbersFit } from '../numbers-fit-measure';
import { sheetsCaption } from '../print-fit';
import { toSheetModel } from '../print-sheet-model';
import type { PrintSheetFit, WorkoutPrintSource } from '../print-types';
import { useProbeFit } from '../use-probe-fit';
import { numbersPages, type NumbersViewKind } from './numbers-pages';
import { NumbersSheetFrame } from './numbers-sheet-frame';
import '../workout-print.css';
import './numbers-sheet.css';

/**
 * Листы видов `numbers` и `numbers-two-sides` (хендофф 27.11 вторая версия): один «лист кубов» или
 * два (лицо / оборот). Шрифт подбирается ДЛЯ КАЖДОГО листа отдельно (`fitNumbersSheet` по скрытой
 * копии); подпись в окне — «Numbers · 34 pt», «Two sides · 50 pt / 57 pt»; «не влезло» — если не
 * влез хоть один лист.
 */

const sameFits = (a: NumbersFit[] | null, b: NumbersFit[] | null) =>
  a === b ||
  (!!a && !!b && a.length === b.length &&
    a.every((x, i) => x.size === b[i].size && x.overflow === b[i].overflow && x.below.join() === b[i].below.join()));

export const NumbersSheets: React.FC<{
  source: WorkoutPrintSource;
  kind: NumbersViewKind;
  label: string;
  onFit?: (fit: PrintSheetFit) => void;
}> = ({ source, kind, label, onFit }) => {
  const model = useMemo(() => toSheetModel(source), [source]);
  const pages = useMemo(() => numbersPages(model, kind) ?? [], [model, kind]);
  const keys = useMemo(() => pages.map((p) => p.key), [pages]);
  const renderProbe = useCallback(
    (key: string) => <NumbersSheetFrame model={model} page={pages.find((p) => p.key === key)!} kind={kind} probe />,
    [model, pages, kind],
  );
  const measure = useCallback(
    (get: (key: string) => HTMLElement | null) => {
      const fits = pages.map((p) => {
        const el = get(p.key);
        return el ? fitNumbersSheet(el) : null;
      });
      return fits.every((f): f is NumbersFit => f != null) ? fits : null;
    },
    [pages],
  );
  const { result, probe } = useProbeFit(keys, renderProbe, measure, sameFits, pages);

  useEffect(() => {
    if (result && result.length > 0) {
      onFit?.({ caption: sheetsCaption(label, result.map((r) => r.size)), overflow: result.some((r) => r.overflow) });
    }
  }, [result, label, onFit]);

  return (
    <>
      {probe}
      {pages.map((page, i) => (
        <NumbersSheetFrame
          key={page.key}
          model={model}
          page={page}
          kind={kind}
          size={result?.[i]?.size}
          below={result?.[i] ? new Set(result[i].below) : undefined}
          overflow={result?.[i]?.overflow}
        />
      ))}
    </>
  );
};
