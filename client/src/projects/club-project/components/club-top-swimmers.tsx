import React from 'react';
import type { ClubTopSwimmer } from '../../../hooks/useClubOverview';
import { routes } from '../../../utils/routes';
import ClubAvatar from './club-avatar';

/**
 * Top swimmers — топ-5 по очкам в выбранном скоупе, уже отсортирован сервером
 * (CARDS.md §9, club-page-cards-sonnet.md §4.3). На мобайле (390) — только топ-4
 * (CARDS.md «Мобайл»): пятая строка скрывается через `hidden sm:flex`, без второго запроса.
 */

interface Props {
  swimmers: ClubTopSwimmer[];
}

function ClubTopSwimmers({ swimmers }: Props) {
  return (
    <section className="deep-card mb-4">
      <div className="deep-card-title">Top swimmers</div>

      {swimmers.length === 0 ? (
        <div className="mt-3 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          No swimmers with points yet
        </div>
      ) : (
        <div className="mt-4 flex flex-col gap-2">
          {swimmers.map((s, i) => {
            const rank = i + 1;
            const name = `${s.first_name} ${s.last_name}`.trim();
            const medals = s.gold + s.silver + s.bronze;
            return (
              <a
                key={s.swimmer_id}
                href={routes.swimmer(s.swimmer_id)}
                className={`flex items-center gap-3 rounded-[var(--deep-radius-row)] px-2 py-2 no-underline ${
                  i === 4 ? 'hidden sm:flex' : 'flex'
                }`}
                style={{ background: 'var(--deep-card-bg-row)' }}
              >
                <span
                  className="w-6 shrink-0 text-center text-[15px] leading-none"
                  style={{
                    fontFamily: 'var(--deep-font-display)',
                    color: rank === 1 ? 'var(--deep-gold)' : 'var(--deep-text-mute)',
                  }}
                >
                  #{rank}
                </span>

                <ClubAvatar firstName={s.first_name} lastName={s.last_name} gender={s.gender} />

                <div className="min-w-0 flex-1">
                  <div className="truncate text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                    {name || '—'}
                  </div>
                  <div className="truncate text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                    {s.age != null && `age ${s.age}`}
                    {s.age != null && medals > 0 && ' · '}
                    {medals > 0 && `🥇${s.gold} 🥈${s.silver} 🥉${s.bronze}`}
                  </div>
                </div>

                <div
                  className="shrink-0 text-[14px] font-extrabold tabular-nums"
                  style={{ color: 'var(--deep-text)' }}
                >
                  {s.points}
                </div>
              </a>
            );
          })}
        </div>
      )}
    </section>
  );
}

export default ClubTopSwimmers;
