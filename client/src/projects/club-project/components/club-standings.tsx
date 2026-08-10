import React from 'react';
import type { ClubStandingsTable } from '../../../hooks/useClubOverview';
import { routes } from '../../../utils/routes';

/**
 * Standings — детализация выбранного зачёта (клик по линии в гриде «сезон × группа», или
 * самый свежий зачёт в скоупе, если ничего не выбрано). Данные приходят от useClubOverview
 * УЖЕ окном нужных строк (лидеры + наш ± 1) — переповторный клиентский фильтр не нужен
 * (CARDS.md §4, club-page-cards-sonnet.md §4.1).
 *
 * ⚠ Футер «N competitions left» из макета сознательно не рисуем — под него нет данных
 * (см. таск-документ, §4.1).
 */

interface Props {
  standings: ClubStandingsTable | null;
}

const KIND_ICON: Record<string, string> = { winter: '❄', summer: '☀', openwater: '🌊' };

/** «2025/26» — тот же формат, что SeasonMath.Label на сервере. */
function seasonLabel(season: number): string {
  return `${season}/${String((season + 1) % 100).padStart(2, '0')}`;
}

function ClubStandings({ standings }: Props) {
  if (!standings) {
    return (
      <section className="deep-card h-full">
        <div className="deep-card-title">Standings</div>
        <div className="mt-3 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          No standings yet
        </div>
      </section>
    );
  }

  const icon = standings.kind ? KIND_ICON[standings.kind] ?? '' : '';

  return (
    <section className="deep-card h-full">
      <div className="deep-card-title">Standings</div>
      {/* Название соревнования обязательно: карточка показывает зачёт ОДНОГО старта
          (в гриде слева он подсвечен), а без имени и даты видно только «Kids · 2025/26»,
          и какой именно чемпионат раскрыт — непонятно. Имя ведёт на само соревнование. */}
      <a
        href={routes.competition(standings.competition_id)}
        dir="auto"
        className="mt-1 block truncate text-[13px] font-extrabold no-underline hover:underline"
        style={{ color: 'var(--deep-accent)' }}
        title={standings.competition_name}
      >
        {standings.competition_name}
      </a>
      <div className="deep-card-sub mt-0.5">
        {icon && `${icon} · `}
        {standings.group_name ?? 'All groups'} · {seasonLabel(standings.season)} · {standings.date} ·{' '}
        {standings.club_count} clubs
      </div>

      <div className="mt-4 flex flex-col gap-2">
        {standings.rows.length === 0 ? (
          <div className="text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
            No clubs in this standing
          </div>
        ) : (
          standings.rows.map((row) => (
            <div
              key={row.club_id}
              className={`deep-stand-row ${row.is_us ? 'deep-stand-row--us' : ''}`}
            >
              <span
                className="w-8 shrink-0 text-center text-[18px] leading-none"
                style={{
                  fontFamily: 'var(--deep-font-display)',
                  color: row.rank === 1 ? 'var(--deep-gold)' : 'var(--deep-text)',
                }}
              >
                #{row.rank}
              </span>

              <div className="min-w-0 flex-1">
                <div
                  className="truncate text-[13px] font-extrabold"
                  style={{ color: 'var(--deep-text)' }}
                >
                  {row.club_name}
                </div>
                {/* Мобайл (390): подстрока скрывается — CARDS.md «Мобайл». */}
                <div
                  className="hidden truncate text-[11px] font-bold sm:block"
                  style={{ color: 'var(--deep-text-mute)' }}
                >
                  {row.swimmer_count} swimmers · {row.scoring_swims} scoring swims
                </div>
              </div>

              <div className="shrink-0 text-right">
                <div
                  className="text-[14px] font-extrabold tabular-nums"
                  style={{ color: 'var(--deep-text)' }}
                >
                  {row.points} pts
                </div>
                {/* Мобайл (390): медали скрываются — CARDS.md «Мобайл». */}
                <div
                  className="hidden text-[11px] font-bold sm:block"
                  style={{ color: 'var(--deep-text-mute)' }}
                >
                  🥇{row.gold} 🥈{row.silver} 🥉{row.bronze}
                </div>
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}

export default ClubStandings;
