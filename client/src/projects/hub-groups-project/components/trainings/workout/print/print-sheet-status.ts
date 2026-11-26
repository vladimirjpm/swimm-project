import { createContext, useContext, useEffect } from 'react';

/**
 * Что вид сообщает окну предпросмотра о своём листе: подпись выбранного («Main on the right · 28 pt»)
 * и признак «не влезло даже на полу шрифта» (план §3.5). Лист-заглушка молчит; вид `large` (Ш3)
 * зовёт `useReportPrintStatus` после подгонки. Вне окна отчёт никуда не идёт.
 */
export interface PrintSheetStatus {
  caption: string | null;
  overflow: boolean;
}

type Report = (status: PrintSheetStatus | null) => void;

export const PrintStatusContext = createContext<Report>(() => undefined);

export function useReportPrintStatus(status: PrintSheetStatus | null): void {
  const report = useContext(PrintStatusContext);
  const caption = status?.caption ?? null;
  const overflow = status?.overflow ?? false;
  const has = status != null;
  useEffect(() => {
    report(has ? { caption, overflow } : null);
    return () => report(null);
  }, [report, has, caption, overflow]);
}
