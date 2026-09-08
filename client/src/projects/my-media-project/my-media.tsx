import React, { useCallback, useMemo, useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import './my-media.css';
import { useAuth } from '../../hooks/useAuth';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useFavorites } from '../../hooks/useFavorites';
import { useMyMediaPublications } from '../../hooks/useUserMedia';
import { useMyHubGroups } from '../hub-groups-project/use-my-hub-groups';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import { useAllMyMedia, AllUserMediaDto, AddMediaInput } from './use-all-my-media';
import { useMySwims, MySwimDto, SwimMediaDto, seasonLabel, toggleLike, toggleCheer } from './use-my-swims';
import { useMyMediaModeration } from './use-my-media-moderation';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_SwimmerGallery from '../components/mix/swimmer-gallery/swimmer-gallery';
import { routes } from '../../utils/routes';
import HelperSwimmer from '../../utils/helpers/helper-swimmer';
import { seasonStartYear } from '../../utils/helpers/season-helper';
import { GalleryItem } from '../../utils/interfaces/results';
import MediaCard from './components/media-card';
import AddLinkModal, { AddLinkSwimmerOption } from './components/add-link-modal';
import ModerationPanel from './components/moderation-panel';
import SwimList from './components/swim-list';
import MyMediaFilterPanel, {
  DEFAULT_OPEN_CARDS,
  type Seg, type StatusFilter, type GroupFilter, type MediaCardKey,
} from './components/my-media-filter-panel';
import FilterBar, { type FilterBarChip } from '../components/filter-section/filter-bar';
import FiltersFab from '../components/filter-section/filters-fab';
import UI_SwimmStyleIcon from '../components/mix/swimm-style-icon/swimm-style-icon';
import { useMyMediaFilterHost, type MyMediaHostState } from './my-media-filter-host';
import MobileFiltersDrawer from '../components/filter-section/mobile-filters-drawer';
import { chipClass, derivedCardStatus, visibilityLabel, hpCardCls } from './components/status-styles';

// Страница «My media» v3 (swim-centric) — README design_handoff_my_swims_v3,1.
// Палитра — тема deep (light + dark), как у страниц пловца, клуба и /season-best: вёрстка
// просит роли `--t-*`, карта ролей на `--deep-*` живёт в my-media.css (Ф5).

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

  // ── Фильтры ────────────────────────────────────────────────────────────────
  // null → сервер выберет витринный сезон; 'all' → все сезоны сразу.
  const [season, setSeason] = useState<number | 'all' | null>(null);
  const { data, loading, reload } = useMySwims(season);
  const [swimmerFilter, setSwimmerFilter] = useState<number | 'all'>('all');
  const [seg, setSeg] = useState<Seg>('all');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [groupFilter, setGroupFilter] = useState<GroupFilter>('all');
  const [competitionFilter, setCompetitionFilter] = useState<number | 'all'>('all');
  const [styleFilter, setStyleFilter] = useState<string | 'all'>('all');
  const [distanceFilter, setDistanceFilter] = useState<string | 'all'>('all');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);
  const [unlinkedOpen, setUnlinkedOpen] = useState(false);

  // ── Модалы / оверлеи ──────────────────────────────────────────────────────
  const [addOpen, setAddOpen] = useState(false);
  const [addVideoSwim, setAddVideoSwim] = useState<MySwimDto | null>(null);
  const [addCompTarget, setAddCompTarget] = useState<{ id: number; name: string } | null>(null);
  const [linkSwimTarget, setLinkSwimTarget] = useState<AllUserMediaDto | null>(null);
  const [shareTarget, setShareTarget] = useState<AllUserMediaDto | null>(null);
  const [shareTargets, setShareTargets] = useState<{ id: number; name: string }[] | null>(null);
  const [shareGroupId, setShareGroupId] = useState<number | ''>('');
  const [shareLevel, setShareLevel] = useState<'members' | 'public'>('members');
  const [shareBusy, setShareBusy] = useState(false);
  const [shareError, setShareError] = useState<string | null>(null);

  const [lightboxItems, setLightboxItems] = useState<GalleryItem[]>([]);
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);

  // ── Реакции: оптимистичные оверрайды поверх ответа /api/me/swims ─────────
  const [likeOverrides, setLikeOverrides] = useState<Map<number, { count: number; mine: boolean }>>(new Map());
  const [cheerOverrides, setCheerOverrides] = useState<Map<number, { count: number; mine: boolean }>>(new Map());

  const applyMediaOverride = (m: SwimMediaDto): SwimMediaDto => {
    const o = likeOverrides.get(m.id);
    return o ? { ...m, likes_count: o.count, my_like: o.mine } : m;
  };

  const swims: MySwimDto[] = useMemo(
    () => data.swims.map((s) => {
      const o = cheerOverrides.get(s.result_id);
      return {
        ...s,
        congrats_count: o ? o.count : s.congrats_count,
        my_cheer: o ? o.mine : s.my_cheer,
        media: s.media.map(applyMediaOverride),
      };
    }),
    [data.swims, cheerOverrides, likeOverrides] // eslint-disable-line react-hooks/exhaustive-deps
  );
  const competitionMedia = useMemo(
    () => data.competition_media.map(applyMediaOverride),
    [data.competition_media, likeOverrides] // eslint-disable-line react-hooks/exhaustive-deps
  );
  const unlinkedMedia = useMemo(
    () => data.unlinked_media.map(applyMediaOverride),
    [data.unlinked_media, likeOverrides] // eslint-disable-line react-hooks/exhaustive-deps
  );

  const publicationsByMedia = useMemo(() => {
    const map = new Map<number, typeof publications>();
    for (const p of publications) {
      const list = map.get(p.user_media_id) ?? [];
      list.push(p);
      map.set(p.user_media_id, list);
    }
    return map;
  }, [publications]);

  const swimmerNames = useMemo(() => new Map(data.swimmers.map((s) => [s.id, s.name])), [data.swimmers]);

  // ── Справочники фильтров (из заплывов сезона) ─────────────────────────────
  const competitionOptions = useMemo(() => {
    const byId = new Map<number, string>();
    for (const s of swims) if (!byId.has(s.competition_id)) byId.set(s.competition_id, s.competition_name);
    return Array.from(byId.entries()).map(([id, name]) => ({ id, name }));
  }, [swims]);
  const styleOptions = useMemo(() => Array.from(new Set(swims.map((s) => s.style))).sort(), [swims]);
  const distanceOptions = useMemo(
    () => Array.from(new Set(swims.map((s) => s.distance))).sort((a, b) => Number(a) - Number(b) || a.localeCompare(b)),
    [swims]
  );

  // ── Группы: «куда я поднял медиа» ─────────────────────────────────────────
  // Публикации приходят по ВСЕМУ моему медиа (все сезоны), а страница сезонная,
  // поэтому список чипов берём из публикаций, а счётчики — по медиа этой страницы.
  const pageMediaIds = useMemo(() => {
    const ids: number[] = [];
    for (const sw of swims) for (const m of sw.media) ids.push(m.id);
    for (const m of competitionMedia) ids.push(m.id);
    for (const m of unlinkedMedia) ids.push(m.id);
    return ids;
  }, [swims, competitionMedia, unlinkedMedia]);

  const groupOptions = useMemo(() => {
    const names = new Map<number, string>();
    for (const p of publications) if (!names.has(p.hub_group_id)) names.set(p.hub_group_id, p.hub_group_name);
    const counts = new Map<number, number>();
    let notShared = 0;
    for (const id of pageMediaIds) {
      const pubs = publicationsByMedia.get(id) ?? [];
      if (pubs.length === 0) { notShared += 1; continue; }
      const seen = new Set<number>();
      for (const p of pubs) {
        if (seen.has(p.hub_group_id)) continue;
        seen.add(p.hub_group_id);
        counts.set(p.hub_group_id, (counts.get(p.hub_group_id) ?? 0) + 1);
      }
    }
    const groups = Array.from(names, ([id, name]) => ({ id, name, count: counts.get(id) ?? 0 }))
      .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
    return { groups, notShared, total: pageMediaIds.length };
  }, [publications, publicationsByMedia, pageMediaIds]);

  // Медиа «в группе» при любом статусе заявки (pending/approved/rejected) — статус сужает отдельный фильтр.
  const mediaMatchesGroup = (mediaId: number) => {
    if (groupFilter === 'all') return true;
    const pubs = publicationsByMedia.get(mediaId) ?? [];
    if (groupFilter === 'none') return pubs.length === 0;
    return pubs.some((p) => p.hub_group_id === groupFilter);
  };

  const pickGroup = (v: GroupFilter) => {
    setGroupFilter(v);
    // Медиа без заплыва тоже фильтруется — иначе выбранная группа «пропадает» в свёрнутой секции.
    if (v !== 'all') setUnlinkedOpen(true);
  };

  // ── Фильтрация ────────────────────────────────────────────────────────────
  // Принадлежность заплыва пловцу — ТОЛЬКО через канон-хелпер (docs/relays.md: «новый
  // фильтр/счётчик по пловцу — зови это, не сравнивай swimmer_id сам»): у эстафеты
  // swimmer_id это одна нога, остальные ноги теряются.
  const swimBelongsTo = (s: MySwimDto, id: number) => HelperSwimmer.resultBelongsToSwimmer(s, id);
  const bySwimmer = swimmerFilter === 'all' ? swims : swims.filter((s) => swimBelongsTo(s, swimmerFilter));

  const segCount = (k: Seg) =>
    bySwimmer.filter((s) => {
      const v = s.media.some((m) => m.media_type === 'video');
      return k === 'all' || (k === 'with' ? v : !v);
    }).length;

  const filtered = bySwimmer.filter((s) => {
    const hasVideo = s.media.some((m) => m.media_type === 'video');
    if (seg === 'with' && !hasVideo) return false;
    if (seg === 'without' && hasVideo) return false;
    if (competitionFilter !== 'all' && s.competition_id !== competitionFilter) return false;
    if (styleFilter !== 'all' && s.style !== styleFilter) return false;
    if (distanceFilter !== 'all' && s.distance !== distanceFilter) return false;
    if (groupFilter !== 'all' && !s.media.some((m) => mediaMatchesGroup(m.id))) return false;
    if (dateFrom && s.date < dateFrom) return false;
    if (dateTo && s.date > dateTo) return false;
    if (seg === 'with' && statusFilter !== 'all') {
      const match = s.media.some(
        (m) => m.media_type === 'video' && derivedCardStatus(publicationsByMedia.get(m.id) ?? []) === statusFilter
      );
      if (!match) return false;
    }
    return true;
  });

  const visibleCompetitionMedia =
    groupFilter === 'all' ? competitionMedia : competitionMedia.filter((m) => mediaMatchesGroup(m.id));
  const visibleUnlinkedMedia =
    groupFilter === 'all' ? unlinkedMedia : unlinkedMedia.filter((m) => mediaMatchesGroup(m.id));

  // Сколько фильтров ПАНЕЛИ сужают выборку: цифра на кнопке «Filters» и признак для кнопки
  // сброса. Пловец и сезон не в счёт — они живут наверху страницы и всегда на виду.
  const activeFilterCount =
    [competitionFilter, styleFilter, distanceFilter].filter((v) => v !== 'all').length +
    (dateFrom || dateTo ? 1 : 0) +
    (seg !== 'all' ? 1 : 0) +
    (groupFilter !== 'all' ? 1 : 0) +
    (seg === 'with' && statusFilter !== 'all' ? 1 : 0);

  // Пловца и сезон сброс НЕ трогает (хендофф): это не сужение выборки, а ответ на вопрос
  // «чьи заплывы и за какой сезон я смотрю» — сбросить их значит показать чужое.
  const clearAll = () => {
    setSeg('all');
    setStatusFilter('all');
    setGroupFilter('all');
    setCompetitionFilter('all');
    setStyleFilter('all');
    setDistanceFilter('all');
    setDateFrom('');
    setDateTo('');
  };

  // ── Реакции ───────────────────────────────────────────────────────────────
  const onToggleLike = async (m: SwimMediaDto) => {
    const next = !m.my_like;
    setLikeOverrides((prev) => new Map(prev).set(m.id, { count: m.likes_count + (next ? 1 : -1), mine: next }));
    const state = await toggleLike(m.id, next);
    if (state) setLikeOverrides((prev) => new Map(prev).set(m.id, state));
  };

  const onToggleCheer = async (s: MySwimDto) => {
    const next = !s.my_cheer;
    setCheerOverrides((prev) => new Map(prev).set(s.result_id, { count: s.congrats_count + (next ? 1 : -1), mine: next }));
    const state = await toggleCheer(s.result_id, next);
    if (state) setCheerOverrides((prev) => new Map(prev).set(s.result_id, state));
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
  const handleAdd = async (input: AddMediaInput): Promise<boolean> => {
    const item = await add(input);
    if (item) { await reload(); return true; }
    return false;
  };

  const handleDelete = async (mediaId: number) => {
    const ok = await remove(mediaId);
    if (ok) await reload();
  };

  // ── Share (модал — mobile и Unlinked; inline share живёт в SwimList) ─────
  const openShare = async (item: AllUserMediaDto) => {
    setShareTarget(item);
    setShareTargets(null);
    setShareError(null);
    const active = (publicationsByMedia.get(item.id) ?? []).find(
      (p) => p.status === 'pending' || p.status === 'approved'
    );
    setShareGroupId(active ? active.hub_group_id : '');
    setShareLevel(active ? active.level : 'members');
    try {
      const r = await fetch(`/api/me/media/${item.id}/publish-targets`, { credentials: 'include' });
      setShareTargets(r.ok ? await r.json() : []);
    } catch {
      setShareTargets([]);
    }
  };

  // Одно место на модал и на инлайн-строку заплыва: сервер запрещает переподачу, пока
  // публикация в этой группе pending/approved («publication already exists»), поэтому
  // смена уровня (members ↔ everyone) идёт через withdraw + резаявку.
  const publishTo = async (
    mediaId: number, hubGroupId: number, level: 'members' | 'public'
  ): Promise<{ ok: boolean; error?: string }> => {
    const active = (publicationsByMedia.get(mediaId) ?? []).find(
      (p) => p.hub_group_id === hubGroupId && (p.status === 'pending' || p.status === 'approved')
    );
    if (active) {
      if (active.level === level) return { ok: true }; // менять нечего
      await withdrawPublication(mediaId, hubGroupId);
    }
    return submitPublication(mediaId, hubGroupId, level);
  };

  const handlePublish = async () => {
    if (shareTarget == null || shareGroupId === '') return;
    setShareBusy(true);
    setShareError(null);
    const res = await publishTo(shareTarget.id, shareGroupId, shareLevel);
    setShareBusy(false);
    if (res.ok) setShareTarget(null);
    else setShareError(res.error ?? 'Could not submit the request');
  };

  const submitInlineShare = async (mediaId: number, hubGroupId: number, level: 'members' | 'public'): Promise<boolean> => {
    const res = await publishTo(mediaId, hubGroupId, level);
    return res.ok;
  };

  // ── Add link: пловцы для пикера ───────────────────────────────────────────
  const addLinkSwimmers: AddLinkSwimmerOption[] = useMemo(() => {
    const byId = new Map<number, AddLinkSwimmerOption>();
    for (const s of data.swimmers) {
      byId.set(s.id, { id: s.id, name: s.name, hint: s.is_primary ? 'primary swimmer' : 'favorite' });
    }
    for (const f of favorites.favorites) {
      if (f.target_type !== 'swimmer' || f.swimmer_id == null || byId.has(f.swimmer_id)) continue;
      byId.set(f.swimmer_id, { id: f.swimmer_id, name: f.swimmer_name || `#${f.swimmer_id}`, hint: 'favorite' });
    }
    for (const m of allMedia) {
      if (!byId.has(m.swimmer_id)) byId.set(m.swimmer_id, { id: m.swimmer_id, name: m.swimmer_name, hint: 'has media' });
    }
    return Array.from(byId.values());
  }, [data.swimmers, favorites.favorites, allMedia]);

  const totalCount = bySwimmer.length;
  const pendingModCount = moderation.rows.filter((r) => r.status === 'pending').length;
  const effectiveSeason = data.season || new Date().getFullYear();
  // Выбранный сезон обязан быть в списке: иначе <select> показывает чужую подпись — так
  // страница и «залипала» на пустом 2026/27, когда данные есть только за 2025/26.
  const seasonChoices = useMemo(() => {
    const years = new Set<number>(data.seasons);
    years.add(effectiveSeason);
    // Новый (календарный) сезон в списке есть всегда, даже пока стартов в нём нет — по
    // общему правилу витрины: «новый сезон в селекторе есть, но по умолчанию не выбран»
    // (docs/season-boundary-rule.md). Выбирает же дефолт сервер — витринным сезоном.
    years.add(seasonStartYear());
    return Array.from(years).sort((a, b) => b - a);
  }, [data.seasons, effectiveSeason]);
  // Карусель говорит числом-годом или null (∞ «все сезоны»). Состояние страницы шире:
  // null в нём означает «сезон выбирает сервер» (витринный), поэтому наружу отдаём уже
  // посчитанный `effectiveSeason`, а не сырой null — иначе карусель встала бы на ∞.
  const carouselSeason = season === 'all' || data.all_seasons ? null : season ?? effectiveSeason;
  // Выбран чип пловца — имя должно быть видно и вне чипа: в строках оно скрыто (фильтр же
  // один на всех), и экран переставал отвечать на вопрос «чьи это заплывы».
  const selectedSwimmerName = swimmerFilter === 'all' ? null : swimmerNames.get(swimmerFilter) ?? null;
  const pickSeason = (v: number | null) => setSeason(v === null ? 'all' : v);

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
      value: (
        <span dir="auto">
          {competitionOptions.find((c) => c.id === competitionFilter)?.name}
        </span>
      ),
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
      active: seg === 'with' && statusFilter !== 'all',
      value: statusFilter,
      // Статус живёт только у видео: без него колонка врала бы, что фильтр доступен.
      hideWhenIdle: seg !== 'with',
      onClick: () => revealCard('status'),
    },
  ];

  const seasonOptions = useMemo(
    () => seasonChoices.map((y) => ({ season: y, label: seasonLabel(y) })),
    [seasonChoices],
  );

  const filterPanel = (
    <MyMediaFilterPanel
      host={filterHost}
      seasons={seasonOptions}
      season={carouselSeason}
      onSeason={pickSeason}
      swimmers={data.swimmers.map((sw) => ({
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
      onSeg={(k) => { setSeg(k); if (k !== 'with') setStatusFilter('all'); }}
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
    onWithdraw: (mediaId: number, hubGroupId: number) => withdrawPublication(mediaId, hubGroupId),
    onDelete: handleDelete,
    onToggleLike,
    onToggleCheer,
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
          <p className="mm-head__eyebrow">My profile · {auth.displayName || auth.email}</p>
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
            {showModeration && pendingModCount > 0 && (
              <div className="flex items-center gap-3 rounded-[14px] border border-[var(--t-warn-border)] bg-[var(--t-warn-soft)] p-[12px_16px]">
                <span className="flex h-[22px] min-w-[22px] items-center justify-center rounded-[11px] bg-[var(--t-warn)] px-1.5 text-[12px] font-black text-[var(--t-warn-ink)]">
                  {pendingModCount}
                </span>
                <span className="min-w-0 text-[13.5px] font-bold text-[var(--t-warn)]">requests are waiting for your approval</span>
                <button type="button" onClick={() => setTab('moderation')} className="hp-mono ml-auto rounded-[9px] border-none bg-[var(--t-warn)] px-3.5 py-[7px] text-[12px] font-extrabold text-[var(--t-warn-ink)]">
                  Review →
                </button>
              </div>
            )}


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
                            onWithdraw={(hubGroupId) => withdrawPublication(item.id, hubGroupId)}
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
            onDecide={async (hubGroupId, publicationId, approve) => { await moderation.decide(hubGroupId, publicationId, approve); }}
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
              <span dir="auto" className="ml-2 text-[var(--t-text-2)]">{addVideoSwim.competition_name} · {addVideoSwim.competition_date}</span>
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

      {/* Share with a group — модал (mobile actions sheet + Unlinked) */}
      {shareTarget && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-[var(--t-scrim)] backdrop-blur-[4px]" onClick={() => setShareTarget(null)}>
          <div
            className="w-[420px] max-w-[calc(100vw-40px)] rounded-[16px] border border-[var(--t-border)] bg-[var(--t-surface-strong)] p-5 text-[var(--t-text)]"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 className="m-0 mb-3 text-[15px] font-black">Share with a group</h3>
            {shareTargets != null && shareTargets.length === 0 && (
              <p className="text-[12px] text-[var(--t-text-2)]">
                No eligible groups — the swimmer must be in the group's roster and you must be a member.
              </p>
            )}
            {shareTargets != null && shareTargets.length > 0 && (
              <div className="flex flex-col gap-2.5">
                <select
                  value={shareGroupId}
                  onChange={(e) => setShareGroupId(e.target.value === '' ? '' : Number(e.target.value))}
                  className="rounded-[8px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2.5 py-2 text-[12px] text-[var(--t-text)]"
                >
                  <option value="">— group —</option>
                  {shareTargets.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
                </select>
                <select
                  value={shareLevel}
                  onChange={(e) => setShareLevel(e.target.value as 'members' | 'public')}
                  className="rounded-[8px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2.5 py-2 text-[12px] text-[var(--t-text)]"
                >
                  <option value="members">Group members</option>
                  <option value="public">Public (visible to everyone)</option>
                </select>
                {shareLevel === 'public' && (
                  <p className="m-0 text-[11px] text-[var(--t-warn)]">Public = visible to everyone on the internet after approval.</p>
                )}
                {shareError && <div className="text-[11.5px] text-[var(--t-danger)]">{shareError}</div>}
                <div className="flex justify-end gap-2">
                  <button type="button" onClick={() => setShareTarget(null)} className="hp-mono rounded-[8px] border border-[var(--t-border)] bg-transparent px-3 py-[7px] text-[11.5px] font-extrabold text-[var(--t-accent-dim)]">
                    Cancel
                  </button>
                  <button
                    type="button"
                    disabled={shareBusy || shareGroupId === ''}
                    onClick={handlePublish}
                    className="hp-mono rounded-[8px] border-none bg-[var(--t-accent)] px-3 py-[7px] text-[11.5px] font-extrabold text-[var(--t-accent-ink)] disabled:opacity-50"
                  >
                    Submit for approval
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Link to a swim — композиция add+remove (PATCH-а нет: пересоздаём с result_id). */}
      {linkSwimTarget && (
        <AddLinkModal
          swimmers={[{ id: linkSwimTarget.swimmer_id, name: linkSwimTarget.swimmer_name, hint: '' }]}
          initialUrl={linkSwimTarget.url}
          initialSwimmerId={linkSwimTarget.swimmer_id}
          initialStep={3}
          onClose={() => setLinkSwimTarget(null)}
          onSave={async (input): Promise<boolean> => {
            const item = await add({ ...input, swimmer_id: linkSwimTarget.swimmer_id });
            if (item) { await remove(linkSwimTarget.id); await reload(); }
            return !!item;
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
    </div>
  );
}

export default MyMedia;
