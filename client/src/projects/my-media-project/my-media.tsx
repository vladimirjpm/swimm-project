import React, { useMemo, useState } from 'react';
import '../home-project/home.css';
import { useAuth } from '../../hooks/useAuth';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useFavorites } from '../../hooks/useFavorites';
import { useMyMediaPublications } from '../../hooks/useUserMedia';
import { useMyHubGroups } from '../hub-groups-project/use-my-hub-groups';
import { useAllMyMedia, AllUserMediaDto, seasonFromCompetitionDate, AddMediaInput } from './use-all-my-media';
import { useMyMediaModeration } from './use-my-media-moderation';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_SwimmerGallery from '../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem } from '../../utils/interfaces/results';
import MediaCard from './components/media-card';
import AddLinkModal, { AddLinkSwimmerOption } from './components/add-link-modal';
import ModerationPanel from './components/moderation-panel';
import { chipClass, segmentClass, derivedCardStatus, CardStatus } from './components/status-styles';

// Страница «My media» v2 — README дизайн-хендоффа design_handoff_media_page.
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

type StatusFilter = CardStatus | 'all';

function MyMediaContent() {
  const auth = useAuth();
  const { media, loading, remove, add } = useAllMyMedia();
  const { publications, submit: submitPublication, withdraw: withdrawPublication } = useMyMediaPublications();
  const favorites = useFavorites();
  const { groups: myGroups } = useMyHubGroups(auth.isAuthenticated);

  const showModeration = auth.isAdmin || myGroups.length > 0;
  const moderation = useMyMediaModeration(showModeration);

  const [tab, setTab] = useState<'media' | 'moderation'>('media');
  const [swimmerFilter, setSwimmerFilter] = useState<number | 'all'>('all');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [seasonFilter, setSeasonFilter] = useState<string | 'all'>('all');
  const [competitionFilter, setCompetitionFilter] = useState<string | 'all'>('all');
  const [clubFilter, setClubFilter] = useState<string | 'all'>('all');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [moreFiltersOpen, setMoreFiltersOpen] = useState(false);
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);

  const [addOpen, setAddOpen] = useState(false);
  const [linkSwimTarget, setLinkSwimTarget] = useState<AllUserMediaDto | null>(null);
  const [shareTarget, setShareTarget] = useState<AllUserMediaDto | null>(null);
  const [shareTargets, setShareTargets] = useState<{ id: number; name: string }[] | null>(null);
  const [shareGroupId, setShareGroupId] = useState<number | ''>('');
  const [shareLevel, setShareLevel] = useState<'members' | 'public'>('members');
  const [shareBusy, setShareBusy] = useState(false);
  const [shareError, setShareError] = useState<string | null>(null);

  const [lightboxItems, setLightboxItems] = useState<GalleryItem[]>([]);
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);

  const publicationsByMedia = useMemo(() => {
    const map = new Map<number, typeof publications>();
    for (const p of publications) {
      const list = map.get(p.user_media_id) ?? [];
      list.push(p);
      map.set(p.user_media_id, list);
    }
    return map;
  }, [publications]);

  // ── Справочники фильтров (производные от загруженных медиа) ──────────────
  const swimmerOptions = useMemo(() => {
    const byId = new Map<number, string>();
    for (const m of media) byId.set(m.swimmer_id, m.swimmer_name);
    return Array.from(byId.entries()).map(([id, name]) => ({ id, name }));
  }, [media]);

  const seasonOptions = useMemo(
    () => Array.from(new Set(media.map((m) => seasonFromCompetitionDate(m.competition_date)).filter((s): s is string => !!s))).sort().reverse(),
    [media]
  );
  const competitionOptions = useMemo(
    () => Array.from(new Set(media.map((m) => m.competition_name).filter((c): c is string => !!c))),
    [media]
  );
  const clubOptions = useMemo(
    () => Array.from(new Set(media.map((m) => m.club_name).filter((c): c is string => !!c))),
    [media]
  );

  const withStatus = useMemo(
    () => media.map((m) => ({ item: m, status: derivedCardStatus((publicationsByMedia.get(m.id) ?? []).map((p) => ({ status: p.status }))) })),
    [media, publicationsByMedia]
  );

  const bySwimmer = swimmerFilter === 'all' ? withStatus : withStatus.filter((x) => x.item.swimmer_id === swimmerFilter);

  const filtered = bySwimmer.filter((x) => {
    if (statusFilter !== 'all' && x.status !== statusFilter) return false;
    if (seasonFilter !== 'all' && seasonFromCompetitionDate(x.item.competition_date) !== seasonFilter) return false;
    if (competitionFilter !== 'all' && x.item.competition_name !== competitionFilter) return false;
    if (clubFilter !== 'all' && x.item.club_name !== clubFilter) return false;
    if (dateFrom || dateTo) {
      const d = x.item.competition_date;
      if (!d) return false;
      const [dd, mm, yyyy] = d.split('/');
      const iso = yyyy && mm && dd ? `${yyyy}-${mm.padStart(2, '0')}-${dd.padStart(2, '0')}` : null;
      if (!iso) return false;
      if (dateFrom && iso < dateFrom) return false;
      if (dateTo && iso > dateTo) return false;
    }
    return true;
  });

  const activeMoreFilterCount = [seasonFilter, competitionFilter, clubFilter].filter((v) => v !== 'all').length + (dateFrom || dateTo ? 1 : 0);

  const clearAll = () => {
    setSwimmerFilter('all');
    setStatusFilter('all');
    setSeasonFilter('all');
    setCompetitionFilter('all');
    setClubFilter('all');
    setDateFrom('');
    setDateTo('');
  };

  const openLightboxFor = (item: AllUserMediaDto) => {
    const embeddable = bySwimmer
      .map((x) => x.item)
      .filter((m) => m.media_type === 'video' && (m.source_type === 'youtube' || m.source_type === 'vimeo'));
    const idx = embeddable.findIndex((m) => m.id === item.id);
    if (idx < 0) return;
    setLightboxItems(embeddable.map((m) => ({ type: 'video', sourceType: m.source_type as GalleryItem['sourceType'], url: m.url })));
    setLightboxIndex(idx);
  };

  // ── Add link (шаг 2: пловцы = primary + favorites + те, у кого уже есть медиа) ──
  const addLinkSwimmers: AddLinkSwimmerOption[] = useMemo(() => {
    const byId = new Map<number, AddLinkSwimmerOption>();
    for (const f of favorites.favorites) {
      if (f.target_type !== 'swimmer' || f.swimmer_id == null) continue;
      byId.set(f.swimmer_id, {
        id: f.swimmer_id,
        name: f.swimmer_name || `#${f.swimmer_id}`,
        hint: f.is_primary ? 'primary swimmer' : 'favorite',
      });
    }
    for (const s of swimmerOptions) {
      if (!byId.has(s.id)) byId.set(s.id, { id: s.id, name: s.name, hint: 'has media' });
    }
    return Array.from(byId.values());
  }, [favorites.favorites, swimmerOptions]);

  const handleAddSave = async (input: AddMediaInput): Promise<boolean> => {
    const item = await add(input);
    if (item) {
      clearAll();
      return true;
    }
    return false;
  };

  // ── Share with a group ────────────────────────────────────────────────────
  const openShare = async (item: AllUserMediaDto) => {
    setShareTarget(item);
    setShareTargets(null);
    setShareError(null);
    setShareGroupId('');
    try {
      const r = await fetch(`/api/me/media/${item.id}/publish-targets`, { credentials: 'include' });
      setShareTargets(r.ok ? await r.json() : []);
    } catch {
      setShareTargets([]);
    }
  };

  const handlePublish = async () => {
    if (shareTarget == null || shareGroupId === '') return;
    setShareBusy(true);
    setShareError(null);
    const res = await submitPublication(shareTarget.id, shareGroupId, shareLevel);
    setShareBusy(false);
    if (res.ok) {
      setShareTarget(null);
    } else {
      setShareError(res.error ?? 'Could not submit the request');
    }
  };

  const totalCount = media.length;
  const pendingModCount = moderation.rows.filter((r) => r.status === 'pending').length;

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
            <a href="./groups.html" className="hp-mono whitespace-nowrap rounded-[8px] border border-[rgba(125,211,252,0.3)] px-3 py-[5px] text-[12px] font-extrabold text-[rgba(125,211,252,0.6)] no-underline">
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
          All your links across swimmers — share them with groups and track publication status.
        </p>
      </section>

      <section className="relative px-5 pt-[26px] lg:px-16">
        <div className="flex items-center gap-2.5">
          <button type="button" onClick={() => setTab('media')} className={
            `hp-mono inline-flex items-center gap-2 whitespace-nowrap rounded-[12px] border px-[18px] py-[10px] text-[13.5px] font-extrabold ${
              tab === 'media' ? 'border-[#7dd3fc] bg-[rgba(125,211,252,0.14)] text-[#7dd3fc]' : 'border-[rgba(125,211,252,0.3)] bg-transparent text-[rgba(125,211,252,0.55)]'
            }`
          }>
            My media <span className="font-bold opacity-65">· {totalCount}</span>
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

            {/* Swimmer chips + Add link — desktop row, mobile: scroll row + Filters/Add buttons */}
            <div className="flex flex-wrap items-center gap-2">
              <button type="button" onClick={() => setSwimmerFilter('all')} className={chipClass(swimmerFilter === 'all')}>
                All · {media.length}
              </button>
              {swimmerOptions.map((s) => (
                <button key={s.id} type="button" onClick={() => setSwimmerFilter(s.id)} className={chipClass(swimmerFilter === s.id)}>
                  <span
                    className="inline-flex h-[17px] w-[17px] items-center justify-center rounded-full text-[8px] font-black"
                    style={{ background: swimmerFilter === s.id ? 'rgba(4,16,31,0.2)' : '#2c3d52', color: swimmerFilter === s.id ? '#04101f' : '#bfe0f5' }}
                  >
                    {s.name.trim().charAt(0).toUpperCase()}
                  </span>
                  {s.name} · {media.filter((m) => m.swimmer_id === s.id).length}
                </button>
              ))}
              <button
                type="button"
                onClick={() => setAddOpen(true)}
                className="hp-mono ml-auto whitespace-nowrap rounded-[10px] border-none bg-[#38ef8f] px-[18px] py-[9px] text-[13px] font-extrabold text-[#04101f]"
              >
                + Add link
              </button>
            </div>

            {/* Status segments + More filters — desktop; mobile collapses into a Filters button */}
            <div className="hidden flex-wrap items-center gap-3 sm:flex">
              <div className="inline-flex overflow-hidden rounded-[10px] border border-[rgba(125,211,252,0.35)]">
                {(['all', 'private', 'pending', 'published', 'rejected'] as StatusFilter[]).map((k, i, arr) => (
                  <button key={k} type="button" onClick={() => setStatusFilter(k)} className={segmentClass(statusFilter === k, i === arr.length - 1)}>
                    {k === 'all' ? `All · ${bySwimmer.length}` : k[0].toUpperCase() + k.slice(1)}
                  </button>
                ))}
              </div>
              <div className="relative">
                <button
                  type="button"
                  onClick={() => setMoreFiltersOpen((v) => !v)}
                  className="hp-mono rounded-[10px] border border-[rgba(125,211,252,0.3)] bg-transparent px-3 py-[7px] text-[11.5px] font-extrabold text-[rgba(125,211,252,0.7)]"
                >
                  More filters {activeMoreFilterCount > 0 ? `(${activeMoreFilterCount}) ` : ''}▾
                </button>
                {moreFiltersOpen && (
                  <div className="absolute left-0 top-[38px] z-30 flex w-[280px] flex-col gap-2.5 rounded-[12px] border border-[rgba(125,211,252,0.3)] bg-[#0e2138] p-3 shadow-[0_18px_44px_rgba(0,0,0,0.45)]">
                    <FilterSelect label="Season" value={seasonFilter} onChange={setSeasonFilter} options={seasonOptions} />
                    <FilterSelect label="Competition" value={competitionFilter} onChange={setCompetitionFilter} options={competitionOptions} rtl />
                    <FilterSelect label="Club" value={clubFilter} onChange={setClubFilter} options={clubOptions} />
                    <div>
                      <label className="hp-mono mb-1 block text-[10px] font-extrabold uppercase tracking-[0.1em] text-[rgba(125,211,252,0.7)]">Date range</label>
                      <div className="flex items-center gap-1.5">
                        <input type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} className="w-full rounded-[8px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-2 py-1 text-[11px] text-[#f3f8fd]" />
                        <span className="text-[rgba(203,224,240,0.5)]">–</span>
                        <input type="date" value={dateTo} onChange={(e) => setDateTo(e.target.value)} className="w-full rounded-[8px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-2 py-1 text-[11px] text-[#f3f8fd]" />
                      </div>
                    </div>
                    <button type="button" onClick={clearAll} className="hp-mono self-start text-[11px] font-extrabold text-[#7dd3fc]">Clear all</button>
                  </div>
                )}
              </div>
              <span className="ml-auto text-[11.5px] font-bold text-[rgba(203,224,240,0.5)]">sorted by swim date ↓</span>
            </div>

            {/* Mobile: Filters + Add link buttons */}
            <div className="flex items-center gap-2 sm:hidden">
              <button
                type="button"
                onClick={() => setMobileFiltersOpen(true)}
                className="hp-mono flex min-h-[44px] flex-1 items-center justify-center gap-2 rounded-[10px] border border-[rgba(125,211,252,0.35)] bg-transparent text-[13px] font-extrabold text-[#7dd3fc]"
              >
                ⚙ Filters {activeMoreFilterCount + (statusFilter !== 'all' ? 1 : 0) > 0 ? `(${activeMoreFilterCount + (statusFilter !== 'all' ? 1 : 0)})` : ''}
              </button>
            </div>

            {loading ? (
              <div className="py-10 text-center text-[13px] italic text-[rgba(203,224,240,0.5)]">Loading…</div>
            ) : media.length === 0 ? (
              <div className="rounded-[18px] border border-[rgba(125,211,252,0.22)] bg-[linear-gradient(180deg,rgba(56,189,248,0.08),rgba(8,25,48,0.78))] p-[56px_40px] text-center">
                <div className="text-[40px]">🎬</div>
                <p className="m-0 mt-3 text-[17px] font-black text-[#f3f8fd]">No media yet</p>
                <p className="mx-auto mt-2 max-w-[380px] text-[13px] leading-[1.5] text-[rgba(203,224,240,0.6)]">
                  Add a YouTube or Vimeo link — or add videos straight from a swim row on the results page.
                </p>
                <button type="button" onClick={() => setAddOpen(true)} className="hp-mono mt-[18px] rounded-[10px] border-none bg-[#38ef8f] px-5 py-[10px] text-[13px] font-extrabold text-[#04101f]">
                  + Add link
                </button>
              </div>
            ) : filtered.length === 0 ? (
              <div className="rounded-[16px] border border-dashed border-[rgba(125,211,252,0.25)] p-10 text-center">
                <p className="m-0 text-[14px] font-bold text-[rgba(203,224,240,0.6)]">Nothing matches the filters</p>
                <button type="button" onClick={clearAll} className="hp-mono mt-3 rounded-[9px] border border-[rgba(125,211,252,0.4)] bg-transparent px-3.5 py-[7px] text-[12px] font-extrabold text-[#7dd3fc]">
                  Clear all
                </button>
              </div>
            ) : (
              <div className="grid gap-3.5" style={{ gridTemplateColumns: 'repeat(auto-fill,minmax(250px,1fr))' }}>
                {filtered.map(({ item }) => (
                  <MediaCard
                    key={item.id}
                    item={item}
                    publications={publicationsByMedia.get(item.id) ?? []}
                    onOpenLightbox={() => openLightboxFor(item)}
                    onDelete={() => remove(item.id)}
                    onWithdraw={(hubGroupId) => withdrawPublication(item.id, hubGroupId)}
                    onLinkToSwim={() => setLinkSwimTarget(item)}
                    onShareWithGroup={() => openShare(item)}
                  />
                ))}
              </div>
            )}
          </div>
        ) : (
          <ModerationPanel
            rows={moderation.rows}
            onDecide={async (hubGroupId, publicationId, approve) => { await moderation.decide(hubGroupId, publicationId, approve); }}
          />
        )}
      </section>

      {addOpen && (
        <AddLinkModal
          swimmers={addLinkSwimmers}
          onClose={() => setAddOpen(false)}
          onSave={handleAddSave}
        />
      )}

      {/* Share with a group — simple inline panel (mobile-friendly), triggered by card footer */}
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
                    Submit
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Link to a swim — нет PATCH-эндпоинта на сервере (API «менять нельзя»), поэтому
          линкуем композицией из готовых ручек: пересоздаём запись с тем же url/тип +
          result_id, затем удаляем старую (id меняется, но для юзера выглядит как "привязали").
          Переиспользуем пикер Add link, сразу на шаге 3 — URL и пловец уже известны. */}
      {linkSwimTarget && (
        <AddLinkModal
          swimmers={[{ id: linkSwimTarget.swimmer_id, name: linkSwimTarget.swimmer_name, hint: '' }]}
          initialUrl={linkSwimTarget.url}
          initialSwimmerId={linkSwimTarget.swimmer_id}
          initialStep={3}
          onClose={() => setLinkSwimTarget(null)}
          onSave={async (input): Promise<boolean> => {
            const item = await add({ ...input, swimmer_id: linkSwimTarget.swimmer_id });
            if (item) await remove(linkSwimTarget.id);
            return !!item;
          }}
        />
      )}

      {/* Mobile filters bottom sheet */}
      {mobileFiltersOpen && (
        <div className="fixed inset-0 z-[100] flex items-end bg-[rgba(2,10,24,0.72)] backdrop-blur-[4px]" onClick={() => setMobileFiltersOpen(false)}>
          <div
            className="max-h-[85vh] w-full overflow-y-auto rounded-t-[20px] border-t border-[rgba(125,211,252,0.3)] bg-[linear-gradient(180deg,#0e2138,#081527)] p-5 text-[#f3f8fd]"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mx-auto mb-4 h-1 w-10 rounded-full bg-[rgba(125,211,252,0.35)]" />
            <div className="flex flex-col gap-3">
              <div>
                <p className="hp-mono mb-1.5 text-[11px] font-extrabold uppercase tracking-[0.12em] text-[#7dd3fc]">Swimmer</p>
                <div className="flex flex-wrap gap-2">
                  <button type="button" onClick={() => setSwimmerFilter('all')} className={chipClass(swimmerFilter === 'all')}>All</button>
                  {swimmerOptions.map((s) => (
                    <button key={s.id} type="button" onClick={() => setSwimmerFilter(s.id)} className={chipClass(swimmerFilter === s.id)}>{s.name}</button>
                  ))}
                </div>
              </div>
              <div>
                <p className="hp-mono mb-1.5 text-[11px] font-extrabold uppercase tracking-[0.12em] text-[#7dd3fc]">Status</p>
                <div className="flex flex-wrap gap-2">
                  {(['all', 'private', 'pending', 'published', 'rejected'] as StatusFilter[]).map((k) => (
                    <button key={k} type="button" onClick={() => setStatusFilter(k)} className={chipClass(statusFilter === k)}>
                      {k === 'all' ? 'All' : k[0].toUpperCase() + k.slice(1)}
                    </button>
                  ))}
                </div>
              </div>
              <FilterSelect label="Season" value={seasonFilter} onChange={setSeasonFilter} options={seasonOptions} />
              <FilterSelect label="Competition" value={competitionFilter} onChange={setCompetitionFilter} options={competitionOptions} rtl />
              <FilterSelect label="Club" value={clubFilter} onChange={setClubFilter} options={clubOptions} />
              <button type="button" onClick={clearAll} className="hp-mono self-start text-[12px] font-extrabold text-[#7dd3fc]">Clear all</button>
              <button
                type="button"
                onClick={() => setMobileFiltersOpen(false)}
                className="hp-mono mt-2 min-h-[44px] w-full rounded-[10px] border-none bg-[#7dd3fc] text-[13px] font-extrabold text-[#04101f]"
              >
                Show {filtered.length} items
              </button>
            </div>
          </div>
        </div>
      )}

      <UI_SwimmerGallery gallery={lightboxItems} openIndex={lightboxIndex} onClose={() => setLightboxIndex(null)} />
    </div>
  );
}

function FilterSelect({
  label, value, onChange, options, rtl,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: string[];
  rtl?: boolean;
}) {
  return (
    <div>
      <label className="hp-mono mb-1 block text-[10px] font-extrabold uppercase tracking-[0.1em] text-[rgba(125,211,252,0.7)]">{label}</label>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        dir={rtl ? 'rtl' : undefined}
        className="w-full rounded-[8px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-2 py-[7px] text-[12px] text-[#f3f8fd]"
      >
        <option value="all">All</option>
        {options.map((o) => <option key={o} value={o}>{o}</option>)}
      </select>
    </div>
  );
}

export default MyMedia;
