import React, { useEffect, useState } from 'react';
import DeepEntityPage from '../components/deep/entity-page';
import type {
  EntityPageStatus, EntityTabNav, EntityTabSpec,
} from '../components/deep/entity-page-types';
import { routes } from '../../utils/routes';
import { useCurrentIdentity, useHubGroupMembership, useMyHubGroups } from './use-my-hub-groups';
import GroupHero from './components/group-hero';
import {
  GroupLastStartCard, GroupMembersCard, GroupMembersDigest, GroupRecentSwimsCard,
  GroupRecordsCard, GroupRecordsDigest, GroupStandingsCard,
} from './components/group-cards';
import {
  FromMembersGallery, GroupGallery, MembersPublications, MembersReviews,
} from './components/group-media';
import PublicationsInbox from './components/group-admin';
import GroupJoinPolicyCard from './components/group-join-policy';
import GroupScheduleEditor from './components/group-schedule-editor';
import DeepDisplaySettingsCard from '../components/deep/display-settings-card';
import type { HubGroupDetails } from './types';

/**
 * Страница группы `/groups/{slug}` — ТРЕТИЙ потребитель общего каркаса
 * (`deep/entity-page.tsx`, этап C плана docs/plans/entity-page-shell-plan.md).
 *
 * Группа и клуб — не разные сущности, а два вида одного: коллектив пловцов (решение Влада
 * 09.09.2026). Поэтому устройство страницы то же самое, что у клуба, и словарь табов тот же
 * (`Season · Records · Swimmers · Media`); своё у группы — только шапка и два таба, которых
 * у клуба быть не может: тренировки и админский инбокс.
 *
 * Список групп `/groups` живёт отдельно (`groups.tsx`) и остаётся в семье hp: это витрина,
 * а не страница сущности.
 */

type GroupTab = 'overview' | 'season' | 'records' | 'swimmers' | 'media' | 'trainings' | 'admin';

function GroupPage({ slug }: { slug: string }) {
  const [group, setGroup] = useState<HubGroupDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<'not-found' | 'failed' | null>(null);

  // Решение в инбоксе меняет оба published-списка — форсируем их рефетч ремаунтом по ключу.
  const [publicationsReloadKey, setPublicationsReloadKey] = useState(0);

  const { isAuthenticated, isAdmin } = useCurrentIdentity();
  const { groups: myGroups } = useMyHubGroups(isAuthenticated);
  const { joined } = useHubGroupMembership();

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    // favorites — авторизованный эндпоинт с тем же контрактом.
    const url = slug === 'favorites'
      ? '/api/hub-groups/favorites'
      : `/api/hub-groups/${encodeURIComponent(slug)}`;

    fetch(url, { credentials: 'include' })
      .then((r) => {
        if (r.status === 404) throw new Error('not-found');
        if (!r.ok) throw new Error('failed');
        return r.json() as Promise<HubGroupDetails>;
      })
      .then((data) => { if (!cancelled) setGroup(data); })
      .catch((e: Error) => {
        if (cancelled) return;
        setGroup(null);
        setError(e.message === 'not-found' ? 'not-found' : 'failed');
      })
      .finally(() => { if (!cancelled) setLoading(false); });

    return () => { cancelled = true; };
  }, [slug]);

  const status: EntityPageStatus =
    group ? 'ready'
      : loading ? 'loading'
        : error === 'not-found' ? 'notfound'
          : error ? 'error'
            : 'loading';

  // Управляющий группой — тот же гейт, что у инбокса и тренировок: админ сайта ИЛИ это моя
  // группа. Права на таб `Admin` и на его содержимое обязаны считаться одинаково.
  const manages = group != null && (isAdmin || myGroups.some((g) => g.id === group.id));
  const isMember = group != null && joined.some((j) => j.id === group.id && j.status === 'active');

  // Виртуальное «Моё избранное» — реальной группы за ним нет: ни медиа (галерея висит на
  // HubGroupId, у виртуальной id <= 0), ни тренировок, ни админки. Зачёт, рекорды и состав
  // сервер ей считает тем же агрегатом, что и обычной группе, — эти табы работают.
  const real = group != null && !group.is_virtual && group.id > 0;

  const tabs: EntityTabSpec<GroupTab>[] = group == null ? [] : ([
    {
      // Дайджест — витрина соседних табов, а не шестой набор данных: те же `bests`,
      // `recent_results` и `members`, что у полных карточек, второго запроса нет.
      id: 'overview' as const,
      icon: '▦',
      label: 'Overview',
      sub: 'last start · records',
      cards: (nav: EntityTabNav<GroupTab>) => [
        { id: 'records-digest', render: () => <GroupRecordsDigest group={group} onMore={() => nav.go('records')} /> },
        {
          id: 'last-start',
          span: 'half' as const,
          render: () => <GroupLastStartCard group={group} onMore={() => nav.go('season')} />,
        },
        {
          id: 'members-digest',
          span: 'half' as const,
          render: () => <GroupMembersDigest group={group} onMore={() => nav.go('swimmers')} />,
        },
      ],
    },
    {
      id: 'season' as const,
      icon: '🗓',
      label: 'Season',
      sub: group.season_label || 'standings · swims',
      cards: () => [
        { id: 'standings', render: () => <GroupStandingsCard group={group} /> },
        { id: 'recent-swims', render: () => <GroupRecentSwimsCard group={group} /> },
      ],
    },
    {
      id: 'records' as const,
      icon: '⏱',
      label: 'Records',
      sub: `${group.bests.length} best times`,
      cards: () => [{ id: 'records', render: () => <GroupRecordsCard group={group} /> }],
    },
    {
      id: 'swimmers' as const,
      icon: '🏊',
      label: 'Swimmers',
      sub: `${group.members.length} in the roster`,
      cards: () => [{ id: 'members', render: () => <GroupMembersCard group={group} /> }],
    },
    real && {
      id: 'media' as const,
      icon: '▶',
      label: 'Media',
      sub: 'gallery · from members',
      cards: () => [
        // ⚠ `gallery` из API — это СВОИ медиа группы ПЛЮС одобренные public-публикации
        // участников (сервер домешивает их с отрицательными id, HubGroupsController).
        // Публикации при этом рисует соседняя карточка «From members», и до переезда на
        // табы одно и то же видео показывалось на странице дважды подряд. Здесь оставляем
        // галерее только СВОИ медиа (id > 0); склейка на сервере нужна ленте хайлайтов
        // шапки и не трогается.
        { id: 'gallery', render: () => <GroupGallery gallery={group.gallery.filter((g) => g.id > 0)} /> },
        {
          id: 'from-members',
          render: () => <FromMembersGallery key={`from-members-${publicationsReloadKey}`} group={group} />,
        },
        // 🔒 Разборы и members-публикации сервер отдаёт только участникам; не участнику
        // они не рендерятся вовсе, и таб остаётся публичной своей частью.
        { id: 'reviews', render: () => <MembersReviews group={group} /> },
        {
          id: 'members-publications',
          render: () => <MembersPublications key={`members-publications-${publicationsReloadKey}`} group={group} />,
        },
      ],
    },
    real && {
      id: 'trainings' as const,
      icon: '◷',
      label: 'Trainings',
      sub: '🔒 members only',
      // Таблица тренировок живёт на ДРУГОМ экране (`/groups/{slug}/results?tab=trainings`),
      // поэтому таб — переход туда, а не панель. Непайщику показываем замок, а не прячем
      // таб (правило каркаса: locked ≠ «не класть»).
      locked: !(manages || isMember),
      lockNotice: (
        <div className="deep-card text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          Training sessions are visible to group members only.
        </div>
      ),
      cards: () => [{
        id: 'trainings-link',
        render: () => (
          <section className="deep-card mb-4">
            <div className="deep-card-title">Trainings</div>
            <div className="deep-card-sub mt-1">private — group members and admins</div>
            <a
              href={`${routes.groupResults(group.slug)}?tab=trainings`}
              className="hp-mono mt-4 inline-block rounded-[10px] border px-4 py-2 text-[13px] font-extrabold no-underline"
              style={{
                borderColor: 'var(--deep-accent-border)',
                background: 'var(--deep-accent-chip)',
                color: 'var(--deep-accent)',
              }}
            >
              🔒 Open training log →
            </a>
          </section>
        ),
      }],
    },
    // Управление — только управляющим, и не показывается вовсе остальным (план §3.8):
    // постороннему незачем знать, что у группы есть инбокс.
    real && manages && {
      id: 'admin' as const,
      icon: '⚙',
      label: 'Admin',
      sub: 'display · schedule · joining',
      cards: () => [
        {
          id: 'display-settings',
          render: () => (
            <DeepDisplaySettingsCard
              entity="group"
              entityId={group.id}
              coverImageUrl={group.cover_image_url}
              showHeroImage={group.show_hero_image !== false}
              heroMediaId={group.hero_media_id}
              // Пикер «взять фото из медиа» стоит ЗДЕСЬ, а не кнопкой на карточках таба
              // Media: управление сущностью живёт в одном месте (план §3.8), а лента
              // `gallery` — единственный список, где свои медиа и одобренные публикации
              // лежат в одном пространстве id (публикации приходят с отрицательными).
              media={group.gallery.map((m) => ({
                id: m.id, url: m.url, media_type: m.media_type,
                source_type: m.source_type, caption: m.caption,
              }))}
            />
          ),
        },
        {
          id: 'training-schedule',
          render: () => (
            <GroupScheduleEditor groupId={group.id} schedule={group.training_schedule} />
          ),
        },
        {
          id: 'join-policy',
          render: () => (
            <GroupJoinPolicyCard
              groupId={group.id}
              policy={group.join_policy === 'approval' ? 'approval' : 'open'}
            />
          ),
        },
        {
          id: 'publications-inbox',
          render: () => (
            <PublicationsInbox
              group={group}
              onDecided={() => setPublicationsReloadKey((k) => k + 1)}
            />
          ),
        },
      ],
    },
  ].filter(Boolean) as EntityTabSpec<GroupTab>[]);

  return (
    <DeepEntityPage<GroupTab>
      topbarActive="groups"
      status={status}
      messages={{ notfound: 'Group not found', error: 'Could not load this group' }}
      hero={group ? <GroupHero group={group} /> : null}
      tabsAriaLabel="Group sections"
      tabs={tabs}
    />
  );
}

export default GroupPage;
