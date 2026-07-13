import React from 'react';
import './home.css';
import HomeHeader from './components/home-header';
import DestinationCards from './components/destination-cards';
import RecordTicker from './components/record-ticker';
import { HOME_REGION_LABEL } from '../../utils/constants/home-region';

function Home() {
  return (
    <div className="home-page relative min-h-screen overflow-x-clip pb-[96px] text-[#f3f8fd]">
      <div className="hp-shimmer" aria-hidden="true" />

      <HomeHeader active="home" />

      <section className="relative px-5 pt-[26px] lg:px-16 lg:pt-[46px]">
        <p className="mb-[18px] text-[11px] font-extrabold uppercase tracking-[0.28em] text-[#7dd3fc] lg:text-[15px] lg:tracking-[0.3em]">
          {`${new Date().getFullYear()} Season · ${HOME_REGION_LABEL}`}
        </p>
        <h1 className="max-w-[13ch] text-[52px] font-black leading-[0.92] tracking-[-0.045em] text-[#f3f8fd] lg:text-[clamp(80px,10.3vw,148px)] lg:leading-[0.88]">
          Every hundredth{' '}
          <em className="bg-[linear-gradient(92deg,#7dd3fc,#e0f2fe_70%)] bg-clip-text italic text-transparent">
            counts.
          </em>
        </h1>
        <p className="mt-[26px] max-w-[560px] text-[14.5px] leading-[1.55] text-[#e2f0fc]/[0.82] lg:text-[18px] lg:leading-[1.6]">
          <span className="lg:hidden">Results, records and normatives — live from the pool.</span>
          <span className="hidden lg:inline">
            Results, records and normatives of the swimming season — live from the pool. Pick a
            destination below and dive in.
          </span>
        </p>
      </section>

      <DestinationCards />

      <RecordTicker />
    </div>
  );
}

export default Home;
