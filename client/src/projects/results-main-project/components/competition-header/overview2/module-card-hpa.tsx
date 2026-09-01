import React from 'react';
import type { CompetitionOverview, OverviewHighPoint } from '../types';
import UI_GenderAgeTable, { GenderAgeEntry, GenderAgeRow } from '../../../../components/mix/gender-age-table/gender-age-table';

// Карточка модуля High Point Award (9d): та же таблица ♂ | AGE | ♀, что в старом
// Overview (competition-high-point-award.tsx) — UI_GenderAgeTable с иконками клубов
// (внутри UI_SwimmerNameCell). Обёртка — ov2-card с заголовком цвета модуля.
//
// ПРАВИЛА КАРТОЧКИ — docs/competition-overview-cards.md (раздел High Point). Кратко:
//   очки считает сервер по правилу соревнования (шкала мест или сумма FINA); rule_version
//   = null означает legacy-расчёт по сумме международных очков; при ничьей награду
//   получают ВСЕ (is_tie); эстафеты в личный зачёт НЕ идут (в отличие от Most decorated).

interface Props {
  overview: CompetitionOverview;
  onOpenClub?(club: string): void;
}

export default function ModuleCardHpa({ overview }: Props) {
  const awards = overview.high_point_awards;
  if (awards.length === 0) return null;

  const bucketOf = (a: OverviewHighPoint) => a.age_group || String(a.age);
  const low = (b: string) => parseInt(b, 10) || 0;
  const buckets = [...new Set(awards.map(bucketOf))].sort((x, y) => low(x) - low(y));
  // На (возраст × пол) наград может быть НЕСКОЛЬКО — при ничьей по очкам сервер отдаёт всех
  // (is_tie=true у каждого). Копим списком: на шкале «5/3/2/1 за место» ничьи обычны, и
  // прежняя запись в одно поле оставляла в ячейке случайного (последнего) из них.
  const byBucket = new Map<string, { male: OverviewHighPoint[]; female: OverviewHighPoint[] }>();
  for (const a of awards) {
    const e = byBucket.get(bucketOf(a)) ?? { male: [], female: [] };
    if (a.gender === 'male') e.male.push(a); else if (a.gender === 'female') e.female.push(a);
    byBucket.set(bucketOf(a), e);
  }
  const toEntry = (a: OverviewHighPoint): GenderAgeEntry => ({
    firstName: a.first_name, lastName: a.last_name, club: a.club, clubId: a.club_id,
    value: String(a.points), swimmerId: a.swimmer_id,
  });
  const rows: GenderAgeRow[] = buckets.map((b) => {
    const e = byBucket.get(b)!;
    return { age: b, male: e.male.map(toEntry), female: e.female.map(toEntry) };
  });

  return (
    <section className="ov2-card flex flex-col gap-2" data-module="hpa">
      <div className="flex flex-wrap items-baseline gap-2.5">
        <span className="ov2-card__title">🏆 High Point Award</span>
        <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
          best by points in each age · ♂ / ♀
        </span>
      </div>
      <UI_GenderAgeTable rows={rows} showClubIcon hideClubIconMobile ageColWidthMobile={32} />
      {awards.some((a) => a.finals_only_unavailable) && (
        <span className="text-[11px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          * regulation counts finals only; computed over all swims (heat type not in data yet)
        </span>
      )}
    </section>
  );
}
