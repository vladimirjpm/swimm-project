import React from 'react';
import { routes } from '../../../utils/routes';

const CARD_BASE =
  'flex flex-col justify-between rounded-[18px] p-[18px] shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] lg:min-h-[190px] lg:rounded-[24px] lg:p-[26px]';

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
        className={`hp-card-live ${CARD_BASE} min-h-[130px] border border-[#7dd3fc]/35 text-inherit no-underline transition-[transform,border-color,box-shadow] duration-[180ms] ease-out hover:-translate-y-2 hover:border-[#7dd3fc]/80 hover:shadow-[0_28px_60px_rgba(2,10,24,0.65)] focus-visible:-translate-y-2 focus-visible:border-[#7dd3fc]/80 focus-visible:shadow-[0_28px_60px_rgba(2,10,24,0.65)] focus-visible:outline focus-visible:outline-[3px] focus-visible:outline-offset-[3px] focus-visible:outline-[#7dd3fc]`}
      >
        <div className="flex items-start justify-between gap-3">
          <span className={`${TITLE_BASE} text-[#f3f8fd]`}>Competitions</span>
          <span className="hp-mono flex items-center gap-[6px] pt-[6px] text-[11px] font-extrabold text-[#38ef8f]">
            <span className="hp-live-dot h-[7px] w-[7px] rounded-full bg-[#38ef8f]" />
            LIVE
          </span>
        </div>
        <div>
          <p className="text-[13px] leading-snug text-[#cbe0f0]/75">
            Dolphin &amp; All Masters, Youth 8–11, Junior 11–15
          </p>
          <p className="mt-3 text-[14px] font-extrabold text-[#7dd3fc]">4 events →</p>
        </div>
      </a>

      {SOON_CARDS.map((card) => (
        <div
          key={card.title}
          className={`hp-card-soon ${CARD_BASE} min-h-[110px] border border-dashed border-[#94a3b8]/35`}
        >
          <div className="flex items-start justify-between gap-3">
            <span className={`${TITLE_BASE} text-[#e2f0fc]/70`}>{card.title}</span>
            <span className="hp-mono mt-[3px] rounded-[7px] border border-[#94a3b8]/40 px-2 py-[3px] text-[11px] font-extrabold text-[#94a3b8]">
              SOON
            </span>
          </div>
          <div>
            <p className="text-[13px] leading-snug text-[#cbe0f0]/75">{card.body}</p>
            <p className="mt-3 text-[14px] font-extrabold text-[#7dd3fc]/50">Coming 2026</p>
          </div>
        </div>
      ))}
    </section>
  );
}

export default DestinationCards;
