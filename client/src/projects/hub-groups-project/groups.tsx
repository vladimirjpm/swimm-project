import React, { useEffect, useMemo, useState } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import AppTopbar from '../components/app-topbar/app-topbar';
import RecordTicker from '../home-project/components/record-ticker';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import MyGroupsPanel from './my-groups-panel';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_FlagEmoji from '../components/mix/flag-icon/flag-icon';
import { routes, parseRoute } from '../../utils/routes';
import GroupPage from './group-page';
import { GroupIcon } from './components/group-bits';
import type { HubGroupDetails, HubGroupListItem } from './types';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';

/**
 * СПИСОК групп `/groups` — витрина семьи hp (шиммер, лента рекордов, тумблер режима внизу).
 *
 * Страница ОДНОЙ группы `/groups/{slug}` живёт отдельно, в `group-page.tsx`, и стоит на общем
 * каркасе страницы сущности вместе с клубом и пловцом (этап C плана
 * docs/plans/entity-page-shell-plan.md). Здесь остался только корневой переключатель:
 * есть slug — отдаём страницу группы, нет — рисуем витрину.
 *
 * Виртуальная группа «Моё избранное» (/api/hub-groups/favorites) показывается первой
 * карточкой залогиненному.
 */

// ── Список групп ─────────────────────────────────────────────────────────────

function GroupCard({ group, href }: { group: HubGroupListItem; href: string }) {
  return (
    <a
      href={href}
      className="hp-card-std flex min-h-[130px] flex-col justify-between gap-4 rounded-[18px] border border-[var(--t-border)] p-[18px] text-inherit no-underline shadow-[var(--t-shadow)] backdrop-blur-[14px] transition-[transform,border-color,box-shadow] duration-[180ms] ease-out hover:-translate-y-2 hover:border-[var(--t-accent)] lg:rounded-[24px] lg:p-[26px]"
    >
      <div className="flex items-start gap-4">
        <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="sm" />
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <div className="truncate text-[19px] font-black tracking-[-0.02em] lg:text-[22px]">
              {group.name}
            </div>
            {group.is_official && group.club_name && (
              <UI_ClubIcon clubName={group.club_name} iconWidth="6" styleType="icon-notext" />
            )}
          </div>
          {group.name_en && group.name_en !== group.name && (
            <div className="truncate text-[12px] font-bold text-[var(--t-text-2)]">{group.name_en}</div>
          )}
        </div>
      </div>
      {group.description && (
        <p className="line-clamp-2 text-[13px] leading-snug text-[var(--t-text-2)]">{group.description}</p>
      )}
      <div className="flex items-center justify-between">
        <span className="hp-mono rounded-[7px] border border-[var(--t-accent-border)] px-2 py-[3px] text-[11px] font-extrabold text-[var(--t-accent)]">
          {group.member_count} · swimmers
        </span>
        <span className="inline-flex min-w-0 items-center gap-1.5 truncate pl-3 text-[12px] font-bold text-[var(--t-text-2)]">
          {group.country && <UI_FlagEmoji countryCode={group.country} size="16x12" />}
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
        <p className="mb-[18px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[var(--t-accent)] lg:text-[15px] lg:tracking-[0.3em]">
          Train together · Follow together
        </p>
        <h1 className="text-[44px] font-black leading-[0.92] tracking-[-0.045em] text-[var(--t-text)] lg:text-[88px] lg:leading-[0.9]">
          Groups
        </h1>
        <p className="mt-5 max-w-[560px] text-[14.5px] leading-[1.55] text-[var(--t-text-2)] lg:text-[18px] lg:leading-[1.6]">
          Training groups: rosters, group records and recent swims.
        </p>
      </section>

      <section
        className="grid grid-cols-1 gap-3 px-4 pt-[26px] sm:grid-cols-2 lg:grid-cols-3 lg:gap-[18px] lg:px-16 lg:pt-12"
        aria-label="Groups"
      >
        {favorites && (
          <GroupCard
            href={routes.group('favorites')}
            group={{
              slug: 'favorites',
              name: favorites.name,
              name_en: favorites.name_en,
              description: 'Swimmers from your favorites — as a personal group',
              icon_url: null,
              location: null,
              club_name: null,
              is_official: false,
              member_count: favorites.members.length,
            }}
          />
        )}
        {groups.map((g) => (
          <GroupCard key={g.slug} group={g} href={routes.group(g.slug)} />
        ))}
        {groups.length === 0 && !favorites && (
          <p className="col-span-full py-10 text-center text-[14px] text-[var(--t-text-2)]">
            No groups yet.
          </p>
        )}
      </section>
    </>
  );
}

// ── Корневой компонент страницы ──────────────────────────────────────────────

function Groups() {
  // Страница группы: /groups/{slug}; ?group= — легаси-фоллбек.
  const slug = useMemo(() => parseRoute().groupSlug ?? new URLSearchParams(window.location.search).get('group'), []);

  // Есть slug — это страница сущности, и она целиком своя (свой каркас, своя загрузка).
  // Витрина ниже даже не монтируется: у неё другой корпус страницы (семья hp).
  if (slug) return <GroupPage slug={slug} />;

  return <GroupsShowcase />;
}

/** Витрина `/groups`: список публичных групп + «Моё избранное» первым, если залогинен. */
function GroupsShowcase() {
  const deep = useDeepThemeClass();

  const [groups, setGroups] = useState<HubGroupListItem[]>([]);
  const [favorites, setFavorites] = useState<HubGroupDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const [listR, favR] = await Promise.all([
          fetch('/api/hub-groups'),
          // 401 для незалогиненного — норма, карточка избранного просто не показывается
          fetch('/api/hub-groups/favorites', { credentials: 'include' }).catch(() => null),
        ]);
        if (!listR.ok) throw new Error(`Failed to load (${listR.status})`);
        const list: HubGroupListItem[] = await listR.json();
        const fav: HubGroupDetails | null = favR && favR.ok ? await favR.json() : null;
        if (!cancelled) {
          setGroups(list);
          setFavorites(fav);
        }
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load');
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => { cancelled = true; };
  }, []);

  return (
    <div className={`home-page ${deep} relative min-h-screen overflow-x-clip pb-[96px] text-[var(--t-text)]`}>
      <div className="hp-shimmer" aria-hidden="true" />

      <AppTopbar active="groups" />

      {loading && (
        <p className="px-5 pt-10 text-[14px] font-bold text-[var(--t-text-2)] lg:px-16">Loading…</p>
      )}
      {!loading && error && (
        <div className="px-5 pt-10 lg:px-16">
          <p className="text-[15px] font-bold text-[var(--t-danger)]">{error}</p>
        </div>
      )}
      {!loading && !error && (
        <>
          <GroupsList groups={groups} favorites={favorites} />
          <MyGroupsPanel />
        </>
      )}

      <RecordTicker />
      {/* Переключатель тем. На витрине он единственный способ сменить режим: топбар его
          не носит, а внутренние экраны (results, клуб, пловец) — уже другая дверь.
          Отступ снизу считает `home.css` от высоты ленты рекордов. */}
      <UI_ModeToggle />
    </div>
  );
}

export default Groups;
