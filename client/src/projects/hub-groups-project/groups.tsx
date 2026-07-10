import React, { useEffect, useMemo, useState } from 'react';
import '../home-project/home.css';
import HomeHeader from '../home-project/components/home-header';
import RecordTicker from '../home-project/components/record-ticker';
import MyGroupsPanel from './my-groups-panel';
import type { HubGroupDetails, HubGroupLink, HubGroupListItem, HubGroupMember, HubGroupStanding } from './types';

// Страница групп (HubGroups, фазы 3–4): список публичных групп + страница группы
// с участниками, «рекордами группы» и последними заплывами. Виртуальная группа
// «Моё избранное» (/api/hub-groups/favorites) показывается первой залогиненному.

const ROLE_LABEL: Record<HubGroupMember['role'], string | null> = {
  member: null,
  captain: 'капитан',
  coach: 'тренер',
};

const LINK_LABEL: Record<string, string> = {
  whatsapp: 'WhatsApp',
  telegram: 'Telegram',
  instagram: 'Instagram',
  site: 'Site',
};

function groupInitial(name: string): string {
  return (name.trim()[0] ?? '?').toUpperCase();
}

function GroupIcon({ iconUrl, name, size }: { iconUrl?: string | null; name: string; size: 'sm' | 'lg' }) {
  const cls =
    size === 'lg'
      ? 'h-16 w-16 rounded-[18px] text-[26px] lg:h-20 lg:w-20 lg:text-[32px]'
      : 'h-11 w-11 rounded-[13px] text-[18px]';
  if (iconUrl) {
    return <img src={iconUrl} alt="" className={`${cls} shrink-0 object-cover`} />;
  }
  return (
    <span
      className={`${cls} flex shrink-0 items-center justify-center bg-[linear-gradient(140deg,#38bdf8,#0369a1)] font-black text-[#06263f]`}
    >
      {groupInitial(name)}
    </span>
  );
}

function LinkChips({ links }: { links: HubGroupLink[] }) {
  if (links.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-2">
      {links.map((l) => (
        <a
          key={l.kind + l.url}
          href={l.url}
          target="_blank"
          rel="noopener noreferrer"
          className="hp-mono rounded-[8px] border border-[#7dd3fc]/40 px-3 py-[5px] text-[12px] font-extrabold text-[#7dd3fc] no-underline transition-colors hover:border-[#7dd3fc] hover:bg-[rgba(56,189,248,0.12)]"
        >
          {LINK_LABEL[l.kind] ?? l.kind} ↗
        </a>
      ))}
    </div>
  );
}

// ── Список групп ─────────────────────────────────────────────────────────────

function GroupCard({ group, href }: { group: HubGroupListItem; href: string }) {
  return (
    <a
      href={href}
      className="hp-card-std flex min-h-[130px] flex-col justify-between gap-4 rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] text-inherit no-underline shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] transition-[transform,border-color,box-shadow] duration-[180ms] ease-out hover:-translate-y-2 hover:border-[#7dd3fc]/80 lg:rounded-[24px] lg:p-[26px]"
    >
      <div className="flex items-start gap-4">
        <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="sm" />
        <div className="min-w-0">
          <div className="truncate text-[19px] font-black tracking-[-0.02em] lg:text-[22px]">
            {group.name}
          </div>
          {group.name_en && group.name_en !== group.name && (
            <div className="truncate text-[12px] font-bold text-[#cbe0f0]/60">{group.name_en}</div>
          )}
        </div>
      </div>
      {group.description && (
        <p className="line-clamp-2 text-[13px] leading-snug text-[#cbe0f0]/75">{group.description}</p>
      )}
      <div className="flex items-center justify-between">
        <span className="hp-mono rounded-[7px] border border-[#7dd3fc]/40 px-2 py-[3px] text-[11px] font-extrabold text-[#7dd3fc]">
          {group.member_count} · swimmers
        </span>
        <span className="truncate pl-3 text-[12px] font-bold text-[#cbe0f0]/60">
          {group.location ?? group.club_name ?? ''}
        </span>
      </div>
    </a>
  );
}

function GroupsList({ groups, favorites }: { groups: HubGroupListItem[]; favorites: HubGroupDetails | null }) {
  return (
    <>
      <section className="relative px-5 pt-[26px] lg:px-16 lg:pt-[46px]">
        <p className="mb-[18px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[#7dd3fc] lg:text-[15px] lg:tracking-[0.3em]">
          Train together · Follow together
        </p>
        <h1 className="text-[44px] font-black leading-[0.92] tracking-[-0.045em] text-[#f3f8fd] lg:text-[88px] lg:leading-[0.9]">
          Groups
        </h1>
        <p className="mt-5 max-w-[560px] text-[14.5px] leading-[1.55] text-[#e2f0fc]/[0.82] lg:text-[18px] lg:leading-[1.6]">
          Тренировочные группы: состав, рекорды группы и свежие заплывы участников.
        </p>
      </section>

      <section
        className="grid grid-cols-1 gap-3 px-4 pt-[26px] sm:grid-cols-2 lg:grid-cols-3 lg:gap-[18px] lg:px-16 lg:pt-12"
        aria-label="Groups"
      >
        {favorites && (
          <GroupCard
            href="./groups.html?group=favorites"
            group={{
              slug: 'favorites',
              name: favorites.name,
              name_en: favorites.name_en,
              description: 'Пловцы из твоего избранного — как личная группа',
              icon_url: null,
              location: null,
              club_name: null,
              member_count: favorites.members.length,
            }}
          />
        )}
        {groups.map((g) => (
          <GroupCard key={g.slug} group={g} href={`./groups.html?group=${encodeURIComponent(g.slug)}`} />
        ))}
        {groups.length === 0 && !favorites && (
          <p className="col-span-full py-10 text-center text-[14px] text-[#cbe0f0]/60">
            Групп пока нет.
          </p>
        )}
      </section>
    </>
  );
}

// ── Страница группы ──────────────────────────────────────────────────────────

const BESTS_PREVIEW_COUNT = 10;

function swimmerDisplayName(last: string, first: string, lastEn: string, firstEn: string): string {
  const ru = `${last} ${first}`.trim();
  return ru.length > 0 ? ru : `${lastEn} ${firstEn}`.trim();
}

function GroupDetails({ group }: { group: HubGroupDetails }) {
  const [showAllBests, setShowAllBests] = useState(false);
  const bests = showAllBests ? group.bests : group.bests.slice(0, BESTS_PREVIEW_COUNT);

  const cellCls = 'px-3 py-2 text-left text-[13px] text-[#e2f0fc]/[0.85]';
  const headCls =
    'px-3 py-2 text-left text-[10.5px] font-extrabold uppercase tracking-[0.18em] text-[#7dd3fc]';
  const cardCls =
    'hp-card-std rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]';

  return (
    <>
      <section className="relative px-5 pt-[26px] lg:px-16 lg:pt-[46px]">
        <a href="./groups.html" className="text-[13px] font-extrabold text-[#7dd3fc] no-underline hover:underline">
          ← All groups
        </a>

        <div className="mt-5 flex flex-wrap items-center gap-5">
          <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="lg" />
          <div className="min-w-0">
            <h1 className="text-[34px] font-black leading-[0.95] tracking-[-0.04em] text-[#f3f8fd] lg:text-[56px]">
              {group.name}
            </h1>
            {group.name_en && group.name_en !== group.name && (
              <p className="mt-1 text-[14px] font-bold text-[#cbe0f0]/60">{group.name_en}</p>
            )}
          </div>
        </div>

        {group.description && (
          <p className="mt-4 max-w-[640px] text-[14.5px] leading-[1.55] text-[#e2f0fc]/[0.82] lg:text-[16px]">
            {group.description}
          </p>
        )}

        <div className="mt-4 flex flex-wrap items-center gap-x-5 gap-y-2 text-[13px] font-bold text-[#cbe0f0]/70">
          {group.location && <span>📍 {group.location}</span>}
          {group.club_name && <span>Клуб: {group.club_name}</span>}
          <span>
            {group.members.length} · swimmers
          </span>
        </div>

        <div className="mt-4">
          <LinkChips links={group.links} />
        </div>
      </section>

      <section className="grid grid-cols-1 gap-4 px-4 pt-[26px] lg:grid-cols-[320px_1fr] lg:gap-[18px] lg:px-16 lg:pt-10">
        {/* Участники */}
        <div className={cardCls} aria-label="Members">
          <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">Members</h2>
          {group.members.length === 0 ? (
            <p className="text-[13px] text-[#cbe0f0]/60">
              {group.is_virtual ? 'В избранном пока нет пловцов — жми сердечки на результатах.' : 'Состав пока не заполнен.'}
            </p>
          ) : (
            <ul className="m-0 flex list-none flex-col gap-[10px] p-0">
              {group.members.map((m) => (
                <li key={m.swimmer_id} className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <div className="truncate text-[14px] font-extrabold text-[#f3f8fd]">
                      {m.name || m.name_en}
                    </div>
                    <div className="truncate text-[11.5px] text-[#cbe0f0]/55">
                      {[m.birth_year > 0 ? m.birth_year : null, m.club_name].filter(Boolean).join(' · ')}
                    </div>
                  </div>
                  {ROLE_LABEL[m.role] && (
                    <span className="hp-mono shrink-0 rounded-[7px] border border-[#38ef8f]/40 px-2 py-[3px] text-[10.5px] font-extrabold text-[#38ef8f]">
                      {ROLE_LABEL[m.role]}
                    </span>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="flex flex-col gap-4 lg:gap-[18px]">
          {/* Сезонный зачёт */}
          <div className={cardCls} aria-label="Season standings">
            <div className="mb-4 flex flex-wrap items-baseline justify-between gap-2">
              <h2 className="text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">
                Season standings
              </h2>
              {group.season_label && (
                <span className="hp-mono text-[11.5px] font-bold text-[#cbe0f0]/55">
                  {group.season_label}
                </span>
              )}
            </div>
            {group.standings.length === 0 || group.standings.every((s) => s.swims === 0) ? (
              <p className="text-[13px] text-[#cbe0f0]/60">В этом сезоне заплывов нет.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-[#7dd3fc]/20">
                      <th className={`${headCls} text-right`}>#</th>
                      <th className={headCls}>Swimmer</th>
                      <th className={`${headCls} text-right`}>Swims</th>
                      <th className={headCls}>Medals</th>
                      <th className={`${headCls} text-right`}>Points</th>
                      <th className={`${headCls} text-right`}>Best FINA</th>
                    </tr>
                  </thead>
                  <tbody>
                    {group.standings.map((s: HubGroupStanding, i) => (
                      <tr key={s.swimmer_id} className="border-b border-[#7dd3fc]/10">
                        <td className={`${cellCls} hp-mono text-right font-extrabold text-[#cbe0f0]/70`}>
                          {i + 1}
                        </td>
                        <td className={`${cellCls} font-extrabold text-[#f3f8fd]`}>
                          <span>{s.name || s.name_en}</span>
                          {ROLE_LABEL[s.role] && (
                            <span className="hp-mono ml-2 rounded-[6px] border border-[#38ef8f]/40 px-[6px] py-[2px] text-[10px] font-extrabold text-[#38ef8f]">
                              {ROLE_LABEL[s.role]}
                            </span>
                          )}
                        </td>
                        <td className={`${cellCls} hp-mono text-right`}>{s.swims || '—'}</td>
                        <td className={`${cellCls} whitespace-nowrap`}>
                          {s.golds + s.silvers + s.bronzes === 0 ? (
                            <span className="text-[#cbe0f0]/40">—</span>
                          ) : (
                            <span className="hp-mono">
                              {s.golds > 0 && <span className="mr-2">🥇{s.golds}</span>}
                              {s.silvers > 0 && <span className="mr-2">🥈{s.silvers}</span>}
                              {s.bronzes > 0 && <span>🥉{s.bronzes}</span>}
                            </span>
                          )}
                        </td>
                        <td className={`${cellCls} hp-mono text-right font-extrabold text-[#7dd3fc]`}>
                          {s.club_points || '—'}
                        </td>
                        <td className={`${cellCls} hp-mono text-right`}>{s.best_fina || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* Рекорды группы */}
          <div className={cardCls} aria-label="Group records">
            <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">
              Group records
            </h2>
            {group.bests.length === 0 ? (
              <p className="text-[13px] text-[#cbe0f0]/60">У участников пока нет зачтённых результатов.</p>
            ) : (
              <>
                <div className="overflow-x-auto">
                  <table className="w-full border-collapse">
                    <thead>
                      <tr className="border-b border-[#7dd3fc]/20">
                        <th className={headCls}>Event</th>
                        <th className={headCls}>Pool</th>
                        <th className={headCls}>Time</th>
                        <th className={headCls}>Holder</th>
                        <th className={headCls}>Meet</th>
                        <th className={`${headCls} text-right`}>Pts</th>
                      </tr>
                    </thead>
                    <tbody>
                      {bests.map((b) => (
                        <tr
                          key={`${b.style_name}-${b.distance}-${b.pool_type}-${b.gender}`}
                          className="border-b border-[#7dd3fc]/10"
                        >
                          <td className={`${cellCls} whitespace-nowrap font-extrabold text-[#f3f8fd]`}>
                            {b.distance} {b.style_name}
                            <span className="pl-2 text-[11px] font-bold text-[#cbe0f0]/50">
                              {b.gender === 'female' ? 'W' : b.gender === 'male' ? 'M' : b.gender}
                            </span>
                          </td>
                          <td className={cellCls}>{b.pool_type ?? '—'}</td>
                          <td className={`${cellCls} hp-mono whitespace-nowrap font-extrabold text-[#7dd3fc]`}>
                            {b.time_original}
                          </td>
                          <td className={cellCls}>{b.swimmer_name || b.swimmer_name_en}</td>
                          <td className={`${cellCls} max-w-[220px]`}>
                            <span className="block truncate">{b.competition_name}</span>
                            <span className="text-[11px] text-[#cbe0f0]/50">{b.date}</span>
                          </td>
                          <td className={`${cellCls} hp-mono text-right`}>{b.points || '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                {group.bests.length > BESTS_PREVIEW_COUNT && (
                  <button
                    type="button"
                    onClick={() => setShowAllBests((v) => !v)}
                    className="mt-3 cursor-pointer rounded-[8px] border border-[#7dd3fc]/40 bg-transparent px-3 py-[6px] text-[12px] font-extrabold text-[#7dd3fc] transition-colors hover:bg-[rgba(56,189,248,0.12)]"
                  >
                    {showAllBests ? 'Show less' : `Show all ${group.bests.length}`}
                  </button>
                )}
              </>
            )}
          </div>

          {/* Последние заплывы */}
          <div className={cardCls} aria-label="Recent swims">
            <h2 className="mb-4 text-[15px] font-black uppercase tracking-[0.2em] text-[#7dd3fc]">
              Recent swims
            </h2>
            {group.recent_results.length === 0 ? (
              <p className="text-[13px] text-[#cbe0f0]/60">Заплывов пока нет.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-[#7dd3fc]/20">
                      <th className={headCls}>Date</th>
                      <th className={headCls}>Swimmer</th>
                      <th className={headCls}>Event</th>
                      <th className={headCls}>Time</th>
                      <th className={headCls}>Pos</th>
                      <th className={`${headCls} max-w-[220px]`}>Meet</th>
                    </tr>
                  </thead>
                  <tbody>
                    {group.recent_results.map((r) => (
                      <tr key={r.id} className="border-b border-[#7dd3fc]/10">
                        <td className={`${cellCls} hp-mono whitespace-nowrap`}>{r.date}</td>
                        <td className={`${cellCls} font-extrabold text-[#f3f8fd]`}>
                          {swimmerDisplayName(r.last_name, r.first_name, r.last_name_en, r.first_name_en)}
                        </td>
                        <td className={`${cellCls} whitespace-nowrap`}>
                          {r.event_style_len} {r.event_style_name}
                          {r.is_relay && <span className="pl-1 text-[11px] text-[#cbe0f0]/50">relay</span>}
                        </td>
                        <td className={`${cellCls} hp-mono whitespace-nowrap font-extrabold ${r.time_fail ? 'text-[#ef5350]' : 'text-[#7dd3fc]'}`}>
                          {r.time_fail ? 'DSQ' : r.time}
                        </td>
                        <td className={`${cellCls} hp-mono`}>{r.position ?? '—'}</td>
                        <td className={`${cellCls} max-w-[220px]`}>
                          <span className="block truncate">{r.competition}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      </section>
    </>
  );
}

// ── Корневой компонент страницы ──────────────────────────────────────────────

function Groups() {
  const slug = useMemo(() => new URLSearchParams(window.location.search).get('group'), []);

  const [groups, setGroups] = useState<HubGroupListItem[]>([]);
  const [favorites, setFavorites] = useState<HubGroupDetails | null>(null);
  const [details, setDetails] = useState<HubGroupDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        if (slug) {
          // favorites — авторизованный эндпоинт с тем же контрактом
          const url = slug === 'favorites' ? '/api/hub-groups/favorites' : `/api/hub-groups/${encodeURIComponent(slug)}`;
          const r = await fetch(url, { credentials: 'include' });
          if (!r.ok) throw new Error(r.status === 404 ? 'Группа не найдена' : `Ошибка загрузки (${r.status})`);
          const data: HubGroupDetails = await r.json();
          if (!cancelled) setDetails(data);
        } else {
          const [listR, favR] = await Promise.all([
            fetch('/api/hub-groups'),
            // 401 для незалогиненного — норма, карточка избранного просто не показывается
            fetch('/api/hub-groups/favorites', { credentials: 'include' }).catch(() => null),
          ]);
          if (!listR.ok) throw new Error(`Ошибка загрузки (${listR.status})`);
          const list: HubGroupListItem[] = await listR.json();
          const fav: HubGroupDetails | null = favR && favR.ok ? await favR.json() : null;
          if (!cancelled) {
            setGroups(list);
            setFavorites(fav);
          }
        }
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Ошибка загрузки');
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [slug]);

  return (
    <div className="home-page relative min-h-screen overflow-x-clip pb-[96px] text-[#f3f8fd]">
      <div className="hp-shimmer" aria-hidden="true" />

      <HomeHeader active="groups" />

      {loading && (
        <p className="px-5 pt-10 text-[14px] font-bold text-[#cbe0f0]/60 lg:px-16">Loading…</p>
      )}
      {!loading && error && (
        <div className="px-5 pt-10 lg:px-16">
          <p className="text-[15px] font-bold text-[#ef5350]">{error}</p>
          <a href="./groups.html" className="mt-2 inline-block text-[13px] font-extrabold text-[#7dd3fc] no-underline hover:underline">
            ← All groups
          </a>
        </div>
      )}
      {!loading && !error && (details ? (
        <GroupDetails group={details} />
      ) : (
        <>
          <GroupsList groups={groups} favorites={favorites} />
          <MyGroupsPanel />
        </>
      ))}

      <RecordTicker />
    </div>
  );
}

export default Groups;
