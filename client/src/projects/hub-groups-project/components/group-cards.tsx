import React, { useMemo, useState } from 'react';
import { ClubRecordCard, ClubRecordTile, type PoolFilter } from '../../club-project/components/club-record-card';
import DeepDigestCard from '../../components/deep/digest-card';
import SwimRow from '../../components/swim-row/swim-row';
import { routes } from '../../../utils/routes';
import { GROUP_DISCLAIMER, ROLE_LABEL, swimmerDisplayName } from './group-bits';
import type { HubGroupDetails, HubGroupStanding } from '../types';

/**
 * Карточки страницы группы — на ОБЩИХ примитивах, а не на своей вёрстке (этап C плана
 * docs/plans/entity-page-shell-plan.md).
 *
 * До переезда здесь стояли три сырых `<table>`: сезонный зачёт, рекорды группы и последние
 * заплывы. Своя таблица заплывов молча теряла то, что общая строка уже умеет — пометку
 * спорного времени, эмблему клуба с фоллбеком, единый формат даты, дугу уровня. Теперь:
 *
 *  • рекорды группы — `ClubRecordCard` + `ClubRecordTile` (та же форма, что у клуба);
 *  • последние заплывы — `SwimRow` в контейнере `.deep-list`;
 *  • зачёт — таблица и осталась таблицей: это действительно табличные данные (место, очки,
 *    медали, FINA), строкой заплыва их не выразить. Переехали только токены.
 *
 * ⚠ «Рекорд группы» — это НЕ официальный рекорд: лучшее время среди участников по оси
 * стиль+дистанция+бассейн+пол за всё время. Одинаковая с клубом форма, разный смысл
 * (план §2.2) — поэтому и подпись карточки говорит про участников.
 */

/** Пул из строки: у рекордов группы он приходит как «25m»/«50m» либо null. */
const poolOf = (value?: string | null): PoolFilter | null =>
  value === '25m' || value === '50m' ? value : null;

/** Участники: тренер и капитаны первыми, дальше как отдал сервер. */
function GroupMembersCard({ group }: { group: HubGroupDetails }) {
  return (
    <section className="deep-card mb-4" aria-label="Members">
      <div className="deep-card-title">Members</div>
      <div className="deep-card-sub mt-1">
        {group.members.length} in the roster
      </div>

      {group.members.length === 0 ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          {group.is_virtual
            ? 'No swimmers in favorites yet — tap the hearts on results.'
            : 'The roster is empty for now.'}
        </div>
      ) : (
        <ul className="m-0 mt-4 flex list-none flex-col gap-1.5 p-0">
          {group.members.map((m) => (
            <li
              key={m.swimmer_id}
              className="flex items-center justify-between gap-3 px-3 py-2"
              style={{
                background: 'var(--deep-card-bg-row)',
                borderRadius: 'var(--deep-radius-row)',
              }}
            >
              <a
                href={routes.swimmer(m.swimmer_id)}
                className="min-w-0 no-underline"
                style={{ color: 'inherit' }}
              >
                <div className="truncate text-[14px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                  {m.name || m.name_en}
                </div>
                <div className="truncate text-[11.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                  {[m.birth_year > 0 ? m.birth_year : null, m.club_name].filter(Boolean).join(' · ')}
                </div>
              </a>
              {ROLE_LABEL[m.role] && (
                <span
                  className="hp-mono shrink-0 rounded-[7px] border px-2 py-[3px] text-[10.5px] font-extrabold uppercase"
                  style={{
                    borderColor: 'var(--deep-accent-border)',
                    background: 'var(--deep-accent-chip)',
                    color: 'var(--deep-accent)',
                  }}
                >
                  {ROLE_LABEL[m.role]}
                </span>
              )}
            </li>
          ))}
        </ul>
      )}

      {/* Ростер собирает человек, а не федерация — это надо говорить вслух: иначе группа
          читается как официальный состав клуба. У виртуального «избранного» оговорки нет,
          там и состава-то чужого нет. */}
      {!group.is_virtual && (
        <p className="mt-3 text-[11px] italic leading-snug" style={{ color: 'var(--deep-text-ghost)' }}>
          {GROUP_DISCLAIMER}
        </p>
      )}
    </section>
  );
}

/** Сезонный зачёт участников. Табличные данные — остаются таблицей. */
function GroupStandingsCard({ group }: { group: HubGroupDetails }) {
  const cellCls = 'px-3 py-2 text-left text-[13px]';
  const headCls = 'px-3 py-2 text-left text-[10.5px] font-extrabold uppercase tracking-[0.18em]';
  const empty = group.standings.length === 0 || group.standings.every((s) => s.swims === 0);

  return (
    <section className="deep-card mb-4" aria-label="Season standings">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <div className="deep-card-title">Season standings</div>
          <div className="deep-card-sub mt-1">swims, medals and points of the roster</div>
        </div>
        {group.season_label && (
          <span className="hp-mono text-[11.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
            {group.season_label}
          </span>
        )}
      </div>

      {empty ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          No swims this season.
        </div>
      ) : (
        <div className="mt-4 overflow-x-auto">
          <table className="w-full border-collapse" style={{ color: 'var(--deep-text-mute)' }}>
            <thead>
              <tr style={{ borderBottom: '1px solid var(--deep-card-border)' }}>
                <th className={`${headCls} text-right`} style={{ color: 'var(--deep-accent)' }}>#</th>
                <th className={headCls} style={{ color: 'var(--deep-accent)' }}>Swimmer</th>
                <th className={`${headCls} text-right`} style={{ color: 'var(--deep-accent)' }}>Swims</th>
                <th className={headCls} style={{ color: 'var(--deep-accent)' }}>Medals</th>
                <th className={`${headCls} text-right`} style={{ color: 'var(--deep-accent)' }}>Points</th>
                <th className={`${headCls} text-right`} style={{ color: 'var(--deep-accent)' }}>Best FINA</th>
              </tr>
            </thead>
            <tbody>
              {group.standings.map((s: HubGroupStanding, i) => (
                <tr key={s.swimmer_id} style={{ borderBottom: '1px solid var(--deep-card-border)' }}>
                  <td className={`${cellCls} hp-mono text-right font-extrabold`}>{i + 1}</td>
                  <td className={`${cellCls} font-extrabold`} style={{ color: 'var(--deep-text)' }}>
                    <a href={routes.swimmer(s.swimmer_id)} className="no-underline" style={{ color: 'inherit' }}>
                      {s.name || s.name_en}
                    </a>
                    {ROLE_LABEL[s.role] && (
                      <span
                        className="hp-mono ml-2 rounded-[6px] border px-[6px] py-[2px] text-[10px] font-extrabold uppercase"
                        style={{ borderColor: 'var(--deep-card-border)', color: 'var(--deep-text-mute)' }}
                      >
                        {ROLE_LABEL[s.role]}
                      </span>
                    )}
                  </td>
                  <td className={`${cellCls} hp-mono text-right`}>{s.swims || '—'}</td>
                  <td className={`${cellCls} whitespace-nowrap`}>
                    {s.golds + s.silvers + s.bronzes === 0 ? (
                      <span style={{ color: 'var(--deep-text-ghost)' }}>—</span>
                    ) : (
                      <span className="hp-mono">
                        {s.golds > 0 && <span className="mr-2">🥇{s.golds}</span>}
                        {s.silvers > 0 && <span className="mr-2">🥈{s.silvers}</span>}
                        {s.bronzes > 0 && <span>🥉{s.bronzes}</span>}
                      </span>
                    )}
                  </td>
                  <td className={`${cellCls} hp-mono text-right font-extrabold`} style={{ color: 'var(--deep-accent)' }}>
                    {s.club_points || '—'}
                  </td>
                  <td className={`${cellCls} hp-mono text-right`}>{s.best_fina || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/** Рекорды группы — та же форма, что у стены рекордов клуба. */
function GroupRecordsCard({ group }: { group: HubGroupDetails }) {
  const [pool, setPool] = useState<PoolFilter>('all');

  const rows = useMemo(
    () => (pool === 'all' ? group.bests : group.bests.filter((b) => poolOf(b.pool_type) === pool)),
    [group.bests, pool],
  );

  return (
    <ClubRecordCard
      title="Group records"
      subtitle="best time among the roster · by event and pool"
      count={rows.length}
      countLabel="RECORDS"
      pool={pool}
      onPool={setPool}
      emptyText="No counted results yet."
      isEmpty={rows.length === 0}
    >
      {rows.map((b) => (
        <ClubRecordTile
          key={`${b.style_name}-${b.distance}-${b.pool_type}-${b.gender}`}
          gender={b.gender}
          topLine={`${b.distance} ${b.style_name}`}
          secondLine={b.pool_type ?? undefined}
          time={b.time_original}
          quality={b.suspect_reason ? { kind: 'protocol', reason: b.suspect_reason } : null}
          name={b.swimmer_name || b.swimmer_name_en}
          footnote={`${b.date} · ${b.points || 0} pts`}
          href={routes.swimmer(b.swimmer_id)}
        />
      ))}
    </ClubRecordCard>
  );
}

/** Последние заплывы — ОБЩЕЙ строкой заплыва, а не своей таблицей. */
function GroupRecentSwimsCard({ group }: { group: HubGroupDetails }) {
  return (
    <section className="deep-card mb-4" aria-label="Recent swims">
      <div className="deep-card-title">Recent swims</div>
      <div className="deep-card-sub mt-1">latest results of the roster</div>

      {group.recent_results.length === 0 ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          No swims yet.
        </div>
      ) : (
        <div className="deep-list mt-4">
          {group.recent_results.map((r) => (
            <SwimRow
              key={r.id}
              stroke={r.event_style_name}
              distance={r.event_style_len}
              poolType={r.pool_type}
              time={r.time}
              timeFail={r.time_fail}
              // Место протокольное; награждаемость строка ленты не знает, поэтому диск серый —
              // выдавать его за медаль нельзя (правило четырёх видов места, см. SwimRow).
              place={r.position != null ? { kind: 'medal', value: r.position, isAward: false } : { kind: 'none' }}
              swimmer={{
                name: swimmerDisplayName(r.last_name, r.first_name, r.last_name_en, r.first_name_en),
                showClubIcon: false,
              }}
              competition={{ name: r.competition }}
              date={r.date}
              points={r.international_points}
              extras={r.is_relay ? <span>relay</span> : undefined}
              // Ссылки на старт у строки нет: лента отдаёт НАЗВАНИЕ соревнования, но не его
              // id (`HubGroupRecentResult`), а `routes.competitionSwims` просит именно id.
              // Кликабельной строку сделает добавление id в DTO, а не догадка на клиенте.
            />
          ))}
        </div>
      )}
    </section>
  );
}


/* ── Дайджест (таб Overview) ──────────────────────────────────────────────────
   Срезы соседних табов: показываем немного и уводим туда, где это целиком. Данные те же
   самые, что у полных карточек — второго запроса дайджест не делает. */

const DIGEST_RECORDS = 4;
const DIGEST_MEMBERS = 4;
const DIGEST_SWIMS = 5;

/** Рекорды группы: четыре плитки той же формы, что на своём табе. */
function GroupRecordsDigest({ group, onMore }: { group: HubGroupDetails; onMore: () => void }) {
  return (
    <DeepDigestCard
      title="Group records"
      subtitle="best time among the roster · both pools"
      count={group.bests.length}
      countLabel="RECORDS"
      moreLabel={`All ${group.bests.length} records →`}
      onMore={onMore}
      isEmpty={group.bests.length === 0}
      emptyText="No counted results yet."
    >
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 min-[960px]:grid-cols-4">
        {group.bests.slice(0, DIGEST_RECORDS).map((b) => (
          <ClubRecordTile
            key={`${b.style_name}-${b.distance}-${b.pool_type}-${b.gender}`}
            gender={b.gender}
            topLine={`${b.distance} ${b.style_name}`}
            secondLine={b.pool_type ?? undefined}
            time={b.time_original}
            quality={b.suspect_reason ? { kind: 'protocol', reason: b.suspect_reason } : null}
            name={b.swimmer_name || b.swimmer_name_en}
            footnote={`${b.date} · ${b.points || 0} pts`}
            href={routes.swimmer(b.swimmer_id)}
          />
        ))}
      </div>
    </DeepDigestCard>
  );
}

/**
 * Последний старт: заплывы САМОГО СВЕЖЕГО соревнования ленты.
 *
 * Лента приходит отсортированной по дате вниз, поэтому «последний старт» — это соревнование
 * первой строки, а его заплывы — все строки с тем же названием. По названию, а не по дате:
 * многодневка это несколько дней одного турнира, и срез по дате показал бы один день.
 */
function GroupLastStartCard({ group, onMore }: { group: HubGroupDetails; onMore: () => void }) {
  const first = group.recent_results[0];
  const rows = first ? group.recent_results.filter((r) => r.competition === first.competition) : [];
  const golds = rows.filter((r) => r.position === 1).length;

  return (
    <DeepDigestCard
      title={first ? first.competition : 'Last start'}
      subtitle={first
        ? `${first.date} · ${rows.length} swims${golds > 0 ? ` · ${golds} gold` : ''}`
        : undefined}
      moreLabel="All recent swims →"
      onMore={onMore}
      isEmpty={rows.length === 0}
      emptyText="No swims yet."
    >
      <div className="deep-list">
        {rows.slice(0, DIGEST_SWIMS).map((r) => (
          <SwimRow
            key={r.id}
            stroke={r.event_style_name}
            distance={r.event_style_len}
            poolType={r.pool_type}
            time={r.time}
            timeFail={r.time_fail}
            place={r.position != null ? { kind: 'medal', value: r.position, isAward: false } : { kind: 'none' }}
            swimmer={{
              name: swimmerDisplayName(r.last_name, r.first_name, r.last_name_en, r.first_name_en),
              showClubIcon: false,
            }}
            date={r.date}
            points={r.international_points}
          />
        ))}
      </div>
    </DeepDigestCard>
  );
}

/** Состав: тренер и первые участники ростера. */
function GroupMembersDigest({ group, onMore }: { group: HubGroupDetails; onMore: () => void }) {
  return (
    <DeepDigestCard
      title="Members"
      subtitle={group.is_virtual ? 'from your favorites' : 'roster kept by the group creator'}
      count={group.members.length}
      countLabel="SWIMMERS"
      moreLabel={`All ${group.members.length} →`}
      onMore={onMore}
      isEmpty={group.members.length === 0}
      emptyText={group.is_virtual
        ? 'No swimmers in favorites yet — tap the hearts on results.'
        : 'The roster is empty for now.'}
    >
      <ul className="m-0 flex list-none flex-col gap-1.5 p-0">
        {group.members.slice(0, DIGEST_MEMBERS).map((m) => (
          <li
            key={m.swimmer_id}
            className="flex items-center justify-between gap-3 px-3 py-2"
            style={{ background: 'var(--deep-card-bg-row)', borderRadius: 'var(--deep-radius-row)' }}
          >
            <a href={routes.swimmer(m.swimmer_id)} className="min-w-0 truncate text-[12.5px] font-extrabold no-underline" style={{ color: 'var(--deep-text)' }}>
              {m.name || m.name_en}
            </a>
            {ROLE_LABEL[m.role] && (
              <span
                className="hp-mono shrink-0 rounded-[6px] px-1.5 py-[2px] text-[9.5px] font-extrabold uppercase"
                style={{ background: 'var(--deep-accent-chip)', color: 'var(--deep-accent)' }}
              >
                {ROLE_LABEL[m.role]}
              </span>
            )}
          </li>
        ))}
      </ul>
    </DeepDigestCard>
  );
}

export {
  GroupMembersCard, GroupStandingsCard, GroupRecordsCard, GroupRecentSwimsCard,
  GroupRecordsDigest, GroupLastStartCard, GroupMembersDigest,
};
