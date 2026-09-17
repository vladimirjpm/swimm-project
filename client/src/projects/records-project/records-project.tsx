import React, { useCallback, useEffect, useMemo, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './records-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { parseRecordsQuery, routes } from '../../utils/routes';
import { useRecordsRanking } from '../../hooks/useRecordsRanking';
import RkDisciplinePicker from './components/rk-discipline-picker';
import RkWorldCard from './components/rk-world-card';
import RkTable from './components/rk-table';
import {
  HOME_REGION, RK_DEFAULT, disciplineLabel, isRelay, type RkFilters,
} from './rk-disciplines';

/**
 * Страница `/records` — рейтинг национальных рекордов (этап 11.2.2,
 * docs/plans/records-all-countries-plan.md §5).
 *
 * Показывает одну дисциплину: кто из стран быстрее и насколько отстаёт от мирового.
 * Данные — `GET /api/records/ranking` (11.2.1): места, мировой рекорд отдельным полем,
 * отставание в мс и процентах.
 *
 * Палитра deep — та же семья, что `/season-best` и страница спортсмена, откуда сюда ведут
 * ссылки. Дисциплина живёт в query и читается через `parseRecordsQuery`: у рейтинга нет
 * идентичности в пути, адресом его делает именно дисциплина (правило routes.ts).
 *
 * ⚠ Страница ходит в API НАПРЯМУЮ, минуя легаси-дерево `records-helper.ts` (11.2.3): у того
 * ключ `ISR→NR` рассчитан на одну страну, и рейтинг стран в него не ложится. Попапы
 * нормативов и возрастные карточки продолжают жить на нём — их мы не трогаем.
 */

function RecordsProject() {
  useTheme();
  const { mode } = useMode();
  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';

  const query = useMemo(() => parseRecordsQuery(), []);

  // Дефолт подставляем ЗДЕСЬ, а не в parseRecordsQuery: разбор адреса обязан отличать
  // «пользователь выбрал 50 вольным» от «мы показали 50 вольным, потому что надо же
  // что-то показать». Иначе первая же смена дефолта перепишет смысл чужих ссылок.
  const [filters, setFilters] = useState<RkFilters>(() => ({
    stroke: query.stroke ?? RK_DEFAULT.stroke,
    distance: query.distance ?? RK_DEFAULT.distance,
    gender: query.gender ?? RK_DEFAULT.gender,
    poolType: query.poolType ?? RK_DEFAULT.poolType,
    highlight: query.highlight,
  }));

  // Адрес — единственный носитель состояния: перезагрузка и «поделиться ссылкой» обязаны
  // давать тот же экран.
  useEffect(() => {
    const url = new URL(window.location.href);
    const set = (key: string, value: string | null) => {
      if (!value) url.searchParams.delete(key);
      else url.searchParams.set(key, value);
    };
    set('stroke', filters.stroke);
    set('distance', filters.distance);
    set('gender', filters.gender);
    set('pool', filters.poolType);
    set('country', filters.highlight);
    window.history.replaceState(null, '', url.toString());
  }, [filters]);

  const ranking = useRecordsRanking({
    stroke: filters.stroke,
    distance: filters.distance,
    gender: filters.gender,
    poolType: filters.poolType,
  });

  const patch = useCallback(
    (next: Partial<RkFilters>) => setFilters((f) => ({ ...f, ...next })),
    [],
  );

  const data = ranking.data;
  const title = disciplineLabel(filters);
  const home = data?.rows.find((r) => r.region_code === HOME_REGION) ?? null;

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar active="records" />

      <main className="rk-page">
        <div className="rk-topline">
          <div>
            <h1 className="rk-head__title">National records</h1>
            <div className="rk-head__sub">
              {title}
              {isRelay(filters.distance) && <span className="rk-head__tag">relay</span>}
              {' · '}
              {/* Единственный вход на `/records/compare`: в топбаре пункт один, «Records» —
                  это рейтинг. Дисциплину не переносим (там все сразу), а бассейн и пол —
                  те же оси, что и здесь. */}
              <a className="rc-link" href={routes.recordsCompare({
                poolType: filters.poolType, gender: filters.gender,
              })}>compare two countries</a>
            </div>
          </div>
          <UI_ModeToggle />
        </div>

        <RkDisciplinePicker filters={filters} onChange={patch} />

        <RkWorldCard world={data?.world} filters={filters} />

        {/* Место Израиля — отдельной строкой над таблицей: в списке из двух сотен стран
            домашнюю подсветку ещё надо доскроллить, а вопрос «а мы где?» первый. */}
        {home && (
          <div className="rk-home-note">
            <span className="rk-home-note__label">Israel</span>
            <span className="rk-home-note__rank">#{home.rank}</span>
            <span className="rk-home-note__of">of {data!.total}</span>
          </div>
        )}

        {ranking.loading && <div className="rk-state">Loading…</div>}

        {ranking.error && (
          <div className="rk-state rk-state--error">
            Could not load the ranking ({ranking.error}).
          </div>
        )}

        {!ranking.loading && !ranking.error && data && data.rows.length === 0 && (
          <div className="rk-state">
            No national records for {title} yet.
          </div>
        )}

        {!ranking.loading && data && data.rows.length > 0 && (
          <>
            <div className="rk-count">
              {data.total} {data.total === 1 ? 'country' : 'countries'}
              {/* Строку с неразобранным временем не ранжируем — но и не прячем: «страны нет
                  в рейтинге» и «её время не разобралось» разные вещи (см. 11.2.1). */}
              {data.unparsed_skipped > 0 && (
                <span className="rk-count__skipped">
                  · {data.unparsed_skipped} skipped (time not parsed)
                </span>
              )}
            </div>
            <RkTable rows={data.rows} highlight={filters.highlight} />
          </>
        )}
      </main>
    </div>
  );
}

export default RecordsProject;
