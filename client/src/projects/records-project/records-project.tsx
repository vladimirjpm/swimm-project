import React, { useCallback, useEffect, useMemo, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import './records-page.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { parseRecordsQuery, routes, type RecordsTab } from '../../utils/routes';
import { useRecordsRanking } from '../../hooks/useRecordsRanking';
import { useRegionRecords } from '../../hooks/useRegionRecords';
import { useRecordCountries } from '../../hooks/useRecordsCompare';
import UI_FlagEmoji from '../components/mix/flag-icon/flag-icon';
import DeepTabs, { type DeepTabItem } from '../components/deep/tabs';
import RkDisciplinePicker from './components/rk-discipline-picker';
import RkWorldCard from './components/rk-world-card';
import RkTable from './components/rk-table';
import RkWorldList from './components/rk-world-list';
import RkMastersTable, { mastersBands } from './components/rk-masters-table';
import RkJuniorTable from './components/rk-junior-table';
import RkFilterBar from './components/rk-filter-bar';
import UI_RecordsChecked from '../components/mix/records-checked/records-checked';
import {
  HOME_REGION, RK_DEFAULT, disciplineLabel, genderLabel, isRelay, strokeByKey, type RkFilters,
} from './rk-disciplines';

/**
 * Страница `/records` — три таба в шаблоне «папки» `DeepTabs` (18.09.2026):
 * - **Countries** — рейтинг национальных рекордов по одной дисциплине (этап 11.2.2,
 *   docs/plans/records-all-countries-plan.md §5), описан ниже;
 * - **World records** — все мировые рекорды бассейна и пола (`RkWorldList`);
 * - **Masters** — мастерсы Израиля против мирового рекорда своей полосы (`RkMastersTable`);
 * - **World Junior** — возрастные рекорды Израиля против World Junior Record (`RkJuniorTable`,
 *   21.09.2026): WJR один на полосу (ж 14–17, м 15–18), строки — возрасты внутри неё.
 * Дисциплина и пикер общие для всех табов, таб живёт в адресе (`?tab=`).
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

/**
 * Какие источники питают таб — для подписи «checked …» под заголовком (records-freshness-plan
 * U6): у каждого таба своя свежесть. NR — это WA плюс федерация (у Израиля два хозяина,
 * И-13); рекорды остальных стран приходят прогоном по странам, который журнала проверок пока
 * не пишет, — их дата не показывается.
 */
const TAB_SOURCES: Record<RecordsTab, string[]> = {
  countries: ['worldrecords', 'isrorg-age'],
  world: ['worldrecords'],
  masters: ['wa-masters', 'isrorg-masters'],
  junior: ['wa-junior', 'isrorg-age'],
};

function RecordsProject() {
  useTheme();
  const { mode } = useMode();
  const themeClass = mode === 'dark' ? 'theme-deep' : 'theme-deep-light';

  const query = useMemo(() => parseRecordsQuery(), []);

  // Таб — тот же шаблон «папки», что у страниц клуба, спортсмена и группы (DeepTabs). Живёт
  // в адресе рядом с дисциплиной: ссылка на «мастерсы 50 вольным» обязана открывать именно их.
  const [tab, setTab] = useState<RecordsTab>(query.tab);

  // Регион таба WR (9.9): null — мировые рекорды, alpha-3 — национальные рекорды страны против
  // мировых. Только open: возрастные и мастерские рекорды есть лишь у Израиля, и у них свои табы.
  const [region, setRegion] = useState<string | null>(query.region);

  // Дефолт подставляем ЗДЕСЬ, а не в parseRecordsQuery: разбор адреса обязан отличать
  // «пользователь выбрал 50 вольным» от «мы показали 50 вольным, потому что надо же
  // что-то показать». Иначе первая же смена дефолта перепишет смысл чужих ссылок.
  const [rawFilters, setFilters] = useState<RkFilters>(() => ({
    stroke: query.stroke ?? RK_DEFAULT.stroke,
    distance: query.distance ?? RK_DEFAULT.distance,
    gender: query.gender ?? RK_DEFAULT.gender,
    poolType: query.poolType ?? RK_DEFAULT.poolType,
    highlight: query.highlight,
    ageGroup: query.ageGroup,
  }));

  // Пол «mixed» бывает только у эстафеты (Э5, records-relays-plan). Правило — ВЫВОДОМ, а не
  // эффектом после отрисовки: иначе успевает уйти запрос ?gender=mixed&distance=100m (400).
  // Выбор пользователя в состоянии не теряется: вернулся на эстафету — снова mixed. Таб World
  // дистанцию не выбирает и показывает все дисциплины пола — там mixed законен всегда.
  const personalOnly = tab === 'masters';
  const filters = useMemo<RkFilters>(
    () => (rawFilters.gender === 'mixed' && tab !== 'world'
      && (!isRelay(rawFilters.distance) || personalOnly)
      ? { ...rawFilters, gender: 'male' }
      : rawFilters),
    [rawFilters, tab, personalOnly],
  );

  // Адрес — единственный носитель состояния: перезагрузка и «поделиться ссылкой» обязаны
  // давать тот же экран.
  useEffect(() => {
    const url = new URL(window.location.href);
    const set = (key: string, value: string | null) => {
      if (!value) url.searchParams.delete(key);
      else url.searchParams.set(key, value);
    };
    set('tab', tab === 'countries' ? null : tab);
    set('stroke', filters.stroke);
    set('distance', filters.distance);
    set('gender', filters.gender);
    set('pool', filters.poolType);
    set('country', filters.highlight);
    set('age', tab === 'masters' ? filters.ageGroup : null);
    set('region', tab === 'world' ? region : null);
    window.history.replaceState(null, '', url.toString());
  }, [filters, tab, region]);

  // У мастерсов эстафет нет ни в одной оси (решение 3 docs/plans/records-relays-plan.md);
  // юниорские эстафеты есть с Э1/Э3 того же плана. Пришли на таб Masters с «4×100m» — берём
  // первую личную дистанцию стиля: пустая таблица без объяснения читается как «данных нет».
  useEffect(() => {
    if (!personalOnly || !isRelay(filters.distance)) return;
    const first = strokeByKey(filters.stroke)?.distances[0] ?? RK_DEFAULT.distance;
    setFilters((f) => ({ ...f, distance: first }));
  }, [personalOnly, filters.distance, filters.stroke]);

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

  // Справочники табов грузятся, только когда таб открыт: смотрят обычно один из трёх.
  const worldOpen = useRegionRecords('WORLD', 'open', tab === 'world');
  const nationalOpen = useRegionRecords(region ?? 'WORLD', 'open', tab === 'world' && region != null);
  // Список стран — только когда таб WR открыт: выбор страны живёт там.
  const countries = useRecordCountries(tab === 'world');
  const israelMasters = useRegionRecords(HOME_REGION, 'masters', tab === 'masters');
  const worldMasters = useRegionRecords('WORLD', 'masters', tab === 'masters');
  const israelAge = useRegionRecords(HOME_REGION, 'age', tab === 'junior');
  const worldJunior = useRegionRecords('WORLD', 'junior', tab === 'junior');

  const data = ranking.data;
  const title = disciplineLabel(filters);
  const home = data?.rows.find((r) => r.region_code === HOME_REGION) ?? null;

  const worldList = region ? nationalOpen : worldOpen;
  const worldCount = worldList.data
    ?.filter((r) => r.pool_type === filters.poolType && r.gender === filters.gender).length;

  // Подписи — живые данные, как требует хендофф табов: где числа ещё нет, стоит слово.
  const tabs: DeepTabItem<RecordsTab>[] = [
    {
      id: 'countries', icon: '🌍', label: 'National Records', shortLabel: 'NR',
      sub: data ? `${data.total} countries · one event` : 'ranking by event',
    },
    {
      id: 'world', icon: '🏆', label: 'World records', shortLabel: 'WR',
      sub: worldCount != null ? `${worldCount} records${region ? ` · ${region}` : ''}` : 'every event',
    },
    // Юниоры стоят ПЕРЕД мастерсами (просьба Влада 23.09.2026): порядок табов читается
    // как возрастная лестница — страна, мир, юниоры, мастерсы.
    {
      id: 'junior', icon: '🌱', label: 'World Junior',
      sub: 'Israel ages vs world junior',
    },
    {
      id: 'masters', icon: '⏱', label: 'Masters WR',
      sub: 'Israel vs world · by age band',
    },
  ];

  // Возрастные группы для фильтра — из тех же данных, что таблица: у каждой дисциплины своя
  // лестница, и кнопка группы, которой в ней нет, была бы пустым экраном.
  const ageGroups = useMemo(
    () => (tab === 'masters' ? mastersBands(israelMasters.data, worldMasters.data, filters) : []),
    [tab, israelMasters.data, worldMasters.data, filters],
  );

  const mastersLoading = israelMasters.loading || worldMasters.loading;
  const mastersError = israelMasters.error ?? worldMasters.error;
  const juniorLoading = israelAge.loading || worldJunior.loading;
  const juniorError = israelAge.error ?? worldJunior.error;
  // Полоса WJR выбранной дисциплины — из данных (ключ строки «14-17»), не константой по полу.
  const juniorBand = worldJunior.data?.find((r) =>
    r.style === filters.stroke && r.distance === filters.distance
    && r.gender === filters.gender && r.pool_type === filters.poolType)?.age_key ?? null;

  return (
    <div className={themeClass} style={{ background: 'var(--deep-page-bg)', minHeight: '100vh' }}>
      <AppTopbar active="records" />

      <main className="rk-page">
        <div className="rk-topline">
          <div>
            <h1 className="rk-head__title">Records</h1>
            <div className="rk-head__sub">
              {tab === 'world'
                ? `${region ? `${region} national records · ` : ''}${filters.poolType} pool · ${genderLabel(filters.gender)}`
                : title}
              {tab !== 'world' && isRelay(filters.distance) && <span className="rk-head__tag">relay</span>}
              {' · '}
              {/* Единственный вход на `/records/compare`: в топбаре пункт один, «Records» —
                  это рейтинг. Дисциплину не переносим (там все сразу), а бассейн и пол —
                  те же оси, что и здесь. */}
              <a className="rc-link" href={routes.recordsCompare({
                poolType: filters.poolType, gender: filters.gender,
              })}>compare two countries</a>
            </div>
            <UI_RecordsChecked sources={TAB_SOURCES[tab]} className="rk-head__checked" />
          </div>
          <UI_ModeToggle />
        </div>

        {/* «Папка» (шаблон DeepTabs): плитки и панель — один корпус, между ними ничего
            вставлять нельзя, иначе разорвётся стык активной плитки с панелью. */}
        <div className="deep-folder mb-4">
          <DeepTabs ariaLabel="Records sections" active={tab} onSelect={setTab} tabs={tabs} />

          <div className="deep-tabs-panel rk-panel">
            <RkDisciplinePicker
              filters={filters}
              onChange={patch}
              showEvent={tab !== 'world'}
              allowRelays={!personalOnly}
              ageGroups={tab === 'masters' ? ageGroups : undefined}
            />

            {tab === 'world' && (
              <>
                {/* Регион таба (9.9): мир или одна страна. Тот же выбор, что у сравнения стран. */}
                <label className="rc-picker rk-region">
                  <span className="rc-picker__label">Region</span>
                  <span className="rc-picker__control">
                    {region && <UI_FlagEmoji countryCode={region} size="24x18" className="src-records-project" />}
                    <select
                      className="rc-select"
                      value={region ?? ''}
                      onChange={(e) => setRegion(e.target.value || null)}
                    >
                      <option value="">World</option>
                      {/* Страна из адреса, которой нет в списке (или список ещё грузится), — не
                          пропадает из селекта, иначе он молча показал бы «World». */}
                      {region && !countries.some((c) => c.code === region) && (
                        <option value={region}>{region}</option>
                      )}
                      {/* В скобках — сколько мировых рекордов держит страна; нет ни одного — скобок нет
                          (решение Влада 22.09.2026). */}
                      {countries.map((c) => (
                        <option key={c.code} value={c.code}>
                          {c.world_records ? `${c.code} (${c.world_records})` : c.code}
                        </option>
                      ))}
                    </select>
                  </span>
                </label>

                {worldList.loading && <div className="rk-state">Loading…</div>}
                {worldList.error && (
                  <div className="rk-state rk-state--error">
                    Could not load {region ? `${region} national` : 'world'} records ({worldList.error}).
                  </div>
                )}
                {worldList.data && (!region || worldOpen.data) && (
                  <RkWorldList
                    records={worldList.data}
                    filters={filters}
                    world={region ? worldOpen.data : null}
                  />
                )}
              </>
            )}

            {tab === 'masters' && (
              <>
                {mastersLoading && <div className="rk-state">Loading…</div>}
                {mastersError && (
                  <div className="rk-state rk-state--error">Could not load masters records ({mastersError}).</div>
                )}
                {/* Полоса выбранного — над карточками: у карточки рекорда своей шапки нет,
                    и «какой это заплыв» отвечает она (решение Влада 20.09.2026). Чипы
                    пикера остаются на месте — ими и выбирают. */}
                {!mastersLoading && !mastersError && israelMasters.data && worldMasters.data && (
                  <>
                    <RkFilterBar filters={filters} className="rk-fb" />
                    <RkMastersTable israel={israelMasters.data} world={worldMasters.data} filters={filters} />
                  </>
                )}
              </>
            )}

            {tab === 'junior' && (
              <>
                {juniorLoading && <div className="rk-state">Loading…</div>}
                {juniorError && (
                  <div className="rk-state rk-state--error">Could not load junior records ({juniorError}).</div>
                )}
                {!juniorLoading && !juniorError && israelAge.data && worldJunior.data && (
                  <>
                    {/* Возраст здесь не фильтр, а полоса самого WJR — чип показывает её. */}
                    <RkFilterBar
                      filters={{ ...filters, ageGroup: null }}
                      ageBand={juniorBand}
                      className="rk-fb"
                    />
                    <RkJuniorTable israel={israelAge.data} world={worldJunior.data} filters={filters} />
                  </>
                )}
              </>
            )}

            {tab === 'countries' && (
              <>
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
              </>
            )}
          </div>
        </div>
      </main>
    </div>
  );
}

export default RecordsProject;
