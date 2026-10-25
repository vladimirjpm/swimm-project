import React, { useCallback, useMemo, useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import './my-media.css';
import { useAuth } from '../../hooks/useAuth';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useFavorites } from '../../hooks/useFavorites';
import { useMyMediaPublications, type PublishTargetRef } from '../../hooks/useUserMedia';
import { useMyHubGroups } from '../hub-groups-project/use-my-hub-groups';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import { useAllMyMedia, AllUserMediaDto, AddMediaInput } from './use-all-my-media';
import { MySwimDto, SwimMediaDto, seasonLabel } from './use-my-swims';
import { useMyMediaModeration } from './use-my-media-moderation';
import { useMySwimsSeason } from './use-my-swims-season';
import { useLikeOverrides } from './use-like-overrides';
import { useMediaSummary } from './use-media-summary';
import { useSwimFilters, swimBelongsTo } from './use-swim-filters';
import { useShareDialog } from './use-share-dialog';
import { useProfileSwimmers } from './use-profile-swimmers';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_SwimmerGallery from '../components/mix/swimmer-gallery/swimmer-gallery';
import { routes } from '../../utils/routes';
import { GalleryItem } from '../../utils/interfaces/results';
import MediaCard from './components/media-card';
import AddLinkModal from './components/add-link-modal';
import ShareDialog from './components/share-dialog';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import ModerationPanel from './components/moderation-panel';
import SwimList from './components/swim-list';
import MyMediaFilterPanel, {
  DEFAULT_OPEN_CARDS,
  type GroupFilter, type MediaCardKey,
} from './components/my-media-filter-panel';
import FilterBar, { type FilterBarChip } from '../components/filter-section/filter-bar';
import FiltersFab from '../components/filter-section/filters-fab';
import UI_SwimmStyleIcon from '../components/mix/swimm-style-icon/swimm-style-icon';
import { useMyMediaFilterHost, type MyMediaHostState } from './my-media-filter-host';
import MobileFiltersDrawer from '../components/filter-section/mobile-filters-drawer';
import UI_CompetitionName from '../components/mix/competition-name/competition-name';
import {
  hpCardCls, STATUS_COLORS, type CardStatus,
} from './components/status-styles';

// Страница «My media» v3 (swim-centric) — README design_handoff_my_swims_v3,1.
// Палитра — тема deep (light + dark), как у страниц пловца, клуба и /season-best: вёрстка
// просит роли `--t-*`, карта ролей на `--deep-*` живёт в my-media.css (Ф5).
//
// Логика — в хуках рядом: сезон и агрегат заплывов (use-my-swims-season), реакции
// (use-like-overrides), сводки без фильтров (use-media-summary), фильтры и что после них
// видно (use-swim-filters), публикация в группу (use-share-dialog), пловцы профиля
// (use-profile-swimmers). Здесь — оверлеи, связка панели фильтров с полосой и разметка.

function MyMedia() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();
  // Класс темы deep (`.theme-deep` / `.theme-deep-light`) под текущий режим сайта: токены
  // `--deep-*` объявлены на классе, а не на `:root`, и без него страница рисуется пустыми
  // переменными (Ф5).
  const deep = useDeepThemeClass();

  if (auth.loading) {
    return <div className="min-h-screen bg-[var(--deep-page-bg)]" />;
  }

  if (!auth.isAuthenticated) {
    return (
      <div
        className={`my-media ${deep} relative flex min-h-screen items-center justify-center overflow-x-clip px-4 text-[var(--t-text)]`}
        style={{ background: 'var(--deep-hero-grad)' }}
      >
        <AppTopbar />
        <div className="absolute inset-0 flex items-center justify-center">
          <div className="w-full max-w-md rounded-[18px] border border-[var(--t-border)] bg-[var(--t-card)] p-8 text-center shadow-[var(--t-shadow)]">
            <h1 className="mb-2 text-lg font-black text-[var(--t-text)]">My media</h1>
            <p className="mb-4 text-sm text-[var(--t-text-2)]">Sign in to manage your media</p>
            <button
              type="button"
              onClick={openLoginModal}
              className="hp-mono rounded-[11px] bg-[var(--t-accent)] px-4 py-2 text-sm font-extrabold text-[var(--t-accent-ink)]"
            >
              Sign in
            </button>
          </div>
        </div>
        {/* И на заглушке тоже: она уже покрашена темой, и без кнопки режим отсюда не сменить. */}
        <UI_ModeToggle />
      </div>
    );
  }

  return <MyMediaContent deep={deep} />;
}

function MyMediaContent({ deep }: { deep: string }) {
  const auth = useAuth();
  const favorites = useFavorites();
  const { media: allMedia, remove, add } = useAllMyMedia();
  const { publications, submit: submitPublication, withdraw: withdrawPublication } = useMyMediaPublications();
  const { groups: myGroups } = useMyHubGroups(auth.isAuthenticated);

  const showModeration = auth.isAdmin || myGroups.length > 0;
  const moderation = useMyMediaModeration(showModeration);

  const [tab, setTab] = useState<'media' | 'moderation'>('media');

  // Порядок: агрегат заплывов (эффект с запросом) стоит там же, где стоял до деления —
  // после модерации, до остальных хуков страницы.
  const {
    setSeason, data, loading, reload,
    effectiveSeason, seasonChoices, carouselSeason, pickSeason, seasonOptions,
  } = useMySwimsSeason();
  const { swims, competitionMedia, unlinkedMedia, onToggleLike } = useLikeOverrides(data);
  const { publicationsByMedia, competitionOptions, mediaStats, groupOptions } = useMediaSummary({
    swims, competitionMedia, unlinkedMedia, publications,
  });

  // ── Фильтры ────────────────────────────────────────────────────────────────
  const {
    swimmerFilter, setSwimmerFilter,
    seg, setSeg,
    statusFilter, setStatusFilter,
    groupFilter, setGroupFilter,
    competitionFilter, setCompetitionFilter,
    styleFilter, setStyleFilter,
    distanceFilter, setDistanceFilter,
    dateFrom, setDateFrom,
    dateTo, setDateTo,
    bySwimmer, segCount, filtered,
    visibleCompetitionMedia, visibleUnlinkedMedia,
    activeFilterCount, clearAll,
  } = useSwimFilters({ swims, competitionMedia, unlinkedMedia, publicationsByMedia });
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);
  const [unlinkedOpen, setUnlinkedOpen] = useState(false);

  // ── Модалы / оверлеи ──────────────────────────────────────────────────────
  const [addOpen, setAddOpen] = useState(false);
  const [addVideoSwim, setAddVideoSwim] = useState<MySwimDto | null>(null);
  const [addCompTarget, setAddCompTarget] = useState<{ id: number; name: string } | null>(null);
  const [linkSwimTarget, setLinkSwimTarget] = useState<AllUserMediaDto | null>(null);
  const share = useShareDialog({ publicationsByMedia, submitPublication, withdrawPublication });
  const { openShare, submitInlineShare } = share;

  const [lightboxItems, setLightboxItems] = useState<GalleryItem[]>([]);
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);

  // Открыт ли поверх страницы хоть один слой. Нужно ровно одному потребителю —
  // переключателю тем внизу справа (он единственный, у кого z-index выше всех оверлеев).
  // Заводя новый оверлей, добавляй его сюда, иначе кружок повиснет поверх него.
  const anyOverlayOpen =
    mobileFiltersOpen || addOpen || lightboxIndex !== null ||
    addVideoSwim !== null || addCompTarget !== null || linkSwimTarget !== null;

  const swimmerNames = useMemo(() => new Map(data.swimmers.map((s) => [s.id, s.name])), [data.swimmers]);

  const pickGroup = (v: GroupFilter) => {
    setGroupFilter(v);
    // Медиа без заплыва тоже фильтруется — иначе выбранная группа «пропадает» в свёрнутой секции.
    if (v !== 'all') setUnlinkedOpen(true);
  };

  // ── Лайтбокс ──────────────────────────────────────────────────────────────
  const onPlay = (m: SwimMediaDto) => {
    if (m.media_type === 'video' && (m.source_type === 'youtube' || m.source_type === 'vimeo')) {
      setLightboxItems([{ type: 'video', sourceType: m.source_type as GalleryItem['sourceType'], url: m.url }]);
      setLightboxIndex(0);
    } else if (m.media_type === 'image') {
      setLightboxItems([{ type: 'image', url: m.url }]);
      setLightboxIndex(0);
    } else {
      window.open(m.url, '_blank', 'noopener');
    }
  };

  // ── Добавление / удаление ─────────────────────────────────────────────────
  const handleAdd = async (input: AddMediaInput): Promise<true | string> => {
    const { item, error } = await add(input);
    if (item) { await reload(); return true; }
    return error;
  };

  const handleDelete = async (mediaId: number) => {
    const ok = await remove(mediaId);
    if (ok) await reload();
  };

  const { orderedSwimmers, addLinkSwimmers } = useProfileSwimmers(data.swimmers, favorites, allMedia);

  const totalCount = bySwimmer.length;
  const pendingModCount = moderation.rows.filter((r) => r.status === 'pending').length;
  // Выбран чип пловца — имя должно быть видно и вне чипа: в строках оно скрыто (фильтр же
  // один на всех), и экран переставал отвечать на вопрос «чьи это заплывы».
  const selectedSwimmerName = swimmerFilter === 'all' ? null : swimmerNames.get(swimmerFilter) ?? null;

  // Через общий шов идут только стиль и дистанция — единственные фильтры кабинета, для
  // которых в общей модели есть поля (см. `my-media-filter-host.ts`).
  const onHostChange = useCallback((patch: Partial<MyMediaHostState>) => {
    if (patch.style !== undefined) setStyleFilter(patch.style);
    if (patch.distance !== undefined) setDistanceFilter(patch.distance);
  }, []);
  const filterHost = useMyMediaFilterHost({
    swims,
    state: { style: styleFilter, distance: distanceFilter },
    onChange: onHostChange,
    onReset: clearAll,
  });

  // Панель одна, а мест у неё два — сайдбар и шторка. Элемент можно переиспользовать:
  // React смонтирует по экземпляру на место, и раскрытые карточки у них свои.
  // Раскрытость карточек держит страница, а не карточка: по колонке полосы нужно раскрыть
  // именно её карточку. Открытых бывает несколько — это не аккордеон.
  const [openCards, setOpenCards] = useState<Set<MediaCardKey>>(() => new Set(DEFAULT_OPEN_CARDS));
  const setCardOpen = useCallback((key: MediaCardKey, open: boolean) => {
    setOpenCards((prev) => {
      const next = new Set(prev);
      if (open) next.add(key); else next.delete(key);
      return next;
    });
  }, []);
  // Клик по колонке полосы: раскрыть карточку, а на узком экране ещё и открыть шторку —
  // иначе раскрытая карточка осталась бы за кадром.
  const revealCard = (key: MediaCardKey) => {
    setCardOpen(key, true);
    if (window.matchMedia('(max-width: 1023px)').matches) setMobileFiltersOpen(true);
  };

  const countLabel = `${filtered.length} ${filtered.length === 1 ? 'swim' : 'swims'}`;

  /** Ячейка сезона в полосе: индикатор со стрелками ±1 сезон. Выбор — каруселью в панели. */
  const seasonIdx = carouselSeason == null ? -1 : seasonChoices.indexOf(carouselSeason);
  const seasonCell = (
    <div className="mmb-season">
      <span className="mmb-season__label">Season</span>
      <div className="mmb-season__row">
        {/* seasonChoices идёт от свежего к старому, поэтому «‹» — это шаг ВПЕРЁД по списку. */}
        <button
          type="button"
          className="mmb-season__step"
          aria-label="Previous season"
          disabled={seasonIdx < 0 || seasonIdx >= seasonChoices.length - 1}
          onClick={() => pickSeason(seasonChoices[seasonIdx + 1])}
        >
          ‹
        </button>
        <span className="mmb-season__value">
          {carouselSeason == null ? '∞' : seasonLabel(carouselSeason).slice(2)}
        </span>
        <button
          type="button"
          className="mmb-season__step"
          aria-label="Next season"
          disabled={seasonIdx <= 0}
          onClick={() => pickSeason(seasonChoices[seasonIdx - 1])}
        >
          ›
        </button>
      </div>
    </div>
  );

  /** Чипы полосы — зеркало панели, по одному на карточку-фильтр (сезон стоит ведущей ячейкой). */
  const barChips: FilterBarChip[] = [
    {
      key: 'swimmer',
      label: 'Swimmer',
      active: swimmerFilter !== 'all',
      value: <span dir="auto">{selectedSwimmerName}</span>,
      onClick: () => revealCard('swimmers'),
    },
    {
      key: 'video',
      label: 'Video',
      active: seg !== 'all',
      value: seg === 'with' ? 'With video' : 'No video',
      onClick: () => revealCard('video'),
    },
    {
      key: 'event',
      label: 'Event',
      active: styleFilter !== 'all',
      value: (
        <span className="mmb-event">
          <UI_SwimmStyleIcon
            styleName={styleFilter}
            styleLen={distanceFilter === 'all' ? '' : distanceFilter}
            styleType="icon-len"
            className="src-my-media"
          />
        </span>
      ),
      onClick: () => revealCard('style'),
    },
    {
      key: 'competition',
      label: 'Competition',
      active: competitionFilter !== 'all',
      value: (() => {
        const c = competitionOptions.find((o) => o.id === competitionFilter);
        return <UI_CompetitionName name={c?.name} isChampionship={c?.isChampionship} />;
      })(),
      onClick: () => revealCard('competition'),
    },
    {
      key: 'date',
      label: 'Date',
      active: !!(dateFrom || dateTo),
      value: dateFrom && dateTo ? `${dateFrom} – ${dateTo}` : dateFrom || dateTo,
      onClick: () => revealCard('date'),
    },
    {
      key: 'group',
      label: 'Shared with',
      shortLabel: 'Shared',
      active: groupFilter !== 'all',
      value: (
        <span dir="auto">
          {groupFilter === 'none'
            ? 'Not shared'
            : groupOptions.groups.find((g) => g.id === groupFilter)?.name}
        </span>
      ),
      onClick: () => revealCard('group'),
    },
    {
      key: 'status',
      label: 'Status',
      active: statusFilter !== 'all',
      value: statusFilter,
      onClick: () => revealCard('status'),
    },
  ];


  const filterPanel = (
    <MyMediaFilterPanel
      host={filterHost}
      seasons={seasonOptions}
      season={carouselSeason}
      onSeason={pickSeason}
      swimmers={orderedSwimmers.map((sw) => ({
        id: sw.id,
        name: sw.name,
        count: swims.filter((x) => swimBelongsTo(x, sw.id)).length,
      }))}
      swimmerFilter={swimmerFilter}
      onSwimmer={setSwimmerFilter}
      totalSwims={swims.length}
      openCards={openCards}
      onCardOpenChange={setCardOpen}
      seg={seg}
      onSeg={setSeg}
      segCount={segCount}
      statusFilter={statusFilter}
      onStatus={setStatusFilter}
      groupFilter={groupFilter}
      onGroup={pickGroup}
      groupOptions={groupOptions}
      competitionFilter={competitionFilter}
      onCompetition={setCompetitionFilter}
      competitionOptions={competitionOptions}
      dateFrom={dateFrom}
      dateTo={dateTo}
      onDateFrom={setDateFrom}
      onDateTo={setDateTo}
      activeCount={activeFilterCount}
    />
  );

  const swimListCallbacks = {
    publicationsByMedia,
    onPlay,
    onAddVideo: (s: MySwimDto) => setAddVideoSwim(s),
    onAddCompMedia: (id: number, name: string) => setAddCompTarget({ id, name }),
    onSubmitShare: submitInlineShare,
    onWithdraw: (mediaId: number, target: PublishTargetRef) => withdrawPublication(mediaId, target),
    onDelete: handleDelete,
    onToggleLike,
  };


  return (
    <div
      className={`my-media ${deep} relative min-h-screen overflow-x-clip pb-24 text-[var(--t-text)]${
        tab === 'moderation' ? ' my-media--moderation' : ''
      }`}
      style={{ background: 'var(--deep-hero-grad)' }}
    >
      <AppTopbar />

      {/* Шапка (хендофф 2a/2c): имя · заголовок · папки-табы · «+ Add link». Чипы
          `Media` / `My groups ↗` / `Settings · soon` сняты: первый повторял заголовок,
          второй уехал в меню аватара топбара, третий обещал несуществующее. */}
      <header className="mm-head">
        <div className="mm-head__id">
          <p className="mm-head__eyebrow deep-on-hero">My profile · {auth.displayName || auth.email}</p>
          <h1 className="mm-head__title">My media</h1>
        </div>

        <nav className="mm-tabs" aria-label="Profile sections">
          <button
            type="button"
            onClick={() => setTab('media')}
            className={`mm-tab${tab === 'media' ? ' mm-tab--on' : ''}`}
            aria-current={tab === 'media' ? 'page' : undefined}
          >
            <span className="mm-tab__title">My swims</span>
            <span className="mm-tab__sub">
              {totalCount} swims · {data.all_seasons ? '∞' : seasonLabel(effectiveSeason).slice(2)}
            </span>
          </button>
          {showModeration && (
            <button
              type="button"
              onClick={() => setTab('moderation')}
              className={`mm-tab${tab === 'moderation' ? ' mm-tab--on' : ''}`}
              aria-current={tab === 'moderation' ? 'page' : undefined}
            >
              <span className="mm-tab__title">Moderation</span>
              <span className={`mm-tab__sub${pendingModCount > 0 ? ' mm-tab__sub--warn' : ''}`}>
                {pendingModCount > 0 && <span className="mm-tab__badge">{pendingModCount}</span>}
                {pendingModCount > 0 ? 'waiting' : 'all clear'}
              </span>
            </button>
          )}
        </nav>

        <button type="button" className="mm-add" onClick={() => setAddOpen(true)}>
          + Add link
        </button>
      </header>

      <section className="mm-panel">
        {tab === 'media' ? (
          <div className="flex flex-col gap-4">
            {/* Шапка панели: слева сводка «что у меня есть» (видна всегда), справа баннер
                модерации (только когда есть что решать). Раньше баннер занимал всю ширину
                один — теперь он прижат вправо и делит ряд со сводкой. */}
            <div className="flex flex-col gap-3 lg:flex-row lg:items-stretch">
              <div className={`${hpCardCls} flex flex-wrap items-center gap-x-5 gap-y-2.5 p-[12px_16px]`}>
                <div className="flex items-baseline gap-2">
                  <span className="text-[22px] font-black leading-none text-[var(--t-text)]">{mediaStats.videos}</span>
                  <span className="hp-mono text-[11px] font-extrabold text-[var(--t-text-3)]">
                    {mediaStats.videos === 1 ? 'video' : 'videos'}
                  </span>
                  {mediaStats.photos > 0 && (
                    <span className="text-[11.5px] font-bold text-[var(--t-text-3)]">
                      + {mediaStats.photos} {mediaStats.photos === 1 ? 'photo' : 'photos'}
                    </span>
                  )}
                </div>
                {/* Статусы — цвет И слово, как в чипах карточек: цветом одним статус не
                    называют. Нулевые не прячем, иначе строка прыгает при каждом решении.
                    Каждый чип — тот же фильтр, что карточка «Publication status» в панели
                    (одно состояние `statusFilter` на оба места, второго списка нет).
                    Повторный клик по выбранному снимает фильтр; пустой статус не кликается —
                    он увёл бы страницу в «Nothing matches the filters» без причины. */}
                <div className="flex flex-wrap items-center gap-1.5">
                  {(['private', 'pending', 'published', 'rejected'] as CardStatus[]).map((k) => {
                    const count = mediaStats.status[k];
                    const on = statusFilter === k;
                    return (
                      <button
                        key={k}
                        type="button"
                        disabled={count === 0}
                        aria-pressed={on}
                        title={count === 0 ? `No ${k} media` : on ? 'Clear this filter' : `Show only ${k}`}
                        onClick={() => setStatusFilter(on ? 'all' : k)}
                        className={`hp-mono inline-flex items-center gap-1.5 whitespace-nowrap rounded-[8px] border px-2.5 py-[5px] text-[11px] font-extrabold${
                          count === 0 ? ' cursor-default opacity-45' : ' cursor-pointer'
                        }`}
                        style={{
                          color: STATUS_COLORS[k].text,
                          borderColor: on ? STATUS_COLORS[k].text : STATUS_COLORS[k].border,
                          background: STATUS_COLORS[k].bg,
                          // Выбранный — кольцом цвета статуса: заливка у чипов уже занята
                          // самим статусом, и «активным» её сделать нечем.
                          boxShadow: on ? `0 0 0 2px ${STATUS_COLORS[k].bg}, 0 0 0 3px ${STATUS_COLORS[k].text}` : undefined,
                        }}
                      >
                        <span className="text-[12.5px] font-black">{count}</span>
                        {k}
                      </button>
                    );
                  })}
                </div>
              </div>

              {showModeration && pendingModCount > 0 && (
                <div className="flex items-center gap-3 rounded-[14px] border border-[var(--t-warn-border)] bg-[var(--t-warn-soft)] p-[12px_16px] lg:ml-auto">
                  <span className="flex h-[22px] min-w-[22px] items-center justify-center rounded-[11px] bg-[var(--t-warn)] px-1.5 text-[12px] font-black text-[var(--t-warn-ink)]">
                    {pendingModCount}
                  </span>
                  <span className="min-w-0 text-[13.5px] font-bold text-[var(--t-warn)]">requests are waiting for your approval</span>
                  <button type="button" onClick={() => setTab('moderation')} className="hp-mono ml-auto rounded-[9px] border-none bg-[var(--t-warn)] px-3.5 py-[7px] text-[12px] font-extrabold text-[var(--t-warn-ink)] lg:ml-3">
                    Review →
                  </button>
                </div>
              )}
            </div>


            <div className="flex flex-col gap-4 lg:flex-row lg:items-start">
              {/* Сайдбар фильтров (десктоп). Ширина фиксированная, как на results: строка
                  заплыва тоже фиксированной ширины, и доля от экрана её ломала бы. */}
              <aside className="my-media-filters hidden w-[320px] shrink-0 lg:block">
                {filterPanel}
              </aside>

              <div className="flex min-w-0 flex-1 flex-col gap-4">
                {/* Полоса выбранного — ОБЩИЙ `FilterBar`, тот же, что на results и
                    `/season-best`. Колонки — зеркало панели: щелчок раскрывает карточку
                    того фильтра, по которому щёлкнули (на узком экране — открывает шторку). */}
                <FilterBar
                  className="my-media-bar"
                  desktop="columns"
                  rows="card"
                  lead={seasonCell}
                  aside={<span className="text-[11px] font-bold text-[var(--t-text-3)]">{countLabel}</span>}
                  chips={barChips}
                />
                <p className="m-0 hidden text-[11.5px] font-bold text-[var(--t-text-3)] md:block">
                  {selectedSwimmerName && (
                    <span dir="auto" className="mr-1.5 text-[12.5px] font-black text-[var(--t-accent)]">{selectedSwimmerName}</span>
                  )}
                  {countLabel} · sorted by date ↓
                </p>

                {/* Main list / states */}
                {loading ? (
                  <div className="flex flex-col gap-4">
                    {[0, 1].map((i) => (
                      <div key={i} className={`${hpCardCls} h-[140px] animate-pulse`} />
                    ))}
                  </div>
                ) : data.swimmers.length === 0 ? (
                  <div className={`${hpCardCls} p-[56px_40px] text-center`}>
                    <div className="text-[40px]">⭐</div>
                    <p className="m-0 mt-3 text-[17px] font-black text-[var(--t-text)]">No favorite swimmers yet</p>
                    <p className="mx-auto mt-2 max-w-[380px] text-[13px] leading-[1.5] text-[var(--t-text-2)]">
                      Add a swimmer to favorites — their swims will appear here and you can attach videos.
                    </p>
                    <a href={routes.results()} className="hp-mono mt-[18px] inline-block rounded-[10px] border-none bg-[var(--t-accent)] px-5 py-[10px] text-[13px] font-extrabold text-[var(--t-accent-ink)] no-underline">
                      Find swimmers →
                    </a>
                  </div>
                ) : swims.length === 0 ? (
                  <div className="rounded-[16px] border border-dashed border-[var(--t-border)] p-10 text-center">
                    <p className="m-0 text-[14px] font-bold text-[var(--t-text-2)]">
                      {data.all_seasons ? 'No results yet' : `No results in season ${seasonLabel(effectiveSeason)}`}
                    </p>
                    {data.seasons.filter((y) => y !== effectiveSeason).slice(0, 1).map((y) => (
                      <button key={y} type="button" onClick={() => setSeason(y)} className="hp-mono mt-3 rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3.5 py-[7px] text-[12px] font-extrabold text-[var(--t-accent)]">
                        Season {seasonLabel(y)} →
                      </button>
                    ))}
                  </div>
                ) : filtered.length === 0 ? (
                  <div className="rounded-[16px] border border-dashed border-[var(--t-border)] p-10 text-center">
                    <p className="m-0 text-[14px] font-bold text-[var(--t-text-2)]">Nothing matches the filters</p>
                    <button type="button" onClick={clearAll} className="hp-mono mt-3 rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3.5 py-[7px] text-[12px] font-extrabold text-[var(--t-accent)]">
                      Clear all
                    </button>
                  </div>
                ) : (
                  <SwimList
                    swims={filtered}
                    competitionMedia={visibleCompetitionMedia}
                    showSwimmerName={swimmerFilter === 'all' && data.swimmers.length > 1}
                    swimmerNames={swimmerNames}
                    preferredSwimmerId={swimmerFilter === 'all' ? null : swimmerFilter}
                    {...swimListCallbacks}
                  />
                )}

                {/* Unlinked media */}
                {visibleUnlinkedMedia.length > 0 && (
                  <div className="mt-2">
                    <button
                      type="button"
                      onClick={() => setUnlinkedOpen((v) => !v)}
                      className="hp-mono flex w-full items-center gap-2 rounded-[12px] border border-[var(--t-border)] bg-transparent px-4 py-[10px] text-left text-[12px] font-extrabold text-[var(--t-accent)]"
                    >
                      Unlinked media
                      <span className="inline-flex h-[18px] min-w-[18px] items-center justify-center rounded-[9px] bg-[var(--t-accent-soft)] px-1.5 text-[10.5px]">{visibleUnlinkedMedia.length}</span>
                      <span className="font-bold normal-case text-[var(--t-text-3)]">· club videos and general footage not tied to any swim</span>
                      <span className="ml-auto">{unlinkedOpen ? '▲' : '▼'}</span>
                    </button>
                    {unlinkedOpen && (
                      <div className="mt-3 grid gap-3.5" style={{ gridTemplateColumns: 'repeat(auto-fill,minmax(250px,1fr))' }}>
                        {visibleUnlinkedMedia.map((item) => (
                          <MediaCard
                            key={item.id}
                            item={item}
                            publications={publicationsByMedia.get(item.id) ?? []}
                            onOpenLightbox={() => onPlay(item)}
                            onDelete={() => handleDelete(item.id)}
                            onWithdraw={(target) => withdrawPublication(item.id, target)}
                            onLinkToSwim={() => setLinkSwimTarget(item)}
                            onShareWithGroup={() => openShare(item)}
                          />
                        ))}
                      </div>
                    )}
                    {unlinkedOpen && (
                      <button
                        type="button"
                        onClick={() => setAddOpen(true)}
                        className="hp-mono mt-3 rounded-[9px] border border-dashed border-[var(--t-accent-border)] bg-transparent px-3.5 py-[7px] text-[12px] font-extrabold text-[var(--t-accent-dim)]"
                      >
                        + Add link without a swim
                      </button>
                    )}
                  </div>
                )}
              </div>
            </div>
          </div>
        ) : (
          <ModerationPanel
            rows={moderation.rows}
            onDecide={async (targetType, targetId, publicationId, approve) => {
              await moderation.decide(targetType, targetId, publicationId, approve);
            }}
          />
        )}
      </section>

      {/* Floating + Add link (mobile) */}
      {tab === 'media' && (
        <button
          type="button"
          onClick={() => setAddOpen(true)}
          className="hp-mono fixed bottom-5 left-1/2 z-40 -translate-x-1/2 rounded-full border-none bg-[var(--t-accent)] px-6 py-3 text-[13px] font-extrabold text-[var(--t-accent-ink)] shadow-[var(--t-shadow)] sm:hidden"
        >
          + Add link
        </button>
      )}

      {/* Add link (global, 3 steps) */}
      {addOpen && (
        <AddLinkModal
          swimmers={addLinkSwimmers}
          onClose={() => setAddOpen(false)}
          onSave={handleAdd}
        />
      )}

      {/* Add video — swim pre-selected (single-step) */}
      {addVideoSwim && (
        <AddLinkModal
          swimmers={[{ id: addVideoSwim.swimmer_id, name: swimmerNames.get(addVideoSwim.swimmer_id) ?? '', hint: '' }]}
          initialSwimmerId={addVideoSwim.swimmer_id}
          fixedResultId={addVideoSwim.result_id}
          contextLabel={
            <span>
              <b>{addVideoSwim.distance}m {addVideoSwim.style}</b>
              <span className="hp-mono ml-2 text-[var(--t-accent)]">{addVideoSwim.time}</span>
              <span className="ml-2 text-[var(--t-text-2)]">
                <UI_CompetitionName name={addVideoSwim.competition_name} isChampionship={addVideoSwim.is_championship} /> · {addVideoSwim.competition_date}
              </span>
            </span>
          }
          onClose={() => setAddVideoSwim(null)}
          onSave={handleAdd}
        />
      )}

      {/* Add photo/video to the whole competition (single-step) */}
      {addCompTarget && (
        <AddLinkModal
          swimmers={addLinkSwimmers}
          initialSwimmerId={swimmerFilter !== 'all' ? swimmerFilter : data.swimmers[0]?.id}
          fixedCompetitionId={addCompTarget.id}
          contextLabel={
            <span>
              <b>Competition media</b> 📎
              <span dir="auto" className="ml-2 text-[var(--t-text-2)]">{addCompTarget.name}</span>
            </span>
          }
          onClose={() => setAddCompTarget(null)}
          onSave={handleAdd}
        />
      )}

      <ShareDialog share={share} />

      {/* Link to a swim — композиция add+remove (PATCH-а нет: пересоздаём с result_id). */}
      {linkSwimTarget && (
        <AddLinkModal
          swimmers={[{ id: linkSwimTarget.swimmer_id, name: linkSwimTarget.swimmer_name, hint: '' }]}
          initialUrl={linkSwimTarget.url}
          initialSwimmerId={linkSwimTarget.swimmer_id}
          initialStep={3}
          onClose={() => setLinkSwimTarget(null)}
          onSave={async (input): Promise<true | string> => {
            const { item, error } = await add({ ...input, swimmer_id: linkSwimTarget.swimmer_id });
            if (!item) return error;
            await remove(linkSwimTarget.id);
            await reload();
            return true;
          }}
        />
      )}

      {/* Кнопка-пилюля — общая с results (решение Влада 07.09.2026). Прибита к низу экрана:
          фильтруют, уже прокрутив список, и кнопка в потоке к этому моменту уезжает. */}
      <FiltersFab
        className={`my-media-fab ${deep}`}
        open={mobileFiltersOpen}
        onToggle={() => setMobileFiltersOpen((v) => !v)}
        count={activeFilterCount}
        controls="my-media-filters-sheet"
        // У шторки свой подвал «Show N swims» — пилюля на открытой шторке легла бы на него.
        hideWhenOpen
      />

      {/* Шторка фильтров (до lg) — ОБЩИЙ компонент, тот же, что на results. Внутри та же
          панель, что в сайдбаре: расходиться им нельзя, иначе телефон и десктоп начнут
          фильтровать по-разному. */}
      <MobileFiltersDrawer
        id="my-media-filters-sheet"
        className={`my-media-filters ${deep}`}
        variant="sheet"
        open={mobileFiltersOpen}
        onClose={() => setMobileFiltersOpen(false)}
        footer={(
          <button
            type="button"
            onClick={() => setMobileFiltersOpen(false)}
            className="hp-mono min-h-[44px] w-full rounded-[10px] border-none bg-[var(--t-accent)] text-[13px] font-extrabold text-[var(--t-accent-ink)]"
          >
            Show {filtered.length} {filtered.length === 1 ? 'swim' : 'swims'}
          </button>
        )}
      >
        {filterPanel}
      </MobileFiltersDrawer>

      <UI_SwimmerGallery gallery={lightboxItems} openIndex={lightboxIndex} onClose={() => setLightboxIndex(null)} />

      {/* Переключатель тем. Прячется, когда сверху что-то открыто: у него z-index выше всех
          оверлеев страницы (шторка 100, модалки 100, лайтбокс 50), и иначе кружок висел бы
          поверх них — на мобильной шторке ровно на кнопке «Show N swims». Перечисляем
          состояния явно, а не правим z-index общего компонента: он стоит ещё на пяти
          страницах, и у продукта одной шкалы z-index пока нет. */}
      {!anyOverlayOpen && <UI_ModeToggle />}
    </div>
  );
}

export default MyMedia;
