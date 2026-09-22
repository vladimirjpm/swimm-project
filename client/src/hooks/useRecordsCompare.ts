/**
 * Данные страницы `/records/compare`: две страны по всем дисциплинам
 * (`GET /api/records/compare`, этап 11.3.1) и список стран для выбора сторон
 * (`GET /api/records/countries`).
 *
 * Ответы кэшируются на сервере сутки и отдаются с ETag, поэтому своего кэша тут нет.
 */
import { useEffect, useState } from 'react';
import type { RecordGender } from '../utils/routes';

export interface RecordCompareSide {
  time: string;
  time_ms: number;
  holder_name?: string | null;
  /** Дата рекорда: без неё «медленнее» читается как «слабее», хотя может значить «старше». */
  record_date?: string | null;
  issue_reason?: string | null;
}

/** `no_data` — рекорда нет у одной из сторон. **Это не «медленнее»** (правило 11.3.3). */
export type RecordCompareOutcome = 'no_data' | 'a' | 'b' | 'tie';

export interface RecordCompareRow {
  style: string;
  distance: string;
  gender: string;
  pool_type: string;
  a?: RecordCompareSide | null;
  b?: RecordCompareSide | null;
  outcome: RecordCompareOutcome;
  /** Разница по модулю; null — сравнивать нечего. */
  delta_ms?: number | null;
}

export interface RecordCompareScore {
  /** Дисциплин, где есть ОБЕ стороны. Знаменатель счёта. */
  compared: number;
  a: number;
  b: number;
  tie: number;
  /** Дисциплин только у A — в победы не идут. */
  a_only: number;
  b_only: number;
}

export interface RecordCompareResponse {
  a: string;
  b: string;
  pool_type?: string | null;
  gender?: string | null;
  score: RecordCompareScore;
  rows: RecordCompareRow[];
}

export interface RecordCountryOption {
  code: string;
  records: number;
}

export interface RecordsCompareState {
  data: RecordCompareResponse | null;
  loading: boolean;
  error: string | null;
}

export interface RecordsCompareParams {
  a: string | null;
  b: string | null;
  poolType: '25m' | '50m' | null;
  gender: RecordGender | null;
}

/** Сравнение. Пока не выбраны обе стороны — запроса нет: у сервера они обязательны. */
export function useRecordsCompare(p: RecordsCompareParams): RecordsCompareState {
  const { a, b, poolType, gender } = p;
  const ready = Boolean(a && b && a !== b);

  const [state, setState] = useState<RecordsCompareState>({
    data: null, loading: ready, error: null,
  });

  useEffect(() => {
    if (!ready) {
      setState({ data: null, loading: false, error: null });
      return;
    }

    // Гонка ответов: смена стороны могла бы показать чужое сравнение под новой парой.
    let alive = true;
    setState((s) => ({ ...s, loading: true, error: null }));

    const params = new URLSearchParams({ a: a!, b: b! });
    if (poolType) params.set('pool', poolType);
    if (gender) params.set('gender', gender);

    fetch(`/api/records/compare?${params.toString()}`, { credentials: 'same-origin' })
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
      .then((data: RecordCompareResponse) => {
        if (alive) setState({ data, loading: false, error: null });
      })
      .catch((e: Error) => {
        if (alive) setState({ data: null, loading: false, error: e.message });
      });

    return () => { alive = false; };
  }, [ready, a, b, poolType, gender]);

  return state;
}

/** Страны, у которых в справочнике есть рекорды, — для выбора сторон. */
export function useRecordCountries(): RecordCountryOption[] {
  const [countries, setCountries] = useState<RecordCountryOption[]>([]);

  useEffect(() => {
    let alive = true;
    fetch('/api/records/countries', { credentials: 'same-origin' })
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
      .then((data: RecordCountryOption[]) => { if (alive) setCountries(data); })
      // Список не критичен: без него страница всё равно работает по адресу с парой.
      .catch(() => { if (alive) setCountries([]); });
    return () => { alive = false; };
  }, []);

  return countries;
}
