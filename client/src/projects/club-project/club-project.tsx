import React, { useMemo, useState } from 'react';
import '../../index.css';
import '../components/deep/deep-theme.css';
import { useClubOverview, type ClubScope } from '../../hooks/useClubOverview';
import { parseRoute, routes } from '../../utils/routes';
import ClubHero from './components/club-hero';
import ClubFilters, { SHOW_GROUP_TILES } from './components/club-filters';
import DeepSeasonCarousel from '../components/deep/season-carousel';
import ClubGrid from './components/club-grid';
import ClubStandings from './components/club-standings';
import ClubTimeline from './components/club-timeline';
import ClubTopSwimmers from './components/club-top-swimmers';
import ClubSwimmers from './components/club-swimmers';
import ClubRecords from './components/club-records';
import ClubRecordWall from './components/club-record-wall';
import ClubCoaches from './components/club-coaches';
import ClubSoonCard from './components/club-soon-card';
import ClubMedia from './components/club-media';
import DeepEntityPage from '../components/deep/entity-page';
import DeepDigestCard from '../components/deep/digest-card';
import DeepDisplaySettingsCard from '../components/deep/display-settings-card';
import { useAuth } from '../../hooks/useAuth';
import ClubAvatar from './components/club-avatar';
import type {
  EntityPageStatus, EntityTabNav, EntityTabSpec,
} from '../components/deep/entity-page-types';

/**
 * Страница клуба (Фаза 10, план docs/plans/club-page-plan.md, этап K5) — ПЕРВЫЙ потребитель
 * общего каркаса `deep/entity-page.tsx` (план docs/plans/entity-page-shell-plan.md, этап A).
 *
 * Устройство: Hero → ОДИН глобальный блок фильтров (сезон + зачётная группа) → карточки.
 * Каждая карточка читает выбранный скоуп и своих фильтров не заводит — единственное
 * исключение у стены рекордов (переключатель бассейна: 25м и 50м несравнимы).
 *
 * Страница сама данных каркасу не «отдаёт»: каркас тупой, он знает только про слоты и
 * порядок. Здесь остаётся то, что специфично клубу — скоуп, набор табов и состав карточек.
 * Поменять порядок карточек = переставить элементы массива; убрать карточку = не класть её.
 *
 * Тема — токены `--deep-*` из дизайн-хендоффа (deep-theme.css): класс .theme-deep или
 * .theme-deep-light навешивает каркас по глобальному режиму light/dark.
 */
/** Табы страницы клуба (TABS.md 3a). Компонент плиток и корпус папки — в каркасе. */
type ClubTab = 'overview' | 'season' | 'records' | 'swimmers' | 'media' | 'history' | 'admin';

function ClubProject() {
  const clubId = useMemo<number | null>(() => parseRoute().clubId, []);
  // Управление клубом — только у админа сайта: владельцев у клуба не существует
  // (docs/plans/entity-page-shell-plan.md §3.8). Появятся («claim your club») — гейт станет
  // таким же, как у группы, и правило таба менять не придётся.
  const { isAdmin } = useAuth();

  const [scope, setScope] = useState<ClubScope>({
    season: null,
    group: null,
    standingCompetitionId: null,
  });

  const { data, loading, error } = useClubOverview(clubId, scope);

  // Плашку загрузки показываем ТОЛЬКО пока данных нет вовсе. При смене сезона/зачёта данные
  // остаются на экране и обновляются на месте: иначе плашка вставлялась над контентом и вся
  // страница прыгала вниз-вверх на каждый клик по фильтру.
  const status: EntityPageStatus =
    data ? 'ready'
      : clubId == null ? 'notfound'
        : loading ? 'loading'
          : error === 'not-found' ? 'notfound'
            : error ? 'error'
              : 'loading';

  const scopeLabel =
    scope.season == null
      ? 'all seasons'
      : data?.seasons.find((s) => s.season === scope.season)?.label ?? String(scope.season);

  // Табы собираются только когда данные есть: подписи-сводки — живые числа, а не хардкод.
  const tabs: EntityTabSpec<ClubTab>[] = data == null || clubId == null ? [] : ([
    {
      // Дайджест: срезы соседних табов из УЖЕ загруженного `overview`-ответа. Рекордов тут
      // нет намеренно — их строки живут в своих пагинируемых эндпоинтах внутри карточек, а
      // выдумывать цифру ради витрины нельзя (в шапке она и так есть плиткой KPI).
      id: 'overview',
      icon: '▦',
      label: 'Overview',
      sub: 'latest meets · top swimmers',
      cards: (nav: EntityTabNav<ClubTab>) => [
        {
          id: 'latest-meets',
          span: 'half' as const,
          render: () => (
            <DeepDigestCard
              title="Latest championships"
              subtitle={`rank and medals · ${scopeLabel}`}
              moreLabel={`All ${data.timeline.length} competitions →`}
              onMore={() => nav.go('history')}
              isEmpty={data.timeline.length === 0}
              emptyText="No competitions in this scope."
            >
              <div className="flex flex-col gap-1.5">
                {data.timeline.slice(0, 4).map((t) => (
                  <div
                    key={t.competition_id}
                    className="flex items-center justify-between gap-3 px-3 py-2"
                    style={{ background: 'var(--deep-card-bg-row)', borderRadius: 'var(--deep-radius-row)' }}
                  >
                    <div className="min-w-0">
                      <div className="truncate text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                        {t.name}
                      </div>
                      <div className="hp-mono truncate text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                        {t.date}{t.group_name ? ` · ${t.group_name}` : ''}
                      </div>
                    </div>
                    <div className="flex shrink-0 items-center gap-3">
                      {t.gold + t.silver + t.bronze > 0 && (
                        <span className="hp-mono text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                          {t.gold > 0 && <>🥇{t.gold} </>}
                          {t.silver > 0 && <>🥈{t.silver} </>}
                          {t.bronze > 0 && <>🥉{t.bronze}</>}
                        </span>
                      )}
                      <span
                        className="text-[15px]"
                        style={{
                          fontFamily: 'var(--deep-font-display)',
                          color: t.rank === 1 ? 'var(--deep-gold)' : 'var(--deep-text)',
                        }}
                      >
                        #{t.rank}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </DeepDigestCard>
          ),
        },
        {
          id: 'top-swimmers-digest',
          span: 'half' as const,
          render: () => (
            <DeepDigestCard
              title="Top swimmers"
              subtitle={`club points from individual swims · ${scopeLabel}`}
              count={data.club.swimmer_count}
              countLabel="SWIMMERS"
              moreLabel="Full roster →"
              onMore={() => nav.go('swimmers')}
              isEmpty={data.top_swimmers.length === 0}
              emptyText="No scoring swims in this scope."
            >
              <div className="flex flex-col gap-1.5">
                {data.top_swimmers.slice(0, 4).map((sw) => (
                  <a
                    key={sw.swimmer_id}
                    href={routes.swimmer(sw.swimmer_id)}
                    className="flex items-center gap-3 px-3 py-2 no-underline"
                    style={{ background: 'var(--deep-card-bg-row)', borderRadius: 'var(--deep-radius-row)' }}
                  >
                    <ClubAvatar
                      firstName={sw.first_name || sw.first_name_en}
                      lastName={sw.last_name || sw.last_name_en}
                      gender={sw.gender}
                      size={26}
                    />
                    {/* Имя на иврите по умолчанию, латиница — только фоллбеком (правило проекта). */}
                    <span className="min-w-0 flex-1 truncate text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                      {`${sw.last_name} ${sw.first_name}`.trim() || `${sw.last_name_en} ${sw.first_name_en}`.trim()}
                    </span>
                    <span className="hp-mono shrink-0 text-[11.5px] font-extrabold" style={{ color: 'var(--deep-accent)' }}>
                      {sw.points}
                    </span>
                  </a>
                ))}
              </div>
            </DeepDigestCard>
          ),
        },
      ],
    },
    {
      id: 'season',
      icon: '▦',
      label: 'Season',
      sub: 'grid · standings',
      cards: () => [
        // Плитки групп временно скрыты (SHOW_GROUP_TILES) — карточку фильтра не кладём
        // совсем, чтобы не оставлять пустую рамку.
        ...(SHOW_GROUP_TILES ? [{
          id: 'filters',
          render: () => (
            <ClubFilters
              groups={data.groups}
              group={scope.group}
              // Смена скоупа сбрасывает раскрытый зачёт: он мог принадлежать другому сезону.
              onGroup={(group) => setScope((s) => ({ ...s, group, standingCompetitionId: null }))}
            />
          ),
        }] : []),
        // Грид и таблица зачёта — пара: клик по линии слева меняет таблицу справа, поэтому
        // на десктопе они стоят рядом по половине ширины (порог 960px задаёт каркас).
        {
          id: 'grid',
          span: 'half' as const,
          render: () => (
            <ClubGrid
              grid={data.grid}
              currentSeason={scope.season}
              selectedCompetitionId={data.standings?.competition_id ?? null}
              onPickStanding={(competitionId) =>
                setScope((s) => ({ ...s, standingCompetitionId: competitionId }))
              }
            />
          ),
        },
        {
          id: 'standings',
          span: 'half' as const,
          render: () => <ClubStandings standings={data.standings} />,
        },
      ],
    },
    {
      // Число рекордов знает сама карточка (свой эндпоинт с фильтром пула),
      // страница его не грузит — цифру не выдумываем.
      id: 'records',
      icon: '⏱',
      label: 'Records',
      sub: 'wall · best season',
      // Ростер и рекорды — отдельные пагинируемые эндпоинты (K4.2), им нужен уже-резолвленный
      // clubId (гарантирован здесь: табы собираются только при непустых data и clubId).
      cards: () => [
        // Времена парой: Season best — наши протоколы за ТЕКУЩИЙ сезон по возрастным ступеням
        // (глобальный фильтр сезона не слушает — см. club-records.tsx), Record wall —
        // официальный справочник рекордов (сезона у него нет). Данные разные, форма общая.
        { id: 'record-wall', span: 'half' as const, render: () => <ClubRecordWall clubId={clubId} /> },
        { id: 'season-best', span: 'half' as const, render: () => <ClubRecords clubId={clubId} /> },
        // Best season из макета — карточки ещё нет (нужен сезонный агрегат по клубу).
        {
          id: 'best-season',
          render: () => (
            <ClubSoonCard
              title="Best season"
              sub="The club's strongest season by rank and medals"
              text="Not built yet — needs a per-season aggregate on the API side."
            />
          ),
        },
      ],
    },
    {
      id: 'swimmers',
      icon: '🏊',
      label: 'Swimmers',
      sub: `${data.club.swimmer_count} · coaches`,
      cards: () => [
        // Люди клуба парой: слева выжимка «кто тащит», справа полный ростер.
        {
          id: 'top-swimmers',
          span: 'half' as const,
          render: () => <ClubTopSwimmers swimmers={data.top_swimmers} scopeLabel={scopeLabel} />,
        },
        {
          id: 'roster',
          span: 'half' as const,
          render: () => <ClubSwimmers clubId={clubId} season={scope.season} />,
        },
        { id: 'coaches', render: () => <ClubCoaches /> },
      ],
    },
    {
      id: 'media',
      icon: '▶',
      label: 'Media',
      sub: 'from swimmers',
      // Лента клуба = одобренные public-публикации его пловцов. Ростер клуба приходит из
      // справочника федерации, поэтому вести состав руками не нужно — этим клубная лента и
      // отличается от групповой (docs/plans/entity-page-shell-plan.md §3.10).
      cards: () => [{ id: 'club-media', render: () => <ClubMedia clubId={clubId} /> }],
    },
    {
      id: 'history',
      icon: '🗓',
      label: 'History',
      sub: `${data.timeline.length} competitions`,
      cards: () => [{ id: 'timeline', render: () => <ClubTimeline timeline={data.timeline} /> }],
    },
    // Управление — отдельным табом и только админу сайта; остальным его нет вовсе.
    isAdmin && {
      id: 'admin' as const,
      icon: '⚙',
      label: 'Admin',
      sub: 'page display',
      cards: () => [{
        id: 'display-settings',
        render: () => (
          <DeepDisplaySettingsCard
            entity="club"
            entityId={data.club.id}
            coverImageUrl={data.club.cover_image_url}
            showHeroImage={data.club.show_hero_image}
            // Пикера «взять из медиа» у клуба нет: клубной медиа-ленты не существует
            // (план §3.10) — рисовать пустой выбор было бы враньём.
          />
        ),
      }],
    },
  ].filter(Boolean) as EntityTabSpec<ClubTab>[]);

  return (
    <DeepEntityPage<ClubTab>
      status={status}
      messages={{ notfound: 'Club not found', error: 'Could not load this club' }}
      hero={data ? <ClubHero club={data.club} kpi={data.kpi} /> : null}
      beforeTabs={
        data ? (
          /* Полоса сезонов стоит МЕЖДУ шапкой и табами и действует на всю страницу
             (handoff filter-season 4c). Карточки, которым сезон не положен — Record wall
             (у рекорда сезона нет) и Season best (сознательно живёт текущим сезоном) —
             его по-прежнему не слушают. */
          <DeepSeasonCarousel
            seasons={data.seasons}
            season={scope.season}
            // Смена сезона сбрасывает раскрытый зачёт: он мог принадлежать другому.
            onSeason={(season) => setScope((s) => ({ ...s, season, standingCompetitionId: null }))}
          />
        ) : null
      }
      tabsAriaLabel="Club sections"
      tabs={tabs}
    />
  );
}

export default ClubProject;
