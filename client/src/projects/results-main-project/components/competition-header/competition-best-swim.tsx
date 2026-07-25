import React from 'react';
import type { OverviewBestSwim } from './types';
import { routes } from '../../../../utils/routes';
import { cardStyle, halfCardStyle, halfCardClass, GenderHeader } from './overview-shared';

// Модуль «Best swim of the competition» (design_handoff вариант 4a): карточка
// делится на ♂/♀ половины с медальоном очков 74px; фолбэк — одна половина без пола.

/** Половина «Best swim»: медальон очков 74px + имя/клуб/время/событие. */
function BestSwimHalf({ swim, gender }: { swim: OverviewBestSwim; gender?: 'male' | 'female' }) {
  const inner = (
    <>
      {gender && <GenderHeader gender={gender} />}
      <div className="mt-[9px] flex min-w-0 items-center gap-3">
        <div
          className="flex flex-none flex-col items-center justify-center rounded-full"
          style={{
            width: 74, height: 74,
            background: 'color-mix(in srgb, var(--theme-primary) 12%, transparent)',
            border: '2px solid color-mix(in srgb, var(--theme-primary) 35%, transparent)',
          }}
        >
          <span className="text-[24px] font-black leading-none" style={{ color: 'var(--theme-primary)' }}>{swim.international_points}</span>
          <span className="text-[9px] font-extrabold uppercase tracking-[0.06em]" style={{ color: 'var(--theme-primary)' }}>points</span>
        </div>
        <div className="flex min-w-0 flex-col gap-[3px]" dir="auto">
          <span className="truncate text-[15px] font-extrabold" style={{ color: 'var(--theme-mode-text)' }}>
            {swim.is_relay && swim.relay_team_name ? swim.relay_team_name : `${swim.first_name} ${swim.last_name}`}
          </span>
          <span className="truncate text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-secondary)' }}>{swim.club}</span>
          <span className="text-[15px] font-extrabold tabular-nums" style={{ color: 'var(--theme-mode-text)' }}>{swim.time}</span>
          <span className="text-[11.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
            {swim.distance}m {swim.style_name}{swim.day_number != null ? ` · Day ${swim.day_number}` : ''}
          </span>
        </div>
      </div>
    </>
  );
  return !swim.is_relay && swim.swimmer_id > 0 ? (
    <a href={routes.swimmer(swim.swimmer_id)} className={halfCardClass} style={{ ...halfCardStyle, color: 'inherit' }}>{inner}</a>
  ) : <div className={halfCardClass} style={halfCardStyle}>{inner}</div>;
}

interface Props {
  male: OverviewBestSwim | null;
  female: OverviewBestSwim | null;
  /** Фолбэк, если нет разбивки по полу. */
  single: OverviewBestSwim | null;
}

export default function CompetitionBestSwim({ male, female, single }: Props) {
  if (!male && !female && !single) return null;
  return (
    <div data-module="best-swim" className="flex h-full flex-col rounded-[12px] p-4" style={cardStyle}>
      <div className="mb-2 flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
        <span className="text-[14px] font-extrabold">Best swim of the competition</span>
        {(male || female) && (
          <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>by points · ♂ / ♀</span>
        )}
      </div>
      <div className="flex flex-wrap gap-3">
        {male && <BestSwimHalf swim={male} gender="male" />}
        {female && <BestSwimHalf swim={female} gender="female" />}
        {single && <BestSwimHalf swim={single} />}
      </div>
    </div>
  );
}
