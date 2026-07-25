import React from 'react';
import type { OverviewHighPoint } from './types';
import UI_GenderAgeTable, { GenderAgeEntry, GenderAgeRow } from '../../../components/mix/gender-age-table/gender-age-table';
import { cardStyle } from './overview-shared';

// Модуль «High Point Award» (ISR-style): единая таблица UI_GenderAgeTable
// (♂ | AGE | ♀), значение = ОЧКИ. Для masters корзина — возрастная группа.

export default function CompetitionHighPointAward({ awards }: { awards: OverviewHighPoint[] }) {
  if (awards.length === 0) return null;
  const bucketOf = (a: OverviewHighPoint) => a.age_group || String(a.age);
  const low = (b: string) => parseInt(b, 10) || 0;
  const buckets = [...new Set(awards.map(bucketOf))].sort((x, y) => low(x) - low(y));
  const byBucket = new Map<string, { male?: OverviewHighPoint; female?: OverviewHighPoint }>();
  for (const a of awards) {
    const e = byBucket.get(bucketOf(a)) ?? {};
    if (a.gender === 'male') e.male = a; else if (a.gender === 'female') e.female = a;
    byBucket.set(bucketOf(a), e);
  }
  const toEntry = (a: OverviewHighPoint): GenderAgeEntry => ({
    firstName: a.first_name, lastName: a.last_name, club: a.club,
    value: String(a.points), swimmerId: a.swimmer_id,
  });
  const rows: GenderAgeRow[] = buckets.map((b) => {
    const e = byBucket.get(b)!;
    return { age: b, male: e.male && toEntry(e.male), female: e.female && toEntry(e.female) };
  });
  return (
    <div data-module="high-point-award" className="w-full rounded-[12px] p-4" style={cardStyle}>
      <div className="mb-2 flex flex-wrap items-baseline gap-x-2.5">
        <span className="text-[14px] font-extrabold">🏆 High Point Award</span>
        <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>ISR-style · очки</span>
      </div>
      <UI_GenderAgeTable rows={rows} showClubIcon />
    </div>
  );
}
