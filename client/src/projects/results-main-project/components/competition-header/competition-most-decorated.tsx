import React from 'react';
import type { OverviewMedalist } from './types';
import { routes } from '../../../../utils/routes';
import UI_ClubLogo from '../../../components/mix/club-logo/club-logo';
import { cardStyle, halfCardStyle, halfCardClass, GenderHeader } from './overview-shared';

// Модуль «Most decorated» (design_handoff вариант 4b): карточка делится на ♂/♀
// половины (круг-лого 38px + имя/клуб + пилюля медалей); фолбэк — одна половина.

/** Половина «Most decorated». */
function MedalistHalf({ medalist, gender }: { medalist: OverviewMedalist; gender?: 'male' | 'female' }) {
  const inner = (
    <>
      {gender && <GenderHeader gender={gender} />}
      <div className="mt-2 flex min-w-0 items-center gap-2.5">
        <UI_ClubLogo clubName={medalist.club} size={38} />
        <div className="flex min-w-0 flex-col" dir="auto">
          <span className="truncate text-[15px] font-extrabold" style={{ color: 'var(--theme-mode-text)' }}>{medalist.first_name} {medalist.last_name}</span>
          <span className="truncate text-[12.5px] font-semibold" style={{ color: 'var(--theme-mode-text-secondary)' }}>{medalist.club}</span>
        </div>
      </div>
      <span
        className="mt-2 self-start rounded-full px-2.5 py-[3px] text-[12px] font-extrabold"
        style={{ background: 'color-mix(in srgb, var(--theme-primary) 15%, transparent)', color: 'var(--theme-primary)' }}
      >
        🥇{medalist.gold} 🥈{medalist.silver} 🥉{medalist.bronze}
      </span>
    </>
  );
  return medalist.swimmer_id > 0 ? (
    <a href={routes.swimmer(medalist.swimmer_id)} className={halfCardClass} style={{ ...halfCardStyle, color: 'inherit' }}>{inner}</a>
  ) : <div className={halfCardClass} style={halfCardStyle}>{inner}</div>;
}

interface Props {
  male: OverviewMedalist | null;
  female: OverviewMedalist | null;
  /** Фолбэк, если нет разбивки по полу. */
  single: OverviewMedalist | null;
}

export default function CompetitionMostDecorated({ male, female, single }: Props) {
  if (!male && !female && !single) return null;
  return (
    <div data-module="most-decorated" className="flex h-full flex-col rounded-[12px] p-4" style={cardStyle}>
      <div className="mb-2 flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
        <span className="text-[14px] font-extrabold">Most decorated</span>
        {(male || female) && (
          <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>by medals · ♂ / ♀</span>
        )}
      </div>
      <div className="flex flex-wrap gap-3">
        {male && <MedalistHalf medalist={male} gender="male" />}
        {female && <MedalistHalf medalist={female} gender="female" />}
        {single && <MedalistHalf medalist={single} />}
      </div>
    </div>
  );
}
