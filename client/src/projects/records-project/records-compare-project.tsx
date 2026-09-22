import React, { useCallback, useEffect, useMemo, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './records-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import UI_FlagEmoji from '../components/mix/flag-icon/flag-icon';
import { parseRecordsCompareQuery, routes, type RecordGender } from '../../utils/routes';
import { useRecordCountries, useRecordsCompare } from '../../hooks/useRecordsCompare';
import RcScoreCard from './components/rc-score-card';
import RcTable from './components/rc-table';
import { HOME_REGION } from './rk-disciplines';

/**
 * Страница `/records/compare` — сравнение двух стран по рекордам (этап 11.3.2).
 *
 * Данные — `GET /api/records/compare` (11.3.1). Главное правило экрана вынесено в 11.3.3 и
 * держится тут в трёх местах сразу: дисциплина без рекорда у одной из сторон остаётся
 * ВИДИМОЙ строкой с прочерком и подписью «no record», в счёт не идёт ни в чью пользу, а
 * сколько таких дисциплин — написано прямо под счётом.
 *
 * Пара сторон живёт в query (`?a=ISR&b=USA`), разрез — там же. Сегмент `compare` в пути, как
 * `/groups/{slug}/results`: это другой ЭКРАН тех же данных, а не идентичность ресурса.
 */

const POOLS: Array<{ key: '25m' | '50m' | null; label: string }> = [
  { key: null, label: 'Both pools' },
  { key: '50m', label: '50m' },
  { key: '25m', label: '25m' },
];

const GENDERS: Array<{ key: RecordGender | null; label: string }> = [
  { key: null, label: 'All' },
  { key: 'male', label: 'Men' },
  { key: 'female', label: 'Women' },
  // Смешанные эстафеты (Э5, records-relays-plan): при нём в сравнении остаются только они.
  { key: 'mixed', label: 'Mixed' },
];

interface CompareFilters {
  a: string | null;
  b: string | null;
  poolType: '25m' | '50m' | null;
  gender: RecordGender | null;
}

function RecordsCompareProject() {
  useTheme();
  const { mode } = useMode();
  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';

  const query = useMemo(() => parseRecordsCompareQuery(), []);
  const countries = useRecordCountries();

  // Левая сторона по умолчанию домашняя, правая пустая: «с кем сравнить» — это и есть
  // вопрос экрана, и подставлять за человека вторую страну значит отвечать за него.
  const [filters, setFilters] = useState<CompareFilters>(() => ({
    a: query.a ?? HOME_REGION,
    b: query.b,
    poolType: query.poolType,
    gender: query.gender,
  }));

  useEffect(() => {
    const url = new URL(window.location.href);
    const set = (key: string, value: string | null) => {
      if (!value) url.searchParams.delete(key);
      else url.searchParams.set(key, value);
    };
    set('a', filters.a);
    set('b', filters.b);
    set('pool', filters.poolType);
    set('gender', filters.gender);
    window.history.replaceState(null, '', url.toString());
  }, [filters]);

  const compare = useRecordsCompare(filters);
  const patch = useCallback(
    (next: Partial<CompareFilters>) => setFilters((f) => ({ ...f, ...next })),
    [],
  );

  /** Обмен сторонами: ответ обязан стать зеркальным, и это проверяется тестами API. */
  const swap = useCallback(
    () => setFilters((f) => ({ ...f, a: f.b, b: f.a })),
    [],
  );

  const data = compare.data;

  const picker = (side: 'a' | 'b') => (
    <label className="rc-picker">
      <span className="rc-picker__label">{side === 'a' ? 'Country A' : 'Country B'}</span>
      <span className="rc-picker__control">
        {filters[side] && (
          <UI_FlagEmoji countryCode={filters[side]!} size="24x18" className="src-records-compare-project" />
        )}
        <select
          className="rc-select"
          value={filters[side] ?? ''}
          onChange={(e) => patch({ [side]: e.target.value || null } as Partial<CompareFilters>)}
        >
          <option value="">— pick a country —</option>
          {countries.map((c) => (
            <option key={c.code} value={c.code}>
              {c.code} ({c.records})
            </option>
          ))}
        </select>
      </span>
    </label>
  );

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar active="records" />

      <main className="rk-page">
        <div className="rk-topline">
          <div>
            <h1 className="rk-head__title">Records head-to-head</h1>
            <div className="rk-head__sub">
              two countries across every event ·{' '}
              <a className="rc-link" href={routes.records()}>back to the ranking</a>
            </div>
          </div>
          <UI_ModeToggle />
        </div>

        <div className="rc-controls">
          {picker('a')}
          <button type="button" className="rc-swap" onClick={swap} title="Swap sides">⇄</button>
          {picker('b')}

          <div className="rc-cuts">
            <div className="rk-chips">
              {POOLS.map((p) => (
                <button
                  key={p.label}
                  type="button"
                  className={`rk-chip${filters.poolType === p.key ? ' rk-chip--on' : ''}`}
                  aria-pressed={filters.poolType === p.key}
                  onClick={() => patch({ poolType: p.key })}
                >
                  {p.label}
                </button>
              ))}
            </div>
            <div className="rk-chips">
              {GENDERS.map((g) => (
                <button
                  key={g.label}
                  type="button"
                  className={`rk-chip${filters.gender === g.key ? ' rk-chip--on' : ''}`}
                  aria-pressed={filters.gender === g.key}
                  onClick={() => patch({ gender: g.key })}
                >
                  {g.label}
                </button>
              ))}
            </div>
          </div>
        </div>

        {!filters.a || !filters.b ? (
          <div className="rk-state">Pick two countries to compare.</div>
        ) : compare.loading ? (
          <div className="rk-state">Loading…</div>
        ) : compare.error ? (
          <div className="rk-state rk-state--error">
            Could not load the comparison ({compare.error}).
          </div>
        ) : data && data.rows.length === 0 ? (
          <div className="rk-state">
            Neither {data.a} nor {data.b} has records in this cut.
          </div>
        ) : data ? (
          <>
            <RcScoreCard a={data.a} b={data.b} score={data.score} />
            <RcTable rows={data.rows} a={data.a} b={data.b} />
          </>
        ) : null}
      </main>
    </div>
  );
}

export default RecordsCompareProject;
