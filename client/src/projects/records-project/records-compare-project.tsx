import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './records-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { parseRecordsCompareQuery, routes, type RecordGender } from '../../utils/routes';
import { useRecordCountries, useRecordsCompare } from '../../hooks/useRecordsCompare';
import RcH2HHeader from './components/rc-h2h-header';
import RcH2HEvents from './components/rc-h2h-events';
import RcCountryPicker from './components/rc-country-picker';
import { HOME_REGION } from './rk-disciplines';

/**
 * Страница `/records/compare` — сравнение двух стран по рекордам (этап 11.3.2).
 *
 * Данные — `GET /api/records/compare` (11.3.1). Главное правило экрана вынесено в 11.3.3 и
 * держится тут в трёх местах сразу: дисциплина без рекорда у одной из сторон остаётся
 * ВИДИМОЙ строкой с прочерком и подписью «no record», в счёт не идёт ни в чью пользу, а
 * сколько таких дисциплин — написано прямо под счётом.
 *
 * Экран собран семьёй `UI_H2H*` — той же, что сравнение пловцов, включая сам способ
 * выбора: слот · счёт · слот, а под ними ОДИН пикер на обе стороны. Два селекта, стоявшие
 * здесь раньше, читались как два разных выбора, хотя выбирают одно и то же (просьба Влада
 * 23.09.2026); теперь сторону называют подсвеченный слот и подпись над пикером.
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

/** Сколько богатых рекордами стран вынести кнопками над поиском. */
const QUICK_COUNT = 6;

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

  /** Какую сторону заполнит следующий выбор — как активный слот на `/h2h`. */
  const [active, setActive] = useState<'a' | 'b'>(() => (query.a == null ? 'a' : 'b'));
  const [search, setSearch] = useState('');
  const searchRef = useRef<HTMLInputElement>(null);

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

  /**
   * Выбор страны в активную сторону. Та же страна с другой стороны — не сравнение, а
   * зеркало: стороны меняются местами, как на `/h2h`, а не ставятся дважды.
   */
  const pick = useCallback((code: string) => {
    setFilters((f) => (active === 'a'
      ? { ...f, a: code, b: f.b === code ? f.a : f.b }
      : { ...f, b: code, a: f.a === code ? f.b : f.a }));
    setActive((side) => (side === 'a' ? 'b' : 'a'));
    setSearch('');
  }, [active]);

  const clear = useCallback((side: 'a' | 'b') => {
    patch({ [side]: null } as Partial<CompareFilters>);
    setActive(side);
    setSearch('');
    // Фокус в поиск: крестик — это «выбрать другую», а не «просто убрать».
    window.setTimeout(() => searchRef.current?.focus(), 0);
  }, [patch]);

  const focusSide = useCallback((side: 'a' | 'b') => {
    setActive(side);
    searchRef.current?.focus();
  }, []);

  /**
   * Быстрые кнопки — домашняя страна и сильнейшие плавательные державы.
   *
   * ⚠ Ось — `world_records` (сколько мировых рекордов держат пловцы страны), а НЕ
   * `records`. Второе поле это ПОКРЫТИЕ справочника: у любой заметной страны там 91-92 из
   * сотни дисциплин, и сортировка по нему ставила Парагвай впереди США — список выглядел
   * случайным. Порядок списка от API алфавитный, поэтому сортировать нужно здесь.
   */
  const quick = useMemo(
    () => [
      HOME_REGION,
      ...countries
        .filter((c) => c.code !== HOME_REGION && (c.world_records ?? 0) > 0)
        .slice()
        .sort((x, y) => (y.world_records ?? 0) - (x.world_records ?? 0) || y.records - x.records)
        .slice(0, QUICK_COUNT)
        .map((c) => c.code),
    ],
    [countries],
  );

  const data = compare.data;
  const bothPicked = Boolean(filters.a && filters.b);

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

        {/* Разрез (бассейн и пол) — общий на весь экран, поэтому стоит НАД сторонами:
            он описывает, что именно сравнивается, а не кого. */}
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

        <div className="h2h-scope rc-board">
          <RcH2HHeader
            a={filters.a}
            b={filters.b}
            score={data && bothPicked ? data.score : null}
            totals={{
              a: data ? data.rows.filter((r) => r.a).length : 0,
              b: data ? data.rows.filter((r) => r.b).length : 0,
            }}
            active={active}
            onSwap={swap}
            onClear={clear}
            onFocus={focusSide}
          />

          {/* Пикер стоит в потоке под сторонами и остаётся видимым даже когда обе выбраны:
              сменить страну — обычное действие экрана, а не исключение. */}
          <div className="rc-picker-wrap">
            <div className="rc-picker-cap">
              Choosing the <strong>{active === 'a' ? 'left' : 'right'}</strong> country
            </div>
            <RcCountryPicker
              countries={countries}
              taken={[filters.a, filters.b]}
              query={search}
              onQuery={setSearch}
              onPick={pick}
              quick={quick}
              inputRef={searchRef}
            />
          </div>

          {!bothPicked ? (
            <div className="h2h-empty">Pick two countries to compare.</div>
          ) : compare.loading ? (
            <div className="h2h-empty">Loading…</div>
          ) : compare.error ? (
            <div className="rk-state rk-state--error">
              Could not load the comparison ({compare.error}).
            </div>
          ) : data && data.rows.length === 0 ? (
            <div className="h2h-empty">
              Neither {data.a} nor {data.b} has records in this cut.
            </div>
          ) : data ? (
            <RcH2HEvents rows={data.rows} genderFixed={filters.gender != null} />
          ) : null}
        </div>
      </main>
    </div>
  );
}

export default RecordsCompareProject;
