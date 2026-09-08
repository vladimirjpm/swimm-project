import React from 'react';
import './home.css';
import '../components/deep/deep-theme.css';
import HomeHeader from './components/home-header';
import DestinationCards from './components/destination-cards';
import RecordTicker from './components/record-ticker';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { HOME_REGION_LABEL } from '../../utils/constants/home-region';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';

function Home() {
  const deep = useDeepThemeClass();

  return (
    <div className={`home-page ${deep} relative min-h-screen overflow-x-clip pb-[96px] text-[var(--t-text)]`}>
      <div className="hp-shimmer" aria-hidden="true" />

      <HomeHeader active="home" />

      <section className="relative px-5 pt-[26px] lg:px-16 lg:pt-[46px]">
        <p className="mb-[18px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[var(--t-accent)] lg:text-[15px] lg:tracking-[0.3em]">
          {`${new Date().getFullYear()} Season · ${HOME_REGION_LABEL}`}
        </p>
        <h1 className="max-w-[13ch] text-[52px] font-black leading-[0.92] tracking-[-0.045em] text-[var(--t-text)] lg:text-[clamp(80px,10.3vw,148px)] lg:leading-[0.88]">
          Every hundredth{' '}
          <em className="bg-[linear-gradient(92deg,var(--t-accent),var(--t-text)_70%)] bg-clip-text italic text-transparent">
            counts.
          </em>
        </h1>
        <p className="mt-[26px] max-w-[560px] text-[14.5px] leading-[1.55] text-[var(--t-text-2)] lg:text-[18px] lg:leading-[1.6]">
          <span className="lg:hidden">Results, records and normatives — live from the pool.</span>
          <span className="hidden lg:inline">
            Results, records and normatives of the swimming season — live from the pool. Pick a
            destination below and dive in.
          </span>
        </p>
      </section>

      <DestinationCards />

      <RecordTicker />
      {/* Переключатель тем. На витрине он единственный способ сменить режим: топбар его
          не носит, а внутренние экраны (results, клуб, пловец) — уже другая дверь.
          Отступ снизу считает `home.css` от высоты ленты рекордов. */}
      <UI_ModeToggle />
    </div>
  );
}

export default Home;
