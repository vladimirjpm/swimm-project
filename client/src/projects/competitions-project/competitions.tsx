import React from 'react';
import '../home-project/home.css';
import HomeHeader from '../home-project/components/home-header';
import RecordTicker from '../home-project/components/record-ticker';

type CompetitionLink = {
  href: string;
  title: string;
  subtitle: string;
  badge: string;
  live?: boolean;
};

const PAGES: CompetitionLink[] = [
  {
    href: './results_main.html?competitionId=last',
    title: 'Latest meet',
    subtitle: 'Свежие результаты последнего соревнования',
    badge: '● LIVE',
    live: true,
  },
  {
    href: './results_main.html?category=young8_11',
    title: 'Young 8–11',
    subtitle: 'Youth competitions, ages 8–11',
    badge: '8-11',
  },
  {
    href: './results_main.html?category=junior',
    title: 'Junior',
    subtitle: 'Junior competitions, ages 11–15',
    badge: '11-15',
  },
  {
    href: './results_main.html?category=masters',
    title: 'Masters',
    subtitle: 'Masters meets incl. Dolphin, ages 21+',
    badge: 'М',
  },
  {
    href: './results_main.html?category=all',
    title: 'All results',
    subtitle: 'Every competition in one list',
    badge: '∀',
  },
];

function Competitions() {
  return (
    <div className="home-page relative min-h-screen overflow-x-clip pb-[96px] text-[#f3f8fd]">
      <div className="hp-shimmer" aria-hidden="true" />

      <HomeHeader active="competitions" />

      <section className="relative px-5 pt-[26px] lg:px-16 lg:pt-[46px]">
        <p className="mb-[18px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[#7dd3fc] lg:text-[15px] lg:tracking-[0.3em]">
          2026 Season · Israel
        </p>
        <h1 className="text-[44px] font-black leading-[0.92] tracking-[-0.045em] text-[#f3f8fd] lg:text-[88px] lg:leading-[0.9]">
          Competitions
        </h1>
        <p className="mt-5 max-w-[560px] text-[14.5px] leading-[1.55] text-[#e2f0fc]/[0.82] lg:text-[18px] lg:leading-[1.6]">
          Pick a meet — results are live from the pool.
        </p>
      </section>

      <section
        className="grid grid-cols-1 gap-3 px-4 pt-[26px] sm:grid-cols-2 lg:grid-cols-4 lg:gap-[18px] lg:px-16 lg:pt-12"
        aria-label="Competition pages"
      >
        {PAGES.map((page) => (
          <a
            key={page.href}
            href={page.href}
            className="hp-card-std flex min-h-[130px] flex-col justify-between rounded-[18px] border border-[#7dd3fc]/[0.22] p-[18px] text-inherit no-underline shadow-[0_24px_60px_rgba(2,10,24,0.5)] backdrop-blur-[14px] transition-[transform,border-color,box-shadow] duration-[180ms] ease-out hover:-translate-y-2 hover:border-[#7dd3fc]/80 hover:shadow-[0_28px_60px_rgba(2,10,24,0.65)] focus-visible:-translate-y-2 focus-visible:border-[#7dd3fc]/80 focus-visible:shadow-[0_28px_60px_rgba(2,10,24,0.65)] focus-visible:outline focus-visible:outline-[3px] focus-visible:outline-offset-[3px] focus-visible:outline-[#7dd3fc] lg:min-h-[190px] lg:rounded-[24px] lg:p-[26px]"
          >
            <div className="flex items-start justify-between gap-3">
              <span className="text-[21px] font-black tracking-[-0.02em] lg:text-[26px]">
                {page.title}
              </span>
              <span
                className={`hp-mono mt-[3px] shrink-0 rounded-[7px] border px-2 py-[3px] text-[11px] font-extrabold ${
                  page.live
                    ? 'border-[#38ef8f]/40 text-[#38ef8f]'
                    : 'border-[#7dd3fc]/40 text-[#7dd3fc]'
                }`}
              >
                {page.badge}
              </span>
            </div>
            <div>
              <p className="text-[13px] leading-snug text-[#cbe0f0]/75">{page.subtitle}</p>
              <p className="mt-3 text-[14px] font-extrabold text-[#7dd3fc]">Open results →</p>
            </div>
          </a>
        ))}
      </section>

      <RecordTicker />
    </div>
  );
}

export default Competitions;
