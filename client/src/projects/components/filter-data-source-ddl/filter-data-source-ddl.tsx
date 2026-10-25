import React from 'react';
import { createPortal } from 'react-dom';
import './filter-data-source-ddl.css';
import UI_CompetitionName from '../mix/competition-name/competition-name';
import { useAppSelector } from '../../../store/store';
import { routes } from '../../../utils/routes';
import { seasonLabel, seasonStartYear } from '../../../utils/helpers/season-helper';
import { ResultsCategory } from '../../../utils/constants/results-categories';
import {
  CompetitionSource,
  parseDate,
  dateLabel,
  monthLabel,
  isChampionshipSource,
} from '../../../utils/helpers/competition-source';
import { useIsMobile } from './use-is-mobile';
import { useCompetitionCategories } from './use-competition-categories';
import { useSelectorPanel } from './use-selector-panel';
import { useCompetitionSources, useCompetitionList } from './use-competition-list';
import { useResultsLoader } from './use-results-loader';
import { useUrlInit } from './use-url-init';

// ── Категорийный селектор соревнований ──────────────────────────────────────
// База: design_handoff_category_selector (табы + поиск + сезон + live/upcoming +
// месячные группы + архив). Дельта design_handoff_selector_all: выбор сворачивает
// селектор в акцентную шапку; «Change» открывает панель как дропдаун (desktop)
// или bottom sheet (mobile, <640px); deep-link ?category= рендерит панель inline.
//
// Логика — в хуках рядом: категории (use-competition-categories), список и его фильтры
// (use-competition-list), панель «Change» (use-selector-panel), загрузка результатов в
// store (use-results-loader), начальное состояние из адреса (use-url-init). Здесь —
// выбор источника/категории и разметка.

// Обновляем только свои параметры, не трогая чужие. id и category взаимоисключающие.
const writeUrl = (
  patch: { category?: string; source?: CompetitionSource | null; season?: number | 'all' | null },
) => {
  const params = new URLSearchParams(window.location.search);
  ['category', 'cat', 'competitionId', 'eventId'].forEach((k) => params.delete(k));
  // Идентичность соревнования уходит в путь (/competitions/{id}); eventId и категория —
  // это фасеты вида, остаются в query на /results.
  let pathname = routes.results();
  if (patch.source) {
    if (patch.source.kind === 'event') params.set('eventId', String(patch.source.id));
    else pathname = routes.competition(patch.source.id);
  } else if (patch.category) {
    params.set('category', patch.category);
  }
  if (patch.season != null) params.set('season', String(patch.season));
  else params.delete('season');
  const qs = params.toString();
  window.history.replaceState(null, '', qs ? `${pathname}?${qs}` : pathname);
};

interface DataSourceDDLProps {
  /**
   * Кастомная шапка вместо встроенной (шапка соревнования с табами,
   * design_handoff_competition_overview): DDL отдаёт ей toggle панели и выбранный
   * источник, а сам рендерит только дропдаун/шит выбора. Без выбранного источника
   * игнорируется (панель открыта inline, как раньше).
   */
  renderHeader?: (
    togglePanel: () => void,
    panelOpen: boolean,
    source: CompetitionSource,
  ) => React.ReactNode;
  /**
   * Ленивая загрузка Swims (design_handoff_competition_overview): в режиме соревнования
   * дефолтный таб — Overview, а тяжёлый фетч результатов (`/api/results`, до N страниц)
   * откладывается, пока не пришёл Overview или пока юзер не открыл таб Swims. Когда false,
   * при выборе источника мы только «праймим» шапку (title/sourceParams/флаги из
   * CompetitionSource), но не тянем результаты; фетч случится, когда флаг станет true.
   * Undefined/true → прежнее поведение (грузим сразу). См. use-results-loader.
   */
  canLoadResults?: boolean;
}

const DataSourceDDL: React.FC<DataSourceDDLProps> = ({ renderHeader, canLoadResults = true }) => {
  // Выбранная строка — из store (title = имя источника): синхронизирует инстансы.
  const selectedTitle = useAppSelector((s) => s.dataSourceSelected?.title);
  const isMobile = useIsMobile();

  // Порядок хуков ниже = порядок эффектов в прежнем едином компоненте (категории →
  // панель → список → загрузка → адрес); переставлять без нужды не стоит.
  const { liveCategoryLabels, allCategories, categoriesLoaded, categoryLabel } =
    useCompetitionCategories();
  const wrapRef = React.useRef<HTMLDivElement>(null);
  const touchStartY = React.useRef<number | null>(null);
  const { panelOpen, setPanelOpen, catMenuOpen, setCatMenuOpen, dropBox } =
    useSelectorPanel(isMobile, wrapRef);
  const { sources, loaded } = useCompetitionSources();
  const {
    cat, setCat,
    search, setSearch,
    season, setSeason,
    seasonTouched, setSeasonTouched,
    archiveOpen, setArchiveOpen,
    champOnly, setChampOnly,
    seasons, activeSeason,
    visible, champCount, nowItems, archivedItems, monthGroups,
    catCount,
  } = useCompetitionList(sources);
  const { requestSource, loadDirect } = useResultsLoader(canLoadResults);
  useUrlInit({
    loaded,
    sources,
    cat,
    setCat,
    setSeason,
    setSeasonTouched,
    categoriesLoaded,
    allCategories,
    requestSource,
    loadDirect,
  });

  // Бейдж-кружок (буква из БД) + название — для табов/строк селектора категорий.
  // Кружок не завязан на active/неактивный вариант пилюли отдельными цветами (у tabsRow
  // активная пилюля залита --theme-primary, у categoryRow активная строка — лишь лёгкий
  // --theme-primary-light тон; единой пары «фон+текст», контрастной сразу везде, в теме нет).
  // Вместо этого — обводка currentColor: кружок наследует уже правильно посчитанный цвет
  // текста родителя в каждом состоянии и остаётся читаемым без доп. параметров.
  const categoryLabelNode = (c: ResultsCategory): React.ReactNode => {
    const live = liveCategoryLabels[c.key];
    if (!live?.badge) return categoryLabel(c);
    return (
      <span className="inline-flex items-center gap-1.5">
        <span
          className="inline-flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-full text-[10px] font-extrabold leading-none"
          style={{ border: '1.5px solid currentColor' }}
        >
          {live.badge}
        </span>
        {live.name}
      </span>
    );
  };

  // Выбор источника из списка: адрес, закрыть панель, результаты — сейчас или по гейту
  // ленивого Swims (решает useResultsLoader).
  const selectSource = (src: CompetitionSource) => {
    writeUrl({ source: src, season: seasonTouched ? season : null });
    setPanelOpen(false);
    requestSource(src);
  };

  const activeCategory = allCategories.find((c) => c.key === cat);
  const activeCatLabel = activeCategory ? categoryLabel(activeCategory) : 'All';
  const isSelected = (s: CompetitionSource) =>
    !!selectedTitle && selectedTitle === s.name;

  // ── Рендер-помощник строки (mobile ≥52px, ✓ у выбранной — Row.dc.html) ──
  const row = (s: CompetitionSource) => {
    const live = s.status === 'live';
    const sel = !live && isSelected(s);
    const rowStyle: React.CSSProperties = live
      ? { background: 'var(--status-live-bg)', border: '1px solid var(--status-live-border)' }
      : sel
        ? { background: 'var(--theme-primary-light)', border: '1px solid var(--theme-primary)' }
        : { background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border-row)' };
    return (
      <button
        key={`${s.kind}:${s.id}`}
        type="button"
        onClick={() => selectSource(s)}
        className="dv-sel-row flex w-full cursor-pointer items-center justify-between gap-2.5 rounded-xl px-3.5 py-2 text-left max-sm:min-h-[52px] sm:py-3"
        style={rowStyle}
      >
        <span className="flex min-w-0 items-center gap-2">
          {sel && (
            <span className="shrink-0 text-sm font-black" style={{ color: 'var(--theme-primary)' }}>
              ✓
            </span>
          )}
          <span
            dir="rtl"
            className={`min-w-0 overflow-hidden text-ellipsis whitespace-nowrap text-sm ${live || sel ? 'font-bold' : 'font-semibold'}`}
            style={{
              // sel: фон --theme-primary-light не зависит от режима (light/dark) — текст красим
              // в --theme-primary (как sportsmen-details.tsx), а не --theme-mode-text.
              color: sel
                ? 'var(--theme-primary)'
                : live
                  ? 'var(--theme-mode-text)'
                  : 'var(--theme-mode-text-secondary)',
              textAlign: 'left',
            }}
          >
            <UI_CompetitionName name={s.name} isChampionship={isChampionshipSource(s)} />
          </span>
        </span>
        {live ? (
          <span className="flex shrink-0 items-center gap-1.5">
            <span
              className="dv-sel-live-dot h-1.5 w-1.5 rounded-full"
              style={{ background: 'var(--status-live)' }}
            />
            <span
              className="font-mono text-[10px] font-extrabold whitespace-nowrap"
              style={{ color: 'var(--status-live)' }}
            >
              LIVE
            </span>
          </span>
        ) : (
          <span
            className="shrink-0 text-xs whitespace-nowrap"
            style={{
              color:
                sel || s.status === 'upcoming'
                  ? 'var(--theme-primary)'
                  : 'var(--theme-mode-text-muted)',
              fontWeight: sel || s.status === 'upcoming' ? 700 : 400,
            }}
          >
            {dateLabel(s)}
          </span>
        )}
      </button>
    );
  };

  const selectCat = (key: string) => {
    setCat(key);
    setArchiveOpen(false);
    setCatMenuOpen(false);
    // Переключение категории НЕ сбрасывает выбор (selector_all, Change 4).
    // URL меняем на категорию только пока ничего не выбрано.
    if (!selectedSourceObj) {
      writeUrl({ category: key, season: seasonTouched ? season : null });
    }
  };

  // ── Переиспользуемые части панели (inline / dropdown / sheet) ──
  // Десктоп: табы-пилюли (как в базовом хендоффе)
  const tabsRow = (
    <div className="flex flex-wrap gap-2">
      {allCategories.map((c) => {
        const active = cat === c.key;
        return (
          <button
            key={c.key}
            type="button"
            onClick={() => selectCat(c.key)}
            className="inline-flex shrink-0 cursor-pointer items-center gap-1.5 whitespace-nowrap rounded-full px-4 py-2 text-[13px]"
            style={
              active
                ? {
                    background: 'var(--theme-primary)',
                    color: 'var(--theme-mode-accent-text)',
                    border: '1px solid var(--theme-primary)',
                    fontWeight: 800,
                  }
                : {
                    background: 'var(--theme-mode-input-bg)',
                    color: 'var(--theme-mode-text-secondary)',
                    border: '1px solid var(--theme-mode-border-input)',
                    fontWeight: 700,
                  }
            }
          >
            {categoryLabelNode(c)}{' '}
            <span style={{ fontWeight: 600, opacity: active ? 0.75 : 0.6 }}>{catCount(c.key)}</span>
          </button>
        );
      })}
    </div>
  );

  // Мобайл, вариант 9b: строка «Category: Junior · 3 ▾» (48px) + своё меню (ряды 46px, ✓)
  const categoryRow = (
    <div>
      <button
        type="button"
        onClick={() => setCatMenuOpen((v) => !v)}
        className="flex min-h-[48px] w-full cursor-pointer items-center justify-between rounded-xl px-3.5 text-[13px]"
        style={{
          background: 'var(--theme-mode-input-bg)',
          border: '1px solid var(--theme-mode-border-input)',
          color: 'var(--theme-mode-text)',
        }}
      >
        <span style={{ color: 'var(--theme-mode-text-secondary)', fontWeight: 700 }}>
          Category:{' '}
          <span style={{ color: 'var(--theme-mode-text)', fontWeight: 800 }}>
            {activeCategory ? categoryLabelNode(activeCategory) : 'All'}
          </span>{' '}
          · {catCount(cat)}
        </span>
        <span className="text-[10px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          {catMenuOpen ? '▴' : '▾'}
        </span>
      </button>
      {catMenuOpen && (
        <div
          className="mt-2 flex flex-col rounded-xl p-1"
          style={{
            background: 'var(--theme-mode-surface)',
            border: '1px solid var(--theme-mode-border-input)',
          }}
        >
          {allCategories.map((c) => {
            const active = cat === c.key;
            return (
              <button
                key={c.key}
                type="button"
                onClick={() => selectCat(c.key)}
                className="flex min-h-[46px] cursor-pointer items-center justify-between rounded-[10px] px-3 text-[14px]"
                style={{
                  background: active ? 'var(--theme-primary-light)' : 'transparent',
                  color: active ? 'var(--theme-primary)' : 'var(--theme-mode-text)',
                  fontWeight: active ? 800 : 600,
                }}
              >
                <span>
                  {categoryLabelNode(c)}{' '}
                  <span style={{ color: 'var(--theme-mode-text-muted)', fontWeight: 600 }}>
                    · {catCount(c.key)}
                  </span>
                </span>
                {active && (
                  <span className="font-black" style={{ color: 'var(--theme-primary)' }}>
                    ✓
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );

  const searchBox = (
    <div
      className="flex flex-1 items-center gap-2 rounded-xl px-3.5 py-2.5 max-sm:min-h-[44px]"
      style={{
        background: 'var(--theme-mode-input-bg)',
        border: '1px solid var(--theme-mode-border-input)',
      }}
    >
      <span className="text-sm" style={{ color: 'var(--theme-mode-text-muted)' }}>
        ⌕
      </span>
      <input
        type="text"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder={`Search in ${activeCatLabel}…`}
        className="dv-sel-search w-full bg-transparent text-[13.5px] outline-none"
        style={{ color: 'var(--theme-mode-text)' }}
      />
    </div>
  );

  const seasonSelect =
    seasons.length > 0 ? (
      <select
        value={activeSeason}
        onChange={(e) => {
          setSeason(e.target.value === 'all' ? 'all' : Number(e.target.value));
          setSeasonTouched(true);
        }}
        className="cursor-pointer rounded-xl px-3.5 py-2.5 text-[13px] font-bold max-sm:min-h-[44px]"
        style={{
          background: 'var(--theme-mode-input-bg)',
          border: '1px solid var(--theme-mode-border-input)',
          color: 'var(--theme-mode-text)',
        }}
      >
        <option value="all">All seasons</option>
        {seasons.map((y) => (
          <option key={y} value={y}>
            Season {seasonLabel(y)}
          </option>
        ))}
      </select>
    ) : null;

  // Тумблер «только чемпионаты Израиля» (on/off), рядом с поиском и сезоном.
  const champToggle = (
    <button
      type="button"
      onClick={() => setChampOnly((v) => !v)}
      aria-pressed={champOnly}
      title="Show Israeli championships only"
      className="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-xl px-3.5 py-2.5 text-[13px] font-bold whitespace-nowrap max-sm:min-h-[44px] max-sm:justify-center"
      style={
        champOnly
          ? {
              background: 'var(--theme-primary)',
              color: 'var(--theme-mode-accent-text)',
              border: '1px solid var(--theme-primary)',
            }
          : {
              background: 'var(--theme-mode-input-bg)',
              color: 'var(--theme-mode-text-secondary)',
              border: '1px solid var(--theme-mode-border-input)',
            }
      }
    >
      🏆 Championships{' '}
      <span style={{ fontWeight: 600, opacity: champOnly ? 0.75 : 0.6 }}>{champCount}</span>
    </button>
  );

  const listBody = (
    <>
      {!loaded && (
        <div className="py-2 text-sm" style={{ color: 'var(--theme-mode-text-muted)' }}>
          Loading competitions…
        </div>
      )}

      {/* Live / Upcoming — всегда сверху, вне группировки и архива */}
      {nowItems.length > 0 && (
        <>
          <div
            className="mb-2 text-[10.5px] font-extrabold uppercase tracking-[0.18em]"
            style={{ color: 'var(--status-live)' }}
          >
            Live · Upcoming
          </div>
          <div className="mb-4 flex flex-col gap-2">{nowItems.map(row)}</div>
        </>
      )}

      {/* Завершённые — по месяцам, новые сверху */}
      {monthGroups.map((g) => (
        <React.Fragment key={g.label}>
          <div
            className="mb-2 text-[10.5px] font-extrabold uppercase tracking-[0.18em]"
            style={{ color: 'var(--theme-mode-text-muted)' }}
          >
            {g.label}
          </div>
          <div className="mb-3.5 flex flex-col gap-2">{g.items.map(row)}</div>
        </React.Fragment>
      ))}

      {loaded && visible.length === 0 && (
        <div className="py-2 text-sm" style={{ color: 'var(--theme-mode-text-muted)' }}>
          No competitions found{search ? ` for «${search}»` : ''}.
        </div>
      )}

      {archivedItems.length > 0 && (
        <button
          type="button"
          onClick={() => setArchiveOpen((v) => !v)}
          className="flex w-full cursor-pointer items-center justify-center gap-2 rounded-xl p-3 text-[13px] font-bold max-sm:min-h-[48px]"
          style={{
            background: 'var(--theme-mode-surface)',
            border: '1px dashed var(--theme-mode-border-input)',
            color: 'var(--theme-mode-text-secondary)',
          }}
        >
          {archiveOpen
            ? 'Hide earlier competitions'
            : `Show ${archivedItems.length} earlier competitions`}
          <span className="text-[10px]">{archiveOpen ? '▴' : '▾'}</span>
        </button>
      )}
    </>
  );

  // Выбранное соревнование из списка источников (по title в store)
  const selectedSourceObj = selectedTitle
    ? sources.find((s) => s.name === selectedTitle) ?? null
    : null;

  // Карточка панели: inline (deep-link ?category=) и desktop-дропдаун
  /**
   * Панель выбора. `maxHeight` задаёт дропдаун, который считает, сколько места осталось до
   * низа экрана: без ограничения список соревнований рос как хотел и уезжал за нижний край
   * (баг 31.08.2026 — у залогиненного, с персональной полосой в шапке, за экраном
   * оказывалась половина панели). Скроллится ТОЛЬКО список: табы, поиск и сезон должны
   * остаться на виду.
   */
  const panel = (inline: boolean, maxHeight?: number) => (
    <div
      className="dv-data-source rounded-[18px] px-[22px] py-5 max-sm:px-3.5"
      style={{
        background: 'var(--theme-mode-surface)',
        border: '1px solid var(--theme-mode-border)',
        boxShadow: 'var(--theme-mode-card-shadow)',
        ...(maxHeight != null ? { maxHeight, display: 'flex', flexDirection: 'column' as const } : null),
      }}
    >
      <div className="mb-3.5 flex items-center justify-between">
        <div
          className="text-[11px] font-extrabold uppercase tracking-[0.14em]"
          style={{ color: 'var(--theme-mode-text-muted)' }}
        >
          Select competition{inline ? '' : ` — ${activeCatLabel}`}
        </div>
        {!inline && (
          <button
            type="button"
            onClick={() => setPanelOpen(false)}
            className="cursor-pointer text-xs font-bold"
            style={{ color: 'var(--theme-mode-text-muted)' }}
          >
            ✕ Close
          </button>
        )}
      </div>
      <div className="mb-3.5 max-sm:hidden">{tabsRow}</div>
      <div className="mb-3.5 sm:hidden">{categoryRow}</div>
      <div className="mb-[18px] flex gap-2.5 max-sm:flex-col">
        {searchBox}
        {champToggle}
        {seasonSelect}
      </div>
      {maxHeight != null
        ? <div className="min-h-0 flex-1 overflow-y-auto">{listBody}</div>
        : listBody}
    </div>
  );

  // Мобильный top sheet («Change» на <640px): панель выезжает сверху вниз
  const sheet = createPortal(
    <div
      className="dv-sel-sheet-backdrop fixed inset-0 z-[130] flex items-start"
      style={{ background: 'rgba(10,14,20,0.5)' }}
      onClick={() => setPanelOpen(false)}
    >
      <div
        className="dv-sel-sheet flex w-full flex-col"
        style={{
          maxHeight: '88%',
          borderRadius: '0 0 22px 22px',
          background: 'var(--theme-mode-surface)',
          boxShadow: '0 18px 50px rgba(0,0,0,0.3)',
          paddingTop: 'env(safe-area-inset-top)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 pb-2.5 pt-3">
          <div className="text-base font-extrabold" style={{ color: 'var(--theme-mode-text)' }}>
            Select competition — {activeCatLabel}
          </div>
          <button
            type="button"
            onClick={() => setPanelOpen(false)}
            className="flex h-11 w-11 shrink-0 cursor-pointer items-center justify-center rounded-xl text-sm"
            style={{
              background: 'var(--theme-mode-input-bg)',
              color: 'var(--theme-mode-text-secondary)',
            }}
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        <div className="px-4 pb-3">{categoryRow}</div>
        <div className="flex flex-col gap-2 px-4 pb-3">
          {searchBox}
          {champToggle}
          {seasonSelect}
        </div>
        <div className="min-h-0 overflow-y-auto px-4 pb-3">{listBody}</div>

        {/* Ручка снизу top-sheet: свайп вверх → закрытие */}
        <div
          className="flex justify-center pb-2.5 pt-1"
          onTouchStart={(e) => {
            touchStartY.current = e.touches[0].clientY;
          }}
          onTouchMove={(e) => {
            if (
              touchStartY.current != null &&
              touchStartY.current - e.touches[0].clientY > 70
            ) {
              touchStartY.current = null;
              setPanelOpen(false);
            }
          }}
          onTouchEnd={() => {
            touchStartY.current = null;
          }}
        >
          <span
            className="h-1 w-10 rounded-[3px]"
            style={{ background: 'var(--theme-mode-border-input)' }}
          />
        </div>
      </div>
    </div>,
    document.body,
  );

  // Без выбора — селектор открыт inline (deep-link ?category=, frame 7a)
  if (!selectedSourceObj) return panel(true);

  // С выбором и кастомной шапкой (шапка соревнования) — встроенную не рисуем:
  // рендерим header из пропа, ниже — тот же дропдаун/шит выбора.
  if (renderHeader) {
    return (
      <div ref={wrapRef} className="relative">
        {renderHeader(() => setPanelOpen((v) => !v), panelOpen, selectedSourceObj)}
        {panelOpen && !isMobile && dropBox && (
          // Дропдаун привязан к ВЬЮПОРТУ, а не к низу шапки (баг 31.08.2026).
          //
          // Было `absolute; top: calc(100% + 10px)` — то есть под ВСЕЙ шапкой: hero +
          // персональная полоса + табы. У залогиненного это ~400px, панель начиналась
          // ниже середины экрана и наполовину уходила за нижний край. Плюс с приездом
          // компакт-бара (16b) «Change» стало можно нажать, прокрутив шапку прочь, —
          // и панель открывалась там, где её не видно вовсе.
          //
          // Теперь `fixed` под тем, что реально сверху: под hero, пока он на экране, и
          // под компакт-баром, когда шапка уехала (см. dropBox). Ширина и левый край —
          // от контейнера шапки, чтобы панель не разъезжалась с ней по ширине страницы.
          <div
            className="dv-sel-panel-drop fixed z-[70]"
            style={{
              top: dropBox.top,
              left: dropBox.left,
              width: dropBox.width,
              filter: 'drop-shadow(0 18px 30px rgba(0,0,0,0.16))',
            }}
          >
            {panel(false, dropBox.maxHeight)}
          </div>
        )}
        {panelOpen && isMobile && sheet}
      </div>
    );
  }

  // С выбором — компактная шапка соревнования; «Change» открывает панель поверх контента
  const selectedDate = parseDate(selectedSourceObj.date);
  const seasonYear = selectedDate ? seasonStartYear(selectedDate) : undefined;
  // day_count в имя не подмешиваем: RTL-имена ломают порядок LTR-суффикса,
  // а многодневность и так видна по диапазону дат в headerMeta («15–16 Jul»).
  const headerName = selectedSourceObj.name;
  const headerMeta = [
    dateLabel(selectedSourceObj),
    selectedSourceObj.pool_type,
    monthLabel(selectedSourceObj),
    seasonYear ? `Season ${seasonLabel(seasonYear)}` : '',
  ]
    .filter(Boolean)
    .join(' · ');
  // Бейдж категории соревнования в шапке — «выбит» в акцентном фоне (--theme-mode-accent-text),
  // а не currentColor-обводка, как в табах: тут фон всегда сплошной --theme-primary,
  // так что можно уверенно закрасить кружок, а не только обвести.
  // Без канонической категории (null) берём бейдж первой кастомной из членства.
  const headerCategoryKey =
    selectedSourceObj.category ??
    selectedSourceObj.categories?.find((k) => liveCategoryLabels[k]?.badge);
  const headerCategoryBadge = headerCategoryKey
    ? liveCategoryLabels[headerCategoryKey]?.badge
    : undefined;

  return (
    <div ref={wrapRef} className="relative">
      {/* Desktop-шапка: одна строка ~72px */}
      <div
        className="hidden items-center justify-between gap-4 rounded-[14px] px-5 py-4 sm:flex md:rounded-t-none"
        style={{
          background: 'var(--theme-primary)',
          color: 'var(--theme-mode-accent-text)',
          boxShadow: 'var(--theme-mode-card-shadow)',
        }}
      >
        <div className="flex min-w-0 items-center gap-3.5">
          {headerCategoryBadge && (
            <span
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-lg font-extrabold leading-none"
              style={{ background: 'var(--theme-mode-accent-text)', color: 'var(--theme-primary)' }}
            >
              {headerCategoryBadge}
            </span>
          )}
          <div className="flex min-w-0 flex-col gap-[5px]">
            <div
              dir="rtl"
              className="overflow-hidden text-ellipsis whitespace-nowrap text-[19px] font-extrabold"
              style={{ textAlign: 'left' }}
            >
              {headerName}
            </div>
            <div className="text-[12.5px] font-semibold opacity-85">{headerMeta}</div>
          </div>
        </div>
        <button
          type="button"
          onClick={() => setPanelOpen((v) => !v)}
          className="flex min-h-[44px] shrink-0 cursor-pointer items-center gap-2 whitespace-nowrap rounded-[10px] px-[18px] text-[13px] font-extrabold"
          style={{
            background: 'rgba(255,255,255,0.16)',
            border: '1px solid var(--theme-mode-header-btn-border)',
            color: 'inherit',
          }}
        >
          Change <span className="text-[10px]">{panelOpen ? '▴' : '▾'}</span>
        </button>
      </div>

      {/* Мобильная шапка (<640px): две строки — имя + «⇄ Change», мета ниже */}
      <div
        className="flex flex-col gap-[9px] rounded-[16px] px-3.5 pb-3 pt-3.5 sm:hidden"
        style={{
          background: 'var(--theme-primary)',
          color: 'var(--theme-mode-accent-text)',
          boxShadow: 'var(--theme-mode-card-shadow)',
        }}
      >
        <div className="flex items-center justify-between gap-3">
          <div className="flex min-w-0 items-center gap-2.5">
            {headerCategoryBadge && (
              <span
                className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-base font-extrabold leading-none"
                style={{ background: 'var(--theme-mode-accent-text)', color: 'var(--theme-primary)' }}
              >
                {headerCategoryBadge}
              </span>
            )}
            <div
              dir="rtl"
              className="min-w-0 overflow-hidden text-ellipsis whitespace-nowrap text-[17px] font-extrabold"
              style={{ textAlign: 'left' }}
            >
              {headerName}
            </div>
          </div>
          <button
            type="button"
            onClick={() => setPanelOpen((v) => !v)}
            className="flex min-h-[44px] shrink-0 cursor-pointer items-center gap-1.5 whitespace-nowrap rounded-xl px-3.5 text-[13px] font-extrabold"
            style={{
              background: 'rgba(127,127,127,0.18)',
              border: '1px solid var(--theme-mode-header-btn-border)',
              color: 'inherit',
            }}
          >
            ⇄ Change
          </button>
        </div>
        <div className="text-xs font-semibold opacity-85">{headerMeta}</div>
      </div>

      {/* Desktop: дропдаун под шапкой; mobile: bottom sheet */}
      {panelOpen && !isMobile && (
        <div
          className="dv-sel-panel-drop absolute left-0 right-0 z-20"
          style={{ top: 'calc(100% + 10px)', filter: 'drop-shadow(0 18px 30px rgba(0,0,0,0.16))' }}
        >
          {panel(false)}
        </div>
      )}
      {panelOpen && isMobile && sheet}
    </div>
  );
};

export default DataSourceDDL;
