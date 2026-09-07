import React, { useCallback, useMemo, useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import './my-media.css';
import { useAuth } from '../../hooks/useAuth';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useFavorites } from '../../hooks/useFavorites';
import { useMyMediaPublications } from '../../hooks/useUserMedia';
import { useMyHubGroups } from '../hub-groups-project/use-my-hub-groups';
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
import MyMediaFilterPanel, { type Seg, type StatusFilter, type GroupFilter } from './components/my-media-filter-panel';
import { useMyMediaFilterHost, type MyMediaHostState } from './my-media-filter-host';
import MobileFiltersDrawer from '../components/filter-section/mobile-filters-drawer';
import DeepSeasonCarousel from '../components/deep/season-carousel';
import { chipClass, derivedCardStatus, visibilityLabel, hpCardCls } from './components/status-styles';

// Страница «My media» v3 (swim-centric) — README design_handoff_my_swims_v3,1.
// Тёмный стиль groups.html/home.html — осознанное решение, не через var(--theme-mode-*).

function MyMedia() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();

  if (auth.loading) {
    return <div className="min-h-screen bg-[#050e1c]" />;
  }

  if (!auth.isAuthenticated) {
    return (
      <div className="home-page relative flex min-h-screen items-center justify-center overflow-x-clip px-4 text-[#f3f8fd]">
        <div className="hp-shimmer" aria-hidden="true" />
        <AppTopbar />
        <div className="absolute inset-0 flex items-center justify-center">
          <div className="w-full max-w-md rounded-[18px] border border-[rgba(125,211,252,0.22)] bg-[linear-gradient(180deg,rgba(56,189,248,0.08),rgba(8,25,48,0.78))] p-8 text-center shadow-[0_24px_60px_rgba(2,10,24,0.5)]">
            <h1 className="mb-2 text-lg font-black text-[#f3f8fd]">My media</h1>
            <p className="mb-4 text-sm text-[rgba(203,224,240,0.7)]">Sign in to manage your media</p>
            <button
              type="button"
              onClick={openLoginModal}
              className="hp-mono rounded-[11px] bg-[#38ef8f] px-4 py-2 text-sm font-extrabold text-[#04101f]"
            >
              Sign in
            </button>
          </div>
        </div>
      </div>
    );
  }

  return <MyMediaContent />;
}

function MyMediaContent() {
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
  const [actionsFor, setActionsFor] = useState<MySwimDto | null>(null);
  const [actionsMediaId, setActionsMediaId] = useState<number | null>(null);

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

  const clearAll = () => {
    setSwimmerFilter('all');
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
  const filterPanel = (
    <MyMediaFilterPanel
      host={filterHost}
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
    onOpenActions: (s: MySwimDto) => { setActionsFor(s); setActionsMediaId(s.media[0]?.id ?? null); },
  };

  const actionsSwim = actionsFor ? swims.find((s) => s.result_id === actionsFor.result_id) ?? null : null;
  const actionsMedia = actionsSwim?.media.find((m) => m.id === actionsMediaId) ?? actionsSwim?.media[0] ?? null;

  return (
    <div className="home-page relative min-h-screen overflow-x-clip pb-24 text-[#f3f8fd]" style={{ background: 'linear-gradient(160deg,#0d2036 0%,#0b1b31 45%,#050e1c 100%)' }}>
      <div className="hp-shimmer" aria-hidden="true" />
      <AppTopbar />

      <section className="relative px-5 pt-10 lg:px-16">
        <p className="mb-3.5 text-[11px] font-extrabold uppercase tracking-[0.28em] text-[#7dd3fc] lg:text-[15px] lg:tracking-[0.3em]">
          My profile · {auth.displayName || auth.email}
        </p>
        <div className="flex flex-wrap items-end gap-6">
          <h1 className="m-0 text-[32px] font-black leading-[0.92] tracking-[-0.045em] text-[#f3f8fd] lg:text-[56px]">My media</h1>
          <nav className="flex items-center gap-2 pb-1.5" aria-label="Profile sections">
            <span className="hp-mono whitespace-nowrap rounded-[8px] border border-[#7dd3fc] bg-[rgba(125,211,252,0.14)] px-3 py-[5px] text-[12px] font-extrabold text-[#7dd3fc]">
              Media
            </span>
            <a href={routes.groupsList()} className="hp-mono whitespace-nowrap rounded-[8px] border border-[rgba(125,211,252,0.3)] px-3 py-[5px] text-[12px] font-extrabold text-[rgba(125,211,252,0.6)] no-underline">
              My groups ↗
            </a>
            <span
              title="Coming later"
              className="hp-mono whitespace-nowrap rounded-[8px] border border-dashed border-[rgba(203,224,240,0.25)] px-3 py-[5px] text-[12px] font-extrabold text-[rgba(203,224,240,0.35)]"
            >
              Settings · soon
            </span>
          </nav>
        </div>
        <p className="mt-4 max-w-[560px] text-[14.5px] leading-[1.55] text-[rgba(226,240,252,0.82)]">
          Your swims by season — attach videos and photos, share them with groups.
        </p>
      </section>

      <section className="relative px-5 pt-[26px] lg:px-16">
        <div className="flex items-center gap-2.5">
          <button type="button" onClick={() => setTab('media')} className={
            `hp-mono inline-flex items-center gap-2 whitespace-nowrap rounded-[12px] border px-[18px] py-[10px] text-[13.5px] font-extrabold ${
              tab === 'media' ? 'border-[#7dd3fc] bg-[rgba(125,211,252,0.14)] text-[#7dd3fc]' : 'border-[rgba(125,211,252,0.3)] bg-transparent text-[rgba(125,211,252,0.55)]'
            }`
          }>
            My swims <span className="font-bold opacity-65">· {totalCount}</span>
          </button>
          {showModeration && (
            <button type="button" onClick={() => setTab('moderation')} className={
              `hp-mono inline-flex items-center gap-2 whitespace-nowrap rounded-[12px] border px-[18px] py-[10px] text-[13.5px] font-extrabold ${
                tab === 'moderation' ? 'border-[#7dd3fc] bg-[rgba(125,211,252,0.14)] text-[#7dd3fc]' : 'border-[rgba(125,211,252,0.3)] bg-transparent text-[rgba(125,211,252,0.55)]'
              }`
            }>
              Moderation
              {pendingModCount > 0 && (
                <span className="inline-flex h-[18px] min-w-[18px] items-center justify-center rounded-[9px] bg-[#ffca7a] px-[5px] text-[10.5px] font-black text-[#3a2a08]">
                  {pendingModCount}
                </span>
              )}
            </button>
          )}
        </div>

        {tab === 'media' ? (
          <div className="mt-[18px] flex flex-col gap-4">
            {showModeration && pendingModCount > 0 && (
              <div className="flex items-center gap-3 rounded-[14px] border border-[rgba(255,202,122,0.4)] bg-[linear-gradient(180deg,rgba(255,202,122,0.1),rgba(8,25,48,0.6))] p-[12px_16px]">
                <span className="flex h-[22px] min-w-[22px] items-center justify-center rounded-[11px] bg-[#ffca7a] px-1.5 text-[12px] font-black text-[#3a2a08]">
                  {pendingModCount}
                </span>
                <span className="min-w-0 text-[13.5px] font-bold text-[#ffe3b8]">requests are waiting for your approval</span>
                <button type="button" onClick={() => setTab('moderation')} className="hp-mono ml-auto rounded-[9px] border-none bg-[#ffca7a] px-3.5 py-[7px] text-[12px] font-extrabold text-[#3a2a08]">
                  Review →
                </button>
              </div>
            )}

            {/* Swimmer chips + Add link */}
            <div className="flex items-center gap-2 overflow-x-auto sm:flex-wrap sm:overflow-visible" style={{ scrollbarWidth: 'none' }}>
              <button type="button" onClick={() => setSwimmerFilter('all')} className={chipClass(swimmerFilter === 'all')}>
                All · {swims.length}
              </button>
              {data.swimmers.map((s) => (
                <button key={s.id} type="button" onClick={() => setSwimmerFilter(s.id)} className={chipClass(swimmerFilter === s.id)}>
                  <span
                    className="inline-flex h-[17px] w-[17px] items-center justify-center rounded-full text-[8px] font-black"
                    style={{ background: swimmerFilter === s.id ? 'rgba(4,16,31,0.2)' : '#2c3d52', color: swimmerFilter === s.id ? '#04101f' : '#bfe0f5' }}
                  >
                    {s.name.trim().charAt(0).toUpperCase()}
                  </span>
                  {/* dir только на имени: без изоляции иврит уводит счётчик влево. */}
                  <span><span dir="auto">{s.name}</span> · {swims.filter((x) => swimBelongsTo(x, s.id)).length}</span>
                </button>
              ))}
              <button
                type="button"
                onClick={() => setAddOpen(true)}
                className="hp-mono ml-auto hidden whitespace-nowrap rounded-[10px] border-none bg-[#38ef8f] px-[18px] py-[9px] text-[13px] font-extrabold text-[#04101f] sm:block"
              >
                + Add link
              </button>
            </div>

            {/* Сезон — каруселью, а не селектором: это не клиентский фильтр, а другой
                запрос к серверу, и он обязан оставаться на виду, а не уезжать в панель.
                ⚠ Карусель из семьи deep, её токены объявлены на классе `.theme-deep`, а не
                на `:root`. Класс пока прибит гвоздём (страница тёмная жёстко); Ф5 переводит
                на тему всю страницу, и он переедет на корень через `useDeepThemeClass()`. */}
            <div className="theme-deep">
              <DeepSeasonCarousel
                seasons={seasonChoices.map((y) => ({ season: y, label: seasonLabel(y) }))}
                season={carouselSeason}
                onSeason={pickSeason}
              />
            </div>

            <div className="flex flex-col gap-4 lg:flex-row lg:items-start">
              {/* Сайдбар фильтров (десктоп). Ширина фиксированная, как на results: строка
                  заплыва тоже фиксированной ширины, и доля от экрана её ломала бы. */}
              <aside className="my-media-filters hidden w-[320px] shrink-0 lg:block">
                {filterPanel}
              </aside>

              <div className="flex min-w-0 flex-1 flex-col gap-4">
                {/* Кнопка панели (до lg) и сводка «сколько заплывов сейчас видно» */}
                <div className="flex items-center gap-3">
                  <button
                    type="button"
                    onClick={() => setMobileFiltersOpen(true)}
                    className="hp-mono flex min-h-[40px] shrink-0 items-center justify-center gap-2 rounded-[10px] border border-[rgba(125,211,252,0.35)] bg-transparent px-4 text-[13px] font-extrabold text-[#7dd3fc] lg:hidden"
                    aria-expanded={mobileFiltersOpen}
                    aria-controls="my-media-filters-sheet"
                  >
                    ⚙ Filters {activeFilterCount > 0 ? `(${activeFilterCount})` : ''}
                  </button>
                  <p className="m-0 text-[11.5px] font-bold text-[rgba(203,224,240,0.5)]">
                    {selectedSwimmerName && (
                      <span dir="auto" className="mr-1.5 text-[12.5px] font-black text-[#7dd3fc]">{selectedSwimmerName}</span>
                    )}
                    {filtered.length} {filtered.length === 1 ? 'swim' : 'swims'}
                    <span className="hidden sm:inline"> · sorted by date ↓</span>
                  </p>
                </div>

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
                    <p className="m-0 mt-3 text-[17px] font-black text-[#f3f8fd]">No favorite swimmers yet</p>
                    <p className="mx-auto mt-2 max-w-[380px] text-[13px] leading-[1.5] text-[rgba(203,224,240,0.6)]">
                      Add a swimmer to favorites — their swims will appear here and you can attach videos.
                    </p>
                    <a href={routes.results()} className="hp-mono mt-[18px] inline-block rounded-[10px] border-none bg-[#38ef8f] px-5 py-[10px] text-[13px] font-extrabold text-[#04101f] no-underline">
                      Find swimmers →
                    </a>
                  </div>
                ) : swims.length === 0 ? (
                  <div className="rounded-[16px] border border-dashed border-[rgba(125,211,252,0.25)] p-10 text-center">
                    <p className="m-0 text-[14px] font-bold text-[rgba(203,224,240,0.6)]">
                      {data.all_seasons ? 'No results yet' : `No results in season ${seasonLabel(effectiveSeason)}`}
                    </p>
                    {data.seasons.filter((y) => y !== effectiveSeason).slice(0, 1).map((y) => (
                      <button key={y} type="button" onClick={() => setSeason(y)} className="hp-mono mt-3 rounded-[9px] border border-[rgba(125,211,252,0.4)] bg-transparent px-3.5 py-[7px] text-[12px] font-extrabold text-[#7dd3fc]">
                        Season {seasonLabel(y)} →
                      </button>
                    ))}
                  </div>
                ) : filtered.length === 0 ? (
                  <div className="rounded-[16px] border border-dashed border-[rgba(125,211,252,0.25)] p-10 text-center">
                    <p className="m-0 text-[14px] font-bold text-[rgba(203,224,240,0.6)]">Nothing matches the filters</p>
                    <button type="button" onClick={clearAll} className="hp-mono mt-3 rounded-[9px] border border-[rgba(125,211,252,0.4)] bg-transparent px-3.5 py-[7px] text-[12px] font-extrabold text-[#7dd3fc]">
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
                      className="hp-mono flex w-full items-center gap-2 rounded-[12px] border border-[rgba(125,211,252,0.25)] bg-transparent px-4 py-[10px] text-left text-[12px] font-extrabold text-[#7dd3fc]"
                    >
                      Unlinked media
                      <span className="inline-flex h-[18px] min-w-[18px] items-center justify-center rounded-[9px] bg-[rgba(125,211,252,0.18)] px-1.5 text-[10.5px]">{visibleUnlinkedMedia.length}</span>
                      <span className="font-bold normal-case text-[rgba(203,224,240,0.45)]">· club videos and general footage not tied to any swim</span>
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
                        className="hp-mono mt-3 rounded-[9px] border border-dashed border-[rgba(56,239,143,0.4)] bg-transparent px-3.5 py-[7px] text-[12px] font-extrabold text-[rgba(56,239,143,0.8)]"
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
          className="hp-mono fixed bottom-5 left-1/2 z-40 -translate-x-1/2 rounded-full border-none bg-[#38ef8f] px-6 py-3 text-[13px] font-extrabold text-[#04101f] shadow-[0_12px_30px_rgba(0,0,0,0.45)] sm:hidden"
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
              <span className="hp-mono ml-2 text-[#7dd3fc]">{addVideoSwim.time}</span>
              <span dir="auto" className="ml-2 text-[rgba(203,224,240,0.55)]">{addVideoSwim.competition_name} · {addVideoSwim.competition_date}</span>
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
              <span dir="auto" className="ml-2 text-[rgba(203,224,240,0.55)]">{addCompTarget.name}</span>
            </span>
          }
          onClose={() => setAddCompTarget(null)}
          onSave={handleAdd}
        />
      )}

      {/* Share with a group — модал (mobile actions sheet + Unlinked) */}
      {shareTarget && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-[rgba(2,10,24,0.72)] backdrop-blur-[4px]" onClick={() => setShareTarget(null)}>
          <div
            className="w-[420px] max-w-[calc(100vw-40px)] rounded-[16px] border border-[rgba(125,211,252,0.3)] bg-[linear-gradient(180deg,#0e2138,#081527)] p-5 text-[#f3f8fd]"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 className="m-0 mb-3 text-[15px] font-black">Share with a group</h3>
            {shareTargets != null && shareTargets.length === 0 && (
              <p className="text-[12px] text-[rgba(203,224,240,0.6)]">
                No eligible groups — the swimmer must be in the group's roster and you must be a member.
              </p>
            )}
            {shareTargets != null && shareTargets.length > 0 && (
              <div className="flex flex-col gap-2.5">
                <select
                  value={shareGroupId}
                  onChange={(e) => setShareGroupId(e.target.value === '' ? '' : Number(e.target.value))}
                  className="rounded-[8px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-2.5 py-2 text-[12px] text-[#f3f8fd]"
                >
                  <option value="">— group —</option>
                  {shareTargets.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
                </select>
                <select
                  value={shareLevel}
                  onChange={(e) => setShareLevel(e.target.value as 'members' | 'public')}
                  className="rounded-[8px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-2.5 py-2 text-[12px] text-[#f3f8fd]"
                >
                  <option value="members">Group members</option>
                  <option value="public">Public (visible to everyone)</option>
                </select>
                {shareLevel === 'public' && (
                  <p className="m-0 text-[11px] text-[#ffca7a]">Public = visible to everyone on the internet after approval.</p>
                )}
                {shareError && <div className="text-[11.5px] text-[#ef5350]">{shareError}</div>}
                <div className="flex justify-end gap-2">
                  <button type="button" onClick={() => setShareTarget(null)} className="hp-mono rounded-[8px] border border-[rgba(125,211,252,0.3)] bg-transparent px-3 py-[7px] text-[11.5px] font-extrabold text-[rgba(125,211,252,0.7)]">
                    Cancel
                  </button>
                  <button
                    type="button"
                    disabled={shareBusy || shareGroupId === ''}
                    onClick={handlePublish}
                    className="hp-mono rounded-[8px] border-none bg-[#38ef8f] px-3 py-[7px] text-[11.5px] font-extrabold text-[#04101f] disabled:opacity-50"
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

      {/* Шторка фильтров (до lg) — ОБЩИЙ компонент, тот же, что на results. Внутри та же
          панель, что в сайдбаре: расходиться им нельзя, иначе телефон и десктоп начнут
          фильтровать по-разному. */}
      <MobileFiltersDrawer
        id="my-media-filters-sheet"
        className="my-media-filters"
        variant="sheet"
        open={mobileFiltersOpen}
        onClose={() => setMobileFiltersOpen(false)}
        footer={(
          <button
            type="button"
            onClick={() => setMobileFiltersOpen(false)}
            className="hp-mono min-h-[44px] w-full rounded-[10px] border-none bg-[#38ef8f] text-[13px] font-extrabold text-[#04101f]"
          >
            Show {filtered.length} {filtered.length === 1 ? 'swim' : 'swims'}
          </button>
        )}
      >
        {filterPanel}
      </MobileFiltersDrawer>

      {/* Mobile actions bottom sheet */}
      {actionsSwim && (
        <div className="fixed inset-0 z-[100] flex items-end bg-[rgba(2,10,24,0.72)] backdrop-blur-[4px] sm:hidden" onClick={() => setActionsFor(null)}>
          <div
            className="w-full rounded-t-[20px] border-t border-[rgba(125,211,252,0.3)] bg-[linear-gradient(180deg,#0e2138,#081527)] p-5 text-[#f3f8fd]"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mx-auto mb-4 h-1 w-10 rounded-full bg-[rgba(125,211,252,0.35)]" />
            <p className="m-0 text-[15px] font-black">
              {actionsSwim.distance}m {actionsSwim.style}
              <span className="hp-mono ml-2 text-[#7dd3fc]">{actionsSwim.time}</span>
            </p>
            <p dir="auto" className="m-0 mt-1 text-[12px] text-[rgba(203,224,240,0.55)]">{actionsSwim.competition_name} · {actionsSwim.competition_date}</p>

            {actionsSwim.media.length > 1 && (
              <div className="mt-3 flex gap-2 overflow-x-auto" style={{ scrollbarWidth: 'none' }}>
                {actionsSwim.media.map((m, i) => {
                  const mediaPubs = publicationsByMedia.get(m.id) ?? [];
                  const st = derivedCardStatus(mediaPubs);
                  const seenByAll = mediaPubs.some((p) => p.status === 'approved' && p.level === 'public');
                  return (
                    <button key={m.id} type="button" onClick={() => setActionsMediaId(m.id)} className={chipClass(actionsMedia?.id === m.id)}>
                      {m.media_type === 'image' ? '🖼' : '▶'} {i + 1} · {m.media_type === 'image' ? 'PHOTO' : m.source_type.toUpperCase()} · {visibilityLabel(st, seenByAll)} · ❤ {m.likes_count}
                    </button>
                  );
                })}
              </div>
            )}

            {actionsMedia && (
              <div className="mt-4 flex flex-col gap-2">
                <button type="button" onClick={() => { onPlay(actionsMedia); setActionsFor(null); }} className="hp-mono min-h-[44px] w-full rounded-[10px] border-none bg-[#7dd3fc] text-[13px] font-extrabold text-[#04101f]">
                  {actionsMedia.media_type === 'image' ? '🖼 View photo' : '▶ Play'}
                </button>
                <button type="button" onClick={() => onToggleLike(actionsMedia)} className="hp-mono min-h-[44px] w-full rounded-[10px] border border-[rgba(255,125,156,0.45)] bg-transparent text-[13px] font-extrabold" style={{ color: actionsMedia.my_like ? '#ff7d9c' : 'rgba(255,125,156,0.7)' }}>
                  ❤ {actionsMedia.likes_count}{actionsMedia.my_like ? ' · liked' : ''}
                </button>
                <button type="button" onClick={() => { openShare(actionsMedia); setActionsFor(null); }} className="hp-mono min-h-[44px] w-full rounded-[10px] border border-[rgba(125,211,252,0.35)] bg-transparent text-[13px] font-extrabold text-[#7dd3fc]">
                  Share with a group
                </button>
                {(publicationsByMedia.get(actionsMedia.id) ?? [])
                  .filter((p) => p.status === 'pending' || p.status === 'approved')
                  .map((p) => (
                    <button key={p.hub_group_id} type="button" onClick={() => withdrawPublication(actionsMedia.id, p.hub_group_id)} className="hp-mono min-h-[44px] w-full rounded-[10px] border border-[rgba(255,202,122,0.45)] bg-transparent text-[13px] font-extrabold text-[#ffca7a]">
                      Withdraw from {p.hub_group_name}
                    </button>
                  ))}
                <button type="button" onClick={() => { handleDelete(actionsMedia.id); setActionsFor(null); }} className="hp-mono min-h-[44px] w-full rounded-[10px] border border-[rgba(239,83,80,0.45)] bg-transparent text-[13px] font-extrabold text-[#ef5350]">
                  Delete {actionsMedia.media_type === 'image' ? 'photo' : 'video'}
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      <UI_SwimmerGallery gallery={lightboxItems} openIndex={lightboxIndex} onClose={() => setLightboxIndex(null)} />
    </div>
  );
}

export default MyMedia;
