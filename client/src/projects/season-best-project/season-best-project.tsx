import React, { useCallback, useEffect, useMemo, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './season-best-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import DeepSeasonCarousel from '../components/deep/season-carousel';
import { parseSeasonBestQuery } from '../../utils/routes';
import { seasonLabel } from '../../utils/helpers/season-helper';
import { useSeasonBestList, useSeasonBestOptions } from '../../hooks/useSeasonBestList';
import SEASON_BEST_MODULES from './season-best-modules';
import { strokeLabel, type SbFilters } from './sb-filters-model';
import { useSeasonBestFilterHost } from './sb-filter-host';
import SbFilterPanel from './components/sb-filter-panel';
import SbFilterBar from './components/sb-filter-bar';
import SbList from './components/sb-list';

/**
 * Страница `/season-best` — списки «лучшие в сезоне»: кто быстрее всех в связке
 * стиль × дистанция × бассейн, с фильтрами по возрасту, полу и клубу.
 *
 * Устройство повторяет страницу results (требование Влада): слева сайдбар фильтров, над
 * списком — шапка фильтров с выбранными значениями, на мобайле сайдбар превращается в
 * раскрывающийся блок. Палитра при этом deep — страница стоит рядом со страницей
 * спортсмена, откуда сюда ведут ссылки.
 *
 * Каждый блок — модуль с флагом в `season-best-modules.ts`: выключить колонку или целую
 * полосу должно быть правкой одной строки, а не походом по вёрстке.
 *
 * Весь фильтр живёт в query и читается через `parseSeasonBestQuery` — у списка нет
 * идентичности в пути, адресом его делает набор параметров (правило routes.ts). Поэтому
 * ссылка со страницы спортсмена открывает ровно тот срез, который она обещала.
 */

const MODULES = SEASON_BEST_MODULES;
const PAGE_SIZE = 50;

/**
 * Подпись группы сверстников — та же формула, что на сервере (SwimmerPageBuilder).
 *
 * Три оси возраста дают три разные подписи: год («girls 12»), взрослый хвост («men 21+») и
 * мастерская группа («masters 45-49»), где пол в подпись не идёт — группа уже сама себе круг.
 */
function groupLabel(
  filters: Pick<SbFilters, 'age' | 'ageTo' | 'ageGroup' | 'gender'>,
): string | null {
  if (filters.ageGroup) return `masters ${filters.ageGroup}`;
  const { age, gender } = filters;
  if (age == null) return null;
  const label = filters.ageTo != null ? `${age}+` : String(age);
  if (gender == null) return `age ${label}`;
  const adult = age >= 18;
  const noun = gender === 'female' ? (adult ? 'women' : 'girls') : (adult ? 'men' : 'boys');
  return `${noun} ${label}`;
}

function SeasonBestProject() {
  useTheme();
  const { mode } = useMode();
  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';

  const query = useMemo(() => parseSeasonBestQuery(), []);
  const options = useSeasonBestOptions();

  const [filters, setFilters] = useState<SbFilters>(() => ({
    season: query.season,
    stroke: query.stroke,
    distance: query.distance,
    poolType: query.poolType,
    gender: query.gender,
    age: query.age,
    ageTo: query.ageTo,
    ageGroup: query.ageGroup,
    clubId: query.clubId,
    bestPerSwimmer: query.bestPerSwimmer,
  }));
  const [limit, setLimit] = useState(PAGE_SIZE);
  const [filtersOpen, setFiltersOpen] = useState(false);

  // Сезон не задан в адресе — берём витринный с сервера (самый свежий с данными). Ждём
  // опции, а не подставляем текущий календарный: сезон федерации идёт октябрь–август, и
  // «текущий» в сентябре указал бы на сезон, где ещё нет ни одного старта.
  useEffect(() => {
    if (filters.season != null || !options.data?.seasons?.length) return;
    const preferred = options.data.seasons.find((s) => s.is_display_default) ?? options.data.seasons[0];
    setFilters((f) => ({ ...f, season: preferred.season }));
  }, [options.data, filters.season]);

  // Адрес — единственный носитель состояния: перезагрузка и «поделиться ссылкой» обязаны
  // давать тот же экран. Клуб и режим строк тоже пишем, иначе ссылка врала бы.
  useEffect(() => {
    const url = new URL(window.location.href);
    const set = (key: string, value: string | number | null | undefined) => {
      if (value == null || value === '') url.searchParams.delete(key);
      else url.searchParams.set(key, String(value));
    };
    set('season', filters.season);
    set('stroke', filters.stroke);
    set('distance', filters.distance);
    set('pool', filters.poolType);
    set('gender', filters.gender);
    set('age', filters.age);
    set('age_to', filters.ageTo);
    set('age_group', filters.ageGroup);
    set('club', filters.clubId);
    set('best', filters.bestPerSwimmer ? 'true' : null);
    window.history.replaceState(null, '', url.toString());
  }, [filters]);

  const list = useSeasonBestList({
    style: filters.stroke,
    distance: filters.distance,
    poolType: filters.poolType,
    season: filters.season,
    age: filters.age,
    ageTo: filters.ageTo,
    // Группа задана — срез идёт по мастерским стартам (см. sb-filters-model).
    masters: filters.ageGroup != null,
    ageGroup: filters.ageGroup,
    gender: filters.gender,
    clubId: filters.clubId,
    bestPerSwimmer: filters.bestPerSwimmer,
    limit,
  });

  const patch = useCallback((next: Partial<SbFilters>) => {
    setLimit(PAGE_SIZE);   // сменили срез — страница снова первая
    setFilters((f) => ({ ...f, ...next }));
  }, []);

  const data = list.data;

  // Хост общей панели фильтров (Ф4): он переводит `FilterSelected` этой панели в `SbFilters`
  // страницы. Клубы берём из ответа списка — они посчитаны ДО фильтра по клубу, иначе,
  // выбрав клуб, пользователь больше не смог бы выбрать другой.
  const filterHost = useSeasonBestFilterHost({
    filters,
    onChange: patch,
    events: options.data?.events ?? [],
    clubs: data?.clubs ?? [],
    ageGroups: options.data?.age_groups ?? [],
    modules: MODULES,
  });
  const group = groupLabel(filters);
  const eventTitle = filters.stroke
    ? `${filters.distance ? `${filters.distance} m ` : ''}${strokeLabel(filters.stroke)}`
    : null;
  const hasEvent = !!filters.stroke && !!filters.distance;

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      {MODULES.topbar && <AppTopbar />}

      <main className="sb-page">
        <div className="sb-topline">
          <div>
            <h1 className="sb-head__title">
              Season best{group ? ` — ${group}` : ''}
            </h1>
            <div className="sb-head__sub">
              {[
                filters.season != null ? `season ${seasonLabel(filters.season)}` : 'all seasons',
                eventTitle ?? 'no event selected',
                // Бассейн называем словами: «50m» рядом с дистанцией «50» читается как
                // повтор одного и того же числа.
                filters.poolType ? `${filters.poolType} pool` : 'both pools',
              ].join(' · ')}
            </div>
          </div>
          <UI_ModeToggle />
        </div>

        {MODULES.seasonCarousel && options.data && options.data.seasons.length > 0 && (
          <DeepSeasonCarousel
            seasons={options.data.seasons.map((s) => ({ season: s.season, label: s.label }))}
            season={filters.season}
            onSeason={(season) => patch({ season })}
          />
        )}

        <div className="sb-layout">
          {MODULES.filters && (
            <>
              <button
                type="button"
                className="sb-filters-toggle"
                onClick={() => setFiltersOpen((o) => !o)}
                aria-expanded={filtersOpen}
              >
                {filtersOpen ? 'Hide filters' : 'Filters'}
              </button>
              <aside className={`sb-side${filtersOpen ? ' sb-side--open' : ''}`}>
                <SbFilterPanel
                  host={filterHost}
                  filters={filters}
                  onChange={patch}
                  modules={MODULES}
                />
              </aside>
            </>
          )}

          <section className="sb-content">
            {MODULES.filterBar && (
              <SbFilterBar
                filters={filters}
                seasonLabel={filters.season != null ? seasonLabel(filters.season) : null}
                clubs={data?.clubs ?? []}
                showClub={MODULES.filterClub}
                latinNames={MODULES.latinNames}
              />
            )}

            {!hasEvent ? (
              // Дисциплина не выбрана — списка нет и быть не может. Говорим это прямо и
              // показываем, где выбрать, вместо того чтобы подставить свой любимый заплыв.
              <div className="sb-empty">
                <div className="sb-empty__title">Pick an event</div>
                <p className="sb-empty__text">
                  Choose a stroke and a distance in the filters to see who is fastest this season.
                </p>
              </div>
            ) : list.error ? (
              <div className="sb-empty">
                <div className="sb-empty__title">Could not load the list</div>
                <p className="sb-empty__text">Please try again in a moment.</p>
              </div>
            ) : !data ? (
              <div className="sb-empty">
                <div className="sb-empty__title">{list.loading ? 'Loading…' : 'No data'}</div>
              </div>
            ) : data.data.length === 0 ? (
              <div className="sb-empty">
                <div className="sb-empty__title">Nobody swam this yet</div>
                <p className="sb-empty__text">
                  No swims match this slice in season {data.season_label}. Try another pool,
                  age or season.
                </p>
              </div>
            ) : (
              <>
                {/* Плитка дисциплины в строке выключена флагом `disciplineInRow`: дисциплина
                    одна на весь срез и уже названа шапкой и чипом Event. */}
                <div className="sb-listhead">
                  <div className="sb-listhead__meta">
                    <div className="sb-listhead__title">
                      {data.total} {data.total === 1 ? 'swim' : 'swims'}
                      {' · '}{data.swimmers} {data.swimmers === 1 ? 'swimmer' : 'swimmers'}
                      {' · '}{data.meets} {data.meets === 1 ? 'meet' : 'meets'}
                    </div>
                    <div className="sb-listhead__hint">
                      {filters.bestPerSwimmer
                        ? 'one best swim per swimmer'
                        : 'every swim of the season, so one swimmer can hold several places'}
                    </div>
                  </div>
                </div>

                <SbList
                  rows={data.data}
                  modules={MODULES}
                  stroke={filters.stroke ?? ''}
                  distance={filters.distance ?? ''}
                  isMasters={filters.ageGroup != null}
                  highlightSwimmerId={query.swimmerId}
                />

                {data.data.length < data.total && (
                  <button
                    type="button"
                    className="sb-more"
                    onClick={() => setLimit((l) => l + PAGE_SIZE)}
                    disabled={list.loading}
                  >
                    {list.loading
                      ? 'Loading…'
                      : `Show more — ${data.data.length} of ${data.total}`}
                  </button>
                )}

                {MODULES.footerNote && (
                  <div className="sb-note">
                    Places are counted among the {data.meets} meets we have imported, not from an
                    official ranking. Equal times share a place. Times from 25 m and 50 m pools are
                    not comparable — pick a pool to compare like with like.
                  </div>
                )}
              </>
            )}
          </section>
        </div>
      </main>
    </div>
  );
}

export default SeasonBestProject;
