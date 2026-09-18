/**
 * Справочник рекордов ОДНОГО региона и категории — для табов `/records` «World records» и
 * «Masters» (`GET /api/records?region=…&category=…`).
 *
 * ⚠ Не путать с `useRecordsRanking`: тот тянет срез ПОПЕРЁК стран по одной дисциплине, а
 * здесь — наоборот, все дисциплины одного региона. Фильтрацию по дисциплине делает экран:
 * справочник небольшой (мир open — 86 строк, мастерсы мира — 1095, Израиля — 845), кэшируется
 * сервером сутки и отдаётся с ETag, поэтому перезапрашивать его на каждый клик по фильтру
 * незачем.
 *
 * `enabled` нужен, чтобы табы, которые не открывали, не ходили в сеть: у страницы три таба,
 * а смотрят обычно один.
 */
import { useEffect, useState } from 'react';

export interface RegionRecord {
  region_type: string;
  region_code: string;
  category: string;
  /** Ступень: возраст у `age`, полоса «25-29» у `masters`, пусто у `open`. */
  age_key: string;
  gender: string;
  pool_type: string;
  style: string;
  /** Как в справочнике: «50m», «4X100m». */
  distance: string;
  time: string;
  holder_name?: string | null;
  /** Латиница держателя — есть у тех, чьё имя источник отдаёт латиницей (World Aquatics). */
  holder_name_en?: string | null;
  holder_country?: string | null;
  record_date?: string | null;
  /** Открытая претензия к записи справочника; null — не оспаривается. */
  issue_reason?: string | null;
}

export interface RegionRecordsState {
  data: RegionRecord[] | null;
  loading: boolean;
  error: string | null;
}

export function useRegionRecords(
  region: string,
  category: string,
  enabled: boolean,
): RegionRecordsState {
  const [state, setState] = useState<RegionRecordsState>({ data: null, loading: false, error: null });

  useEffect(() => {
    if (!enabled) return;

    let cancelled = false;
    setState((s) => ({ ...s, loading: true, error: null }));

    const query = new URLSearchParams({ region, category }).toString();
    fetch(`/api/records?${query}`)
      .then((r) => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return r.json() as Promise<RegionRecord[]>;
      })
      .then((data) => { if (!cancelled) setState({ data, loading: false, error: null }); })
      .catch((e: Error) => { if (!cancelled) setState({ data: null, loading: false, error: e.message }); });

    return () => { cancelled = true; };
  }, [region, category, enabled]);

  return state;
}
