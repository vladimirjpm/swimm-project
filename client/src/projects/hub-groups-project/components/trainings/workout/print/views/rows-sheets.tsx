import React, { useCallback, useEffect, useMemo } from 'react';
import { sheetsCaption } from '../print-fit';
import { toSheetModel } from '../print-sheet-model';
import type { PrintSheetFit, WorkoutPrintSource } from '../print-types';
import { fitRowsSheet, type RowsFit } from '../rows-fit-measure';
import { useProbeFit } from '../use-probe-fit';
import { rowsPages, type RowsViewKind } from './rows-pages';
import { RowsSheetFrame } from './rows-sheet-frame';
import '../workout-print.css';
import './rows-sheet.css';

/**
 * Листы видов `rows` и `two-sides` (хендофф 27.11): один «лист строк» или два (лицо / оборот).
 * Шрифт подбирается ДЛЯ КАЖДОГО листа отдельно (`fitRowsSheet` по скрытой копии); подпись в окне —
 * «Rows · 29 pt», «Two sides · 40 pt / 45 pt»; «не влезло» — если не влез хоть один лист.
 */

const sameFits = (a: RowsFit[] | null, b: RowsFit[] | null) =>
  a === b ||
  (!!a && !!b && a.length === b.length &&
    a.every((x, i) => x.size === b[i].size && x.overflow === b[i].overflow && x.below.join() === b[i].below.join()));

export const RowsSheets: React.FC<{
  source: WorkoutPrintSource;
  kind: RowsViewKind;
  label: string;
  onFit?: (fit: PrintSheetFit) => void;
}> = ({ source, kind, label, onFit }) => {
  const model = useMemo(() => toSheetModel(source), [source]);
  const pages = useMemo(() => rowsPages(model, kind) ?? [], [model, kind]);
  const keys = useMemo(() => pages.map((p) => p.key), [pages]);
  const renderProbe = useCallback(
    (key: string) => <RowsSheetFrame model={model} page={pages.find((p) => p.key === key)!} probe />,
    [model, pages],
  );
  const measure = useCallback(
    (get: (key: string) => HTMLElement | null) => {
      const fits = pages.map((p) => {
        const el = get(p.key);
        return el ? fitRowsSheet(el) : null;
      });
      return fits.every((f): f is RowsFit => f != null) ? fits : null;
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
        <RowsSheetFrame
          key={page.key}
          model={model}
          page={page}
          size={result?.[i]?.size}
          below={result?.[i] ? new Set(result[i].below) : undefined}
          overflow={result?.[i]?.overflow}
        />
      ))}
    </>
  );
};
