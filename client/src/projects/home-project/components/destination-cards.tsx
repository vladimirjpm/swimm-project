import React from 'react';
import { routes } from '../../../utils/routes';

const CARD_BASE =
  'flex flex-col justify-between rounded-[18px] p-[18px] shadow-[var(--t-shadow)] backdrop-blur-[14px] lg:min-h-[190px] lg:rounded-[24px] lg:p-[26px]';

const TITLE_BASE = 'text-[21px] font-black tracking-[-0.02em] lg:text-[26px]';

const SOON_CARDS = [
  {
    title: 'Normatives',
    body: 'Youth-3 to MSMK grids for every stroke & distance',
  },
  {
    title: 'Records',
    body: 'Age records, all-time bests, world & national marks',
  },
  {
    title: 'Countries',
    body: 'Compare national teams side by side',
  },
];

function DestinationCards() {
  return (
    <section
      className="grid grid-cols-1 gap-3 px-4 pt-[26px] sm:grid-cols-2 lg:grid-cols-4 lg:gap-[18px] lg:px-16 lg:pt-14"
      aria-label="Destinations"
    >
      <a
        href={routes.competitionsList()}
        className={`hp-card-live ${CARD_BASE} min-h-[130px] border border-[var(--t-accent-border)] text-inherit no-underline transition-[transform,border-color,box-shadow] duration-[180ms] ease-out hover:-translate-y-2 hover:border-[var(--t-accent)] hover:shadow-[var(--t-shadow)] focus-visible:-translate-y-2 focus-visible:border-[var(--t-accent)] focus-visible:shadow-[var(--t-shadow)] focus-visible:outline focus-visible:outline-[3px] focus-visible:outline-offset-[3px] focus-visible:outline-[var(--t-accent)]`}
      >
        <div className="flex items-start justify-between gap-3">
          <span className={`${TITLE_BASE} text-[var(--t-text)]`}>Competitions</span>
          <span className="hp-mono flex items-center gap-[6px] pt-[6px] text-[11px] font-extrabold text-[var(--t-live)]">
            <span className="hp-live-dot h-[7px] w-[7px] rounded-full bg-[var(--t-live)]" />
            LIVE
          </span>
        </div>
        <div>
          <p className="text-[13px] leading-snug text-[var(--t-text-2)]">
            Dolphin &amp; All Masters, Youth 8–11, Junior 11–15
          </p>
          <p className="mt-3 text-[14px] font-extrabold text-[var(--t-accent)]">4 events →</p>
        </div>
      </a>

      {SOON_CARDS.map((card) => (
        <div
          key={card.title}
          className={`hp-card-soon ${CARD_BASE} min-h-[110px] border border-dashed border-[var(--t-border)]`}
        >
          <div className="flex items-start justify-between gap-3">
            <span className={`${TITLE_BASE} text-[var(--t-text-2)]`}>{card.title}</span>
            <span className="hp-mono mt-[3px] rounded-[7px] border border-[var(--t-border)] px-2 py-[3px] text-[11px] font-extrabold text-[var(--t-text-2)]">
              SOON
            </span>
          </div>
          <div>
            <p className="text-[13px] leading-snug text-[var(--t-text-2)]">{card.body}</p>
            <p className="mt-3 text-[14px] font-extrabold text-[var(--t-accent-dim)]">Coming 2026</p>
          </div>
        </div>
      ))}
    </section>
  );
}

export default DestinationCards;
