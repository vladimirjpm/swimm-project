import React from 'react';
import { createPortal } from 'react-dom';
import './filter-data-source-ddl.css';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import { Result } from '../../../utils/interfaces/results';
import {
  RESULTS_CATEGORIES,
  resolveCategoryKey,
  ResultsCategory,
} from '../../../utils/constants/results-categories';
import CategoryHelper, { CategoryDisplay } from '../../../utils/helpers/category-helper';
import ResultsLoadModeHelper from '../../../utils/helpers/results-load-mode';

// ── Категорийный селектор соревнований ──────────────────────────────────────
// База: design_handoff_category_selector (табы + поиск + сезон + live/upcoming +
// месячные группы + архив). Дельта design_handoff_selector_all: выбор сворачивает
// селектор в акцентную шапку; «Change» открывает панель как дропдаун (desktop)
// или bottom sheet (mobile, <640px); deep-link ?category= рендерит панель inline.

// Постраничная выгрузка результатов с серверного API (/api/results).
// Поля ResultDto совпадают с клиентским Result — конвертация не нужна.
// В dev работает через Vite-прокси /api → http://localhost:5078.
//
// Режим загрузки (админ-настройка ResultsLoadMode + ?loadMode=, см. ResultsLoadModeHelper):
//   'full'  — качаем ВСЕ страницы соревнования и фильтруем на клиенте (текущее поведение);
//   'paged' — шов под фазу 3 (этап 3.2 роадмапа): фильтры уйдут на сервер query-параметрами,
//             грузиться будет только запрошенная страница. Пока намеренно ведёт себя как
//             'full' — включать в админке имеет смысл только после реализации 3.2.
const loadFromApi = async (
  params: Record<string, string> = {},
): Promise<Result[]> => {
  const mode = await ResultsLoadModeHelper.getMode();
  if (mode === 'paged') {
    // TODO(фаза 3.2): серверная фильтрация + постраничная подгрузка вместо полного скачивания.
    console.info('[results] loadMode=paged ещё не реализован — работаем как full (фаза 3.2)');
  }
  const all: Result[] = [];
  let page = 1;
  const pageSize = 500; // максимум, отдаваемый сервером
  for (;;) {
    const qs = new URLSearchParams({
      ...params,
      page: String(page),
      pageSize: String(pageSize),
    });
    const resp = await fetch(`/api/results?${qs}`, { credentials: 'same-origin' });
    if (!resp.ok) throw new Error(`API /api/results вернул ${resp.status}`);
    const json = await resp.json();
    all.push(...((json.data ?? []) as Result[]));
    if (!json.hasMore) break;
    page += 1;
  }
  return all;
};

// Элемент /api/competitions: событие (свёрнуто в одну запись) или однодневное соревнование.
type CompetitionSource = {
  kind: 'event' | 'competition';
  id: number;
  name: string;
  date: string; // dd/MM/yyyy
  date_end?: string | null;
  pool_type: string;
  category: 'young8_11' | 'junior' | 'masters';
  status: 'live' | 'upcoming' | 'done';
  day_count: number;
  result_count: number;
};

const MONTHS_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const MONTHS_FULL = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

const parseDate = (d: string): Date | null => {
  const [day, month, year] = d.split('/').map(Number);
  if (!day || !month || !year) return null;
  return new Date(year, month - 1, day);
};

// «16 Jun» / «1–3 Jul» / «28 Jun – 2 Jul» / для upcoming — «starts 12 Jul»
const dateLabel = (src: CompetitionSource): string => {
  const start = parseDate(src.date);
  if (!start) return src.date;
  const startLabel = `${start.getDate()} ${MONTHS_SHORT[start.getMonth()]}`;
  if (src.status === 'upcoming') return `starts ${startLabel}`;
  const end = src.date_end ? parseDate(src.date_end) : null;
  if (end && end.getTime() !== start.getTime()) {
    return end.getMonth() === start.getMonth()
      ? `${start.getDate()}–${end.getDate()} ${MONTHS_SHORT[end.getMonth()]}`
      : `${startLabel} – ${end.getDate()} ${MONTHS_SHORT[end.getMonth()]}`;
  }
  return startLabel;
};

const monthLabel = (src: CompetitionSource): string => {
  const d = parseDate(src.date);
  return d ? `${MONTHS_FULL[d.getMonth()]} ${d.getFullYear()}` : '';
};

// Архив: завершённые старше начала прошлого месяца прячутся за «Show N earlier».
const isArchived = (src: CompetitionSource, now: Date): boolean => {
  if (src.status !== 'done') return false;
  const d = parseDate(src.date);
  if (!d) return false;
  const boundary = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  return d < boundary;
};

const sourceUrlParam = (src: CompetitionSource): [string, string] =>
  src.kind === 'event' ? ['eventId', String(src.id)] : ['competitionId', String(src.id)];

// Обновляем только свои параметры, не трогая чужие. id и category взаимоисключающие.
const writeUrl = (
  patch: { category?: string; source?: CompetitionSource | null; season?: number | 'all' | null },
) => {
  const params = new URLSearchParams(window.location.search);
  ['category', 'cat', 'competitionId', 'eventId'].forEach((k) => params.delete(k));
  if (patch.source) {
    const [k, v] = sourceUrlParam(patch.source);
    params.set(k, v);
  } else if (patch.category) {
    params.set('category', patch.category);
  }
  if (patch.season != null) params.set('season', String(patch.season));
  else params.delete('season');
  const qs = params.toString();
  window.history.replaceState(null, '', qs ? `?${qs}` : window.location.pathname);
};

// Брейкпоинт дропдаун ↔ bottom sheet: <640px (Tailwind sm)
const useIsMobile = () => {
  const [mobile, setMobile] = React.useState(
    () => typeof window !== 'undefined' && window.matchMedia('(max-width: 639px)').matches,
  );
  React.useEffect(() => {
    const mq = window.matchMedia('(max-width: 639px)');
    const onChange = (e: MediaQueryListEvent) => setMobile(e.matches);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);
  return mobile;
};

// Компонент может монтироваться несколько раз — грузить данные
// по URL-параметрам должен только один инстанс.
let urlLoadTriggered = false;

const DataSourceDDL: React.FC = () => {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((s) => s.filterSelected);
  // Выбранная строка — из store (title = имя источника): синхронизирует инстансы.
  const selectedTitle = useAppSelector((s) => s.dataSourceSelected?.title);
  const isMobile = useIsMobile();

  const [sources, setSources] = React.useState<CompetitionSource[]>([]);
  const [loaded, setLoaded] = React.useState(false);
  const [cat, setCat] = React.useState<string>('all');
  const [search, setSearch] = React.useState('');
  const [season, setSeason] = React.useState<number | 'all' | null>(null);
  const [seasonTouched, setSeasonTouched] = React.useState(false);
  const [archiveOpen, setArchiveOpen] = React.useState(false);
  // Панель поверх контента при выбранном соревновании (кнопка «Change» в шапке)
  const [panelOpen, setPanelOpen] = React.useState(false);
  // Мобильное меню категорий (вариант 9b: строка «Category: … ▾» вместо табов)
  const [catMenuOpen, setCatMenuOpen] = React.useState(false);
  // name/badge категорий из /api/categories (БД) — ключ здесь канонический ('young8_11' и т.п.),
  // не Category.Key в БД (маппинг внутри CategoryHelper). Пусто до первой загрузки — тогда
  // используется статичный RESULTS_CATEGORIES.label (см. categoryLabel ниже).
  const [liveCategoryLabels, setLiveCategoryLabels] = React.useState<Record<string, CategoryDisplay>>({});
  const wrapRef = React.useRef<HTMLDivElement>(null);
  const touchStartY = React.useRef<number | null>(null);

  React.useEffect(() => {
    let cancelled = false;
    CategoryHelper.getCanonicalMap().then((map) => {
      if (!cancelled) setLiveCategoryLabels(map);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // Живой name+badge из БД (Admin/Categories), пока не загружен — статичный label.
  const categoryLabel = (c: ResultsCategory): string => {
    const live = liveCategoryLabels[c.key];
    if (!live) return c.label;
    return live.badge ? `${live.badge} ${live.name}` : live.name;
  };

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

  // Закрытие по Esc (всегда) и по клику вне (только desktop-дропдаун —
  // у мобильного sheet эту роль играет backdrop).
  React.useEffect(() => {
    if (!panelOpen) return;
    const onDown = (e: MouseEvent) => {
      if (isMobile) return;
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setPanelOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPanelOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [panelOpen, isMobile]);

  // Панель закрылась → сворачиваем и меню категорий
  React.useEffect(() => {
    if (!panelOpen) setCatMenuOpen(false);
  }, [panelOpen]);

  // Body scroll lock, пока открыт мобильный sheet
  React.useEffect(() => {
    if (!(panelOpen && isMobile)) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [panelOpen, isMobile]);

  React.useEffect(() => {
    const load = async () => {
      try {
        const resp = await fetch('/api/competitions', { credentials: 'same-origin' });
        setSources(resp.ok ? ((await resp.json()) as CompetitionSource[]) : []);
      } catch (e) {
        console.error('Error loading /api/competitions', e);
        setSources([]);
      }
      setLoaded(true);
    };
    load();
  }, []);

  // Загрузка результатов выбранного соревнования в store.
  // Флаги датасета (is_masters/is_award/show_combine) берём из самих результатов —
  // ResultDto несёт их денормализованно per-competition.
  const loadSource = async (src: CompetitionSource) => {
    const [k, v] = sourceUrlParam(src);
    let data: Result[];
    try {
      data = await loadFromApi({ [k]: v });
    } catch (e) {
      console.error('Error loading data source', e);
      return;
    }

    const showCombine =
      data.length > 0 && data.every((r) => r.show_combine_all_results === true);
    const isMasters = data.some((r) => r.is_masters === true);
    const isAward = data.some((r) => r.is_award === true);

    const dates = data.map((r) => r.date).filter(Boolean) as string[];
    const maxDate = dates.length
      ? dates.reduce((max, curr) =>
          (parseDate(curr)?.getTime() ?? 0) > (parseDate(max)?.getTime() ?? 0) ? curr : max,
        )
      : '';
    const [d, m, y] = maxDate.split('/');
    const iso = maxDate ? `${y}-${m.padStart(2, '0')}-${d.padStart(2, '0')}` : '';

    dispatch(
      rootActions.updateState({
        dataSourceSelected: {
          results: data,
          title: src.name,
          is_masters: isMasters,
          is_award: isAward,
        },
        showCombineAllResults: showCombine,
        ...(maxDate
          ? { filterSelected: { ...filters, date: maxDate, date_str: iso } }
          : {}),
      }),
    );
  };

  const selectSource = (src: CompetitionSource) => {
    writeUrl({ source: src, season: seasonTouched ? season : null });
    setPanelOpen(false);
    void loadSource(src);
  };

  // Инициализация из URL: ?category= (легаси ?cat=) активирует таб;
  // ?competitionId=<id|last> / ?eventId=<id> грузит соревнование сразу.
  const initializedRef = React.useRef(false);
  React.useEffect(() => {
    if (!loaded || initializedRef.current) return;
    initializedRef.current = true;

    const params = new URLSearchParams(window.location.search);
    const seasonRaw = params.get('season');
    if (seasonRaw === 'all') {
      setSeason('all');
      setSeasonTouched(true);
    } else if (Number(seasonRaw)) {
      setSeason(Number(seasonRaw));
      setSeasonTouched(true);
    }

    const competitionId = params.get('competitionId');
    const eventId = params.get('eventId');

    let target: CompetitionSource | undefined;
    if (competitionId === 'last') {
      target = sources[0]; // /api/competitions отсортирован по дате desc
    } else if (eventId) {
      target = sources.find((s) => s.kind === 'event' && String(s.id) === eventId);
    } else if (competitionId) {
      target = sources.find((s) => s.kind === 'competition' && String(s.id) === competitionId);
    }

    if (target) {
      setCat(target.category);
      if (!urlLoadTriggered) {
        urlLoadTriggered = true;
        void loadSource(target);
      }
      return;
    }
    // Конкретный id вне списка (например, день события) — грузим напрямую, таб не трогаем.
    // Флаги датасета обязательны и здесь: без is_award медали соревнования не считаются.
    if (competitionId || eventId) {
      if (!urlLoadTriggered) {
        urlLoadTriggered = true;
        void loadFromApi(eventId ? { eventId } : { competitionId: competitionId! }).then(
          (data) =>
            data.length &&
            dispatch(
              rootActions.updateState({
                dataSourceSelected: {
                  results: data,
                  title: data[0].competition,
                  is_masters: data.some((r) => r.is_masters === true),
                  is_award: data.some((r) => r.is_award === true),
                },
                showCombineAllResults:
                  data.length > 0 && data.every((r) => r.show_combine_all_results === true),
              }),
            ),
        );
      }
      return;
    }

    setCat(resolveCategoryKey(params.get('category') ?? params.get('cat')));
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loaded, sources]);

  // ── Производные списки ──
  const seasons = React.useMemo(() => {
    const years = new Set(
      sources.map((s) => parseDate(s.date)?.getFullYear()).filter(Boolean) as number[],
    );
    return [...years].sort((a, b) => b - a);
  }, [sources]);
  const activeSeason = season ?? seasons[0] ?? new Date().getFullYear();

  const now = new Date();
  const inCat = (s: CompetitionSource) => cat === 'all' || s.category === cat;
  const inSeason = (s: CompetitionSource) =>
    activeSeason === 'all' || parseDate(s.date)?.getFullYear() === activeSeason;
  const matchesSearch = (s: CompetitionSource) =>
    !search.trim() || s.name.toLowerCase().includes(search.trim().toLowerCase());

  const visible = sources.filter((s) => inCat(s) && inSeason(s) && matchesSearch(s));
  const nowItems = visible.filter((s) => s.status !== 'done');
  const doneItems = visible.filter((s) => s.status === 'done');
  const archivedItems = doneItems.filter((s) => isArchived(s, now));
  const shownDone = archiveOpen ? doneItems : doneItems.filter((s) => !isArchived(s, now));

  const monthGroups: { label: string; items: CompetitionSource[] }[] = [];
  shownDone.forEach((s) => {
    const label = monthLabel(s);
    let g = monthGroups.find((x) => x.label === label);
    if (!g) {
      g = { label, items: [] };
      monthGroups.push(g);
    }
    g.items.push(s);
  });

  const activeCategory = RESULTS_CATEGORIES.find((c) => c.key === cat);
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
            {s.name}
            {s.day_count > 1 ? ` (${s.day_count} дн.)` : ''}
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

  const catCount = (key: string) =>
    key === 'all' ? sources.length : sources.filter((s) => s.category === key).length;

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
      {RESULTS_CATEGORIES.map((c) => {
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
          {RESULTS_CATEGORIES.map((c) => {
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
            Season {y}
          </option>
        ))}
      </select>
    ) : null;

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
  const panel = (inline: boolean) => (
    <div
      className="dv-data-source rounded-[18px] px-[22px] py-5 max-sm:px-3.5"
      style={{
        background: 'var(--theme-mode-surface)',
        border: '1px solid var(--theme-mode-border)',
        boxShadow: 'var(--theme-mode-card-shadow)',
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
        {seasonSelect}
      </div>
      {listBody}
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

  // С выбором — компактная шапка соревнования; «Change» открывает панель поверх контента
  const seasonYear = parseDate(selectedSourceObj.date)?.getFullYear();
  const headerName = `${selectedSourceObj.name}${
    selectedSourceObj.day_count > 1 ? ` (${selectedSourceObj.day_count} дн.)` : ''
  }`;
  const headerMeta = [
    dateLabel(selectedSourceObj),
    selectedSourceObj.pool_type,
    monthLabel(selectedSourceObj),
    seasonYear ? `Season ${seasonYear}` : '',
  ]
    .filter(Boolean)
    .join(' · ');
  // Бейдж категории соревнования в шапке — «выбит» в акцентном фоне (--theme-mode-accent-text),
  // а не currentColor-обводка, как в табах: тут фон всегда сплошной --theme-primary,
  // так что можно уверенно закрасить кружок, а не только обвести.
  const headerCategoryBadge = liveCategoryLabels[selectedSourceObj.category]?.badge;

  return (
    <div ref={wrapRef} className="relative">
      {/* Desktop-шапка: одна строка ~72px */}
      <div
        className="hidden items-center justify-between gap-4 rounded-[14px] px-5 py-4 sm:flex"
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
