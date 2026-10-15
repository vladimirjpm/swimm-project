import React from 'react';
import './home.css';
import '../components/deep/deep-theme.css';
import HomeHeader from './components/home-header';
import DestinationCards from './components/destination-cards';
import RecordTicker from './components/record-ticker';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { HOME_REGION_LABEL } from '../../utils/constants/home-region';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import { routes } from '../../utils/routes';

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

      {/* Ссылки на политику и условия прямо на главной — требование проверки бренда Google
          (OAuth Branding): ссылка на политику конфиденциальности должна быть видна на главной без входа. */}
      <nav aria-label="Legal" className="relative mt-10 flex flex-wrap gap-x-5 gap-y-2 px-5 text-[12.5px] font-semibold text-[var(--t-text-3)] lg:px-16">
        <a href={routes.privacy()} className="text-[var(--t-text-3)] no-underline hover:text-[var(--t-accent)]">Privacy policy</a>
        <a href={routes.terms()} className="text-[var(--t-text-3)] no-underline hover:text-[var(--t-accent)]">Terms of use</a>
        <a href={routes.about()} className="text-[var(--t-text-3)] no-underline hover:text-[var(--t-accent)]">About</a>
      </nav>

      <RecordTicker />
      {/* Переключатель тем. На витрине он единственный способ сменить режим: топбар его
          не носит, а внутренние экраны (results, клуб, пловец) — уже другая дверь.
          Отступ снизу считает `home.css` от высоты ленты рекордов. */}
      <UI_ModeToggle />
    </div>
  );
}

export default Home;
