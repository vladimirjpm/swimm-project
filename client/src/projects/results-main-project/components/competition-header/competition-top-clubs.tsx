import React from 'react';
import type { OverviewClub } from './types';
import { cardStyle, SectionTitle, ClubsTable } from './overview-shared';

// Модули клубного зачёта: общий Top clubs (топ-3 + ссылка на таб Clubs) и
// разбивка Top clubs · Men/Women (только рейтинг, без медалей).

export function CompetitionTopClubs({ clubs, onOpenTab, onOpenClub }: {
  clubs: OverviewClub[]; onOpenTab(tab: 'clubs'): void; onOpenClub?(club: string): void;
}) {
  if (clubs.length === 0) return null;
  return (
    <div data-module="top-clubs" className="rounded-[12px] p-4" style={cardStyle}>
      <SectionTitle>Top clubs</SectionTitle>
      <ClubsTable clubs={clubs.slice(0, 3)} onOpenClub={onOpenClub} />
      <button type="button" onClick={() => onOpenTab('clubs')}
        className="mt-2 bg-transparent p-0 text-[12px] font-bold hover:underline"
        style={{ color: 'var(--theme-primary)' }}>
        Clubs tab →
      </button>
    </div>
  );
}

export function CompetitionTopClubsByGender({ men, women }: { men: OverviewClub[]; women: OverviewClub[] }) {
  if (men.length === 0 && women.length === 0) return null;
  return (
    <div data-module="top-clubs-by-gender" className="grid grid-cols-2 gap-3">
      {men.length > 0 && (
        <div className="flex h-full flex-col rounded-[12px] p-4" style={cardStyle}>
          <SectionTitle>Top clubs · Men ♂</SectionTitle>
          <ClubsTable clubs={men} showMedals={false} />
        </div>
      )}
      {women.length > 0 && (
        <div className="flex h-full flex-col rounded-[12px] p-4" style={cardStyle}>
          <SectionTitle>Top clubs · Women ♀</SectionTitle>
          <ClubsTable clubs={women} showMedals={false} />
        </div>
      )}
    </div>
  );
}
