import React, { useEffect, useState } from 'react';
import DeepEntityPage from '../components/deep/entity-page';
import type {
  EntityPageStatus, EntityTabNav, EntityTabSpec,
} from '../components/deep/entity-page-types';
import { useCurrentIdentity, useHubGroupMembership, useMyHubGroups } from './use-my-hub-groups';
import GroupHero from './components/group-hero';
import { GroupIcon } from './components/group-bits';
import { GroupResultsTab, GROUP_RESULTS_VIEWS, type GroupResultsView } from './components/group-competitions';
import GroupTrainingsTab from './components/group-trainings-tab';
import { formatNextDate, scheduleDaysLabel } from './components/group-training-slots';
import { readViewParam, rewriteLegacyTab, writeViewParam } from '../components/deep/view-chips';
import { useTrainingRsvp } from './use-training-rsvp';
import { rsvpStickyChip } from './components/group-rsvp';
import GroupRsvpBanner, { showRsvpBanner } from './components/group-rsvp-banner';
import GroupMembersOnly from './components/group-members-only';
import {
  GroupLastStartCard, GroupMembersCard, GroupMembersDigest, GroupRecentSwimsCard,
  GroupRecordsDigest, GroupStandingsCard,
} from './components/group-cards';
import {
  FromMembersGallery, GroupGallery, MembersPublications, MembersReviews,
} from './components/group-media';
import PublicationsInbox from './components/group-admin';
import GroupJoinPolicyCard from './components/group-join-policy';
import GroupScheduleEditor from './components/group-schedule-editor';
import GroupLevelsCard from './components/group-levels';
import DeepDisplaySettingsCard from '../components/deep/display-settings-card';
import type { HubGroupDetails, TrainingRsvp } from './types';

/**
 * Страница группы `/groups/{slug}` — ТРЕТИЙ потребитель общего каркаса
 * (`deep/entity-page.tsx`, этап C плана docs/plans/entity-page-shell-plan.md).
 *
 * Группа и клуб — не разные сущности, а два вида одного: коллектив пловцов (решение Влада
 * 09.09.2026). Поэтому устройство страницы то же самое, что у клуба, и словарь табов тот же
 * (`Overview · Season · Results · Swimmers · Media`); своё у группы — только шапка и два
 * таба, которых у клуба быть не может: тренировки и управление.
 *
 * Табы по ролям (хендофф group-club-changes §3): гостю и участнику — шесть в ряд (Trainings
 * гостю под замком); управляющему — пять сверху, а Trainings и Admin — «Coach tools»
 * (на телефоне панелью у низа экрана). Где у управляющего правка (Team — уровни, Media —
 * галерея), таб фиолетовый с ✎.
 *
 * Список групп `/groups` живёт отдельно (`groups.tsx`) и остаётся в семье hp: это витрина,
 * а не страница сущности.
 */

type GroupTab = 'overview' | 'season' | 'results' | 'swimmers' | 'media' | 'trainings' | 'admin';

function GroupPage({ slug }: { slug: string }) {
  const [group, setGroup] = useState<HubGroupDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<'not-found' | 'failed' | null>(null);

  // Решение в инбоксе меняет оба published-списка — форсируем их рефетч ремаунтом по ключу.
  const [publicationsReloadKey, setPublicationsReloadKey] = useState(0);

  // Вид таба Results живёт здесь, а не в табе: дайджест Overview уводит сразу на чип
  // Records («All 20 records →»).
  const [resultsView, setResultsView] = useState<GroupResultsView>(() => {
    // Легаси-табы стали видами: `?tab=records` → Results·Records, `?tab=lanes` →
    // Trainings·Lanes. Переписываем до того, как каркас прочтёт `?tab=`.
    rewriteLegacyTab({ records: { tab: 'results', view: 'records' }, lanes: { tab: 'trainings', view: 'lanes' } });
    return readViewParam(GROUP_RESULTS_VIEWS, 'results');
  });
  const pickResultsView = (next: GroupResultsView) => {
    setResultsView(next);
    writeViewParam(next, 'results');
  };

  const { isAuthenticated, isAdmin } = useCurrentIdentity();
  const { groups: myGroups } = useMyHubGroups(isAuthenticated);
  // ОДИН экземпляр членства на страницу: от него зависят и чип «✓ Member» в шапке, и замок
  // табов, — после «Join» таб Trainings должен открыться без перезагрузки.
  const { joined, join, leave } = useHubGroupMembership();

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

  // Приватная группа, зритель не участник (§6-6): сервер прислал заглушку без данных — табов нет.
  const membersOnly = group?.members_only === true;

  // Ответы на ближайшее занятие (Ш2): запрос только своим — участнику и управляющему; сервер
  // ответил бы остальным 403. Один экземпляр — его читают шапка, липкая полоса и Trainings.
  const rsvp = useTrainingRsvp(
    real ? group!.id : null,
    group?.next_training?.id,
    real && (manages || isMember) && !membersOnly,
  );

  const membershipStatus = group == null
    ? null
    : (joined.find((j) => j.id === group.id)?.status ?? null) as 'active' | 'pending' | null;

  // «· 3 new» на Admin — ждущие заявки на публикацию. Тот же эндпоинт, что у инбокса;
  // перечитываем после решения в инбоксе (тот же ключ, что у соседних списков).
  const groupId = group?.id ?? 0;
  const [pendingPublications, setPendingPublications] = useState(0);
  useEffect(() => {
    if (!real || !manages) { setPendingPublications(0); return undefined; }
    let cancelled = false;
    fetch(`/api/hub-groups/${groupId}/media/publications`, { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : []))
      .then((rows: Array<{ status?: string }>) => {
        if (!cancelled) setPendingPublications(rows.filter((x) => x.status === 'pending').length);
      })
      .catch(() => { if (!cancelled) setPendingPublications(0); });
    return () => { cancelled = true; };
  }, [real, manages, groupId, publicationsReloadKey]);

  const tabs: EntityTabSpec<GroupTab>[] = group == null || membersOnly ? [] : ([
    {
      // Дайджест — витрина соседних табов, а не шестой набор данных: те же `bests` и
      // `members`, что у полных карточек, плюс `last_start` из того же ответа — второго
      // запроса нет.
      id: 'overview' as const,
      icon: '▦',
      label: 'Overview',
      shortLabel: 'Home',
      sub: 'last start · records',
      cards: (nav: EntityTabNav<GroupTab>) => [
        {
          id: 'records-digest',
          render: () => (
            <GroupRecordsDigest
              group={group}
              onMore={() => { pickResultsView('records'); nav.go('results'); }}
            />
          ),
        },
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
      // Records и Season bests — чипы ВНУТРИ Results, как у пловца (хендофф §3).
      id: 'results' as const,
      icon: '⏱',
      label: 'Results',
      sub: 'results · records · SB',
      cards: () => [{
        id: 'results',
        render: () => <GroupResultsTab group={group} view={resultsView} onView={pickResultsView} />,
      }],
    },
    {
      id: 'swimmers' as const,
      icon: '🏊',
      label: 'Swimmers',
      shortLabel: 'Team',
      sub: real && manages ? `${group.members.length} · levels` : `${group.members.length} in the roster`,
      editable: real && manages,
      // Управляющему — выпадашка уровня у каждого пловца (docs/plans/lane-plans-plan.md).
      cards: () => [{ id: 'members', render: () => <GroupMembersCard group={group} editLevels={real && manages} /> }],
    },
    real && {
      id: 'media' as const,
      icon: '▶',
      label: 'Media',
      sub: 'gallery · from members',
      // Галерея группы правится управляющим; заявки на публикацию — в Admin.
      editable: manages,
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
      shortLabel: 'Train',
      sub: manages || isMember ? 'sessions · lanes' : '🔒 members only',
      // Управляющему — инструмент (Coach tools), участнику — обычный таб. Непайщику замок, а
      // не пропажа таба (правило каркаса: locked ≠ «не класть»). Внутри — Sessions | Lanes:
      // план дорожек был отдельным табом (docs/plans/lane-plans-plan.md).
      pinned: manages,
      locked: !(manages || isMember),
      lockNotice: (
        <div
          className="rounded-[12px] border border-dashed p-3.5 text-[13px] font-bold"
          style={{ borderColor: 'var(--deep-card-border)', color: 'var(--deep-text-mute)' }}
        >
          🔒 Trainings and lane plans are visible to group members only.
        </div>
      ),
      cards: () => [{
        id: 'trainings',
        render: () => <GroupTrainingsTab group={group} manages={manages} rsvp={rsvp} />,
      }],
    },
    // Управление — только управляющим, и не показывается вовсе остальным (план §3.8):
    // постороннему незачем знать, что у группы есть инбокс.
    real && manages && {
      id: 'admin' as const,
      icon: '⚙',
      label: 'Admin',
      sub: 'display · schedule · levels · joining',
      pinned: true,
      badge: pendingPublications > 0 ? `${pendingPublications} new` : undefined,
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
              coverImageMobileUrl={group.cover_image_mobile_url}
              heroMobileMediaId={group.hero_mobile_media_id}
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
          // Уровни пловцов — оценка тренера, приватные (docs/plans/lane-plans-plan.md, L1).
          id: 'levels',
          render: () => <GroupLevelsCard groupId={group.id} />,
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
      hero={group ? (membersOnly ? <GroupMembersOnly group={group} /> : (nav: EntityTabNav<GroupTab>) => {
        // Режим «сверху» (Ш4): не ответившему участнику — баннер над фото, NEXT в шапке прячется.
        const banner = showRsvpBanner(group, rsvp);
        return (
          <>
            {banner && <GroupRsvpBanner group={group} rsvp={rsvp} />}
            <GroupHero
              group={group}
              insider={manages || isMember}
              membership={{ isAuthenticated, status: membershipStatus, join, leave }}
              rsvp={rsvp}
              onWhosComing={() => nav.go('trainings')}
              hideTraining={banner}
            />
          </>
        );
      }) : null}
      sticky={group && !membersOnly ? {
        avatar: <GroupIcon iconUrl={group.icon_url} name={group.name_en || group.name} size="xs" />,
        name: group.name,
        nameEn: group.name_en,
        // Расписания нет — чипа нет вовсе (а не пустой контейнер справа).
        status: scheduleDaysLabel(group.training_schedule)
          ? <GroupStickyStatus group={group} insider={manages || isMember} rsvp={rsvp.rsvp} />
          : null,
      } : undefined}
      toolsLabel="Coach tools"
      beforeTabs={group?.is_private && !membersOnly ? (
        // Участник видит приватную группу целиком — пусть знает, что остальным она закрыта.
        <p className="mb-4 text-[12px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          🔒 Private group — only members see this page. Everyone else sees a “members only” notice
          and can only request to join.
        </p>
      ) : null}
      tabsAriaLabel="Group sections"
      tabs={tabs}
    />
  );
}

/**
 * Чип справа в липкой полосе (хендофф §5): своему — ответ на ближайшее занятие (или
 * «going?», или счётчики тренеру); пока ответы не приехали — ближайшее занятие; постороннему —
 * дни расписания.
 */
function GroupStickyStatus({
  group, insider, rsvp,
}: {
  group: HubGroupDetails;
  insider: boolean;
  rsvp: TrainingRsvp | null;
}) {
  const days = scheduleDaysLabel(group.training_schedule);
  const next = group.next_training;
  if (!days) return null;
  // Приехали ответы — чип говорит о них: свой ответ заливкой, «going?» не ответившему,
  // тренеру — счётчики (хендофф §5).
  if (insider && next && rsvp && rsvp.session_id === next.id) {
    const chip = rsvpStickyChip(rsvp, formatNextDate(next.date).split(' ')[0]);
    return (
      <span
        className="hp-mono flex h-7 items-center rounded-[8px] border px-2.5 text-[11px] font-extrabold min-[960px]:h-[30px] min-[960px]:rounded-[9px] min-[960px]:px-3 min-[960px]:text-[12px]"
        style={chip.style}
      >
        {chip.label}
      </span>
    );
  }
  // «Tue 20:00» — день недели из той же даты, что в шапке, без числа и месяца.
  const nextLabel = insider && next ? `Next · ${formatNextDate(next.date).split(' ')[0]} ${next.start}` : null;
  return (
    <span
      className="hp-mono flex h-7 items-center rounded-[8px] border px-2.5 text-[11px] font-extrabold min-[960px]:h-[30px] min-[960px]:rounded-[9px] min-[960px]:px-3 min-[960px]:text-[12px]"
      style={nextLabel
        ? { borderColor: 'var(--deep-live-border)', background: 'var(--deep-card-bg)', color: 'var(--deep-live)' }
        : { borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)', color: 'var(--deep-text-mute)' }}
    >
      {nextLabel ?? days}
    </span>
  );
}

export default GroupPage;
