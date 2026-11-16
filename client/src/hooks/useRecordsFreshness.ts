/**
 * Когда справочник рекордов сверяли с каждым источником — для подписи «checked …» на витрине
 * (`GET /api/records/freshness`, docs/plans/records-freshness-plan.md U5–U6).
 *
 * Дата ПО КАЖДОМУ источнику, не одна на страницу (решение Влада 21.09.2026): у Masters, NR и
 * World Junior разные источники и разная свежесть, а на странице пловца они смешаны в одной
 * секции — свёрнутый минимум пугал бы там, где всё свежее. Какие источники показать, решает
 * экран (`UI_RecordsChecked`).
 *
 * Ответ крошечный (пять строк), поэтому один запрос на страницу: хук держит обещание на
 * уровне модуля, и два экрана на одной странице не ходят в сеть дважды.
 */
import { useEffect, useState } from 'react';

export interface RecordSourceFreshness {
  /** Ключ источника: worldrecords, wa-masters, wa-junior, isrorg-age, isrorg-masters. */
  source: string;
  /**
   * Последняя сверка, после которой наши данные совпадают с источником (UTC): без изменений
   * или изменения применены (16.11.2026). Нашли изменения и не применили — не сдвигается.
   * null — такой сверки ещё не было.
   */
  checkedAt: string | null;
  /** Когда справочник этого источника реально менялся. */
  changedAt: string | null;
}

let pending: Promise<RecordSourceFreshness[]> | null = null;

function load(): Promise<RecordSourceFreshness[]> {
  if (!pending) {
    pending = fetch('/api/records/freshness')
      .then((r) => (r.ok ? (r.json() as Promise<RecordSourceFreshness[]>) : []))
      // Сбой подписи не должен ронять экран рекордов: нет даты — просто нет строки.
      .catch(() => []);
  }
  return pending;
}

export function useRecordsFreshness(): RecordSourceFreshness[] | null {
  const [data, setData] = useState<RecordSourceFreshness[] | null>(null);
  useEffect(() => {
    let cancelled = false;
    load().then((d) => { if (!cancelled) setData(d); });
    return () => { cancelled = true; };
  }, []);
  return data;
}
