/**
 * Данные страницы `/records`: рейтинг стран по одной дисциплине
 * (`GET /api/records/ranking`, этап 11.2.1).
 *
 * ⚠ Не путать с `RecordsHelper` (`utils/helpers/records-helper.ts`): тот тянет
 * `GET /api/records?region=…` — справочник ОДНОГО региона, из которого строится дуга уровня
 * в строке заплыва. Здесь — срез ПОПЕРЁК регионов, которого то API дать не может: у него
 * `region` обязателен.
 *
 * Ответ кэшируется на сервере сутки и отдаётся с ETag, поэтому своего кэша тут нет.
 */
import { useEffect, useState } from 'react';

export interface RecordsRankingRow {
  rank: number;
  region_code: string;
  time: string;
  time_ms: number;
  holder_name?: string | null;
  record_date?: string | null;
  /** Открытая претензия к записи справочника; null — не оспаривается. */
  issue_reason?: string | null;
  /** Отставание от мирового в мс; null — мирового эталона у дисциплины нет.
   *  ⚠ Может быть ОТРИЦАТЕЛЬНЫМ: рекорд страны быстрее мирового (аномалия источника). */
  behind_world_ms?: number | null;
  behind_world_percent?: number | null;
}

export interface RecordsRankingWorld {
  time: string;
  time_ms: number;
  holder_name?: string | null;
  record_date?: string | null;
  issue_reason?: string | null;
}

export interface RecordsRankingResponse {
  style: string;
  distance: string;
  gender: string;
  pool_type: string;
  total: number;
  /** Сколько строк дисциплины отброшено из-за неразобранного времени. */
  unparsed_skipped: number;
  world?: RecordsRankingWorld | null;
  rows: RecordsRankingRow[];
}

export interface RecordsRankingQuery {
  stroke: string | null;
  distance: string | null;
  gender: 'male' | 'female' | null;
  poolType: '25m' | '50m' | null;
}

export interface RecordsRankingState {
  data: RecordsRankingResponse | null;
  loading: boolean;
  error: string | null;
}

/**
 * Рейтинг дисциплины. Пока хоть одна ось не выбрана — запроса НЕТ и `data` пуст: у сервера
 * все четыре обязательны, и «ещё не выбрали» не должно выглядеть как «ничего не нашлось».
 */
export function useRecordsRanking(q: RecordsRankingQuery): RecordsRankingState {
  const { stroke, distance, gender, poolType } = q;
  const ready = Boolean(stroke && distance && gender && poolType);

  const [state, setState] = useState<RecordsRankingState>({
    data: null, loading: ready, error: null,
  });

  useEffect(() => {
    if (!ready) {
      setState({ data: null, loading: false, error: null });
      return;
    }

    // Гонка ответов: быстрый запрос по новой дисциплине мог прийти раньше медленного по
    // старой, и таблица показывала бы чужой рейтинг под новым заголовком.
    let alive = true;
    setState((s) => ({ ...s, loading: true, error: null }));

    const params = new URLSearchParams({
      style: stroke!,
      distance: distance!,
      gender: gender!,
      pool: poolType!,
    });

    fetch(`/api/records/ranking?${params.toString()}`, { credentials: 'same-origin' })
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
      .then((data: RecordsRankingResponse) => {
        if (alive) setState({ data, loading: false, error: null });
      })
      .catch((e: Error) => {
        if (alive) setState({ data: null, loading: false, error: e.message });
      });

    return () => { alive = false; };
  }, [ready, stroke, distance, gender, poolType]);

  return state;
}

export default useRecordsRanking;
