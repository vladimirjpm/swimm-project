import React from 'react';
import { HOME_REGION_LABEL } from '../../../utils/constants/home-region';
import { seasonLabel, seasonStartYear } from '../../../utils/helpers/season-helper';

/*
 * Заголовок /competitions. Десктоп (≥ lg, хендофф competitions_home-3, 2b) — одна строка ≤ 48px:
 * «Competitions» 28px + надзаголовок сезона по базовой линии, чтобы мини-месяц и кубы начинались
 * сразу под ним. Телефон — надзаголовок над крупным h1 (как было, Б12).
 */
function CompetitionsHero() {
  return (
    <section className="relative flex flex-col-reverse gap-2 lg:flex-row lg:items-baseline lg:gap-[14px]">
      <h1 className="m-0 text-[44px] font-black leading-[0.92] tracking-[-0.045em] text-[var(--t-text)] lg:text-[28px] lg:leading-tight lg:tracking-[-0.02em]">
        Competitions
      </h1>
      <p className="m-0 text-[11px] font-extrabold uppercase tracking-[0.28em] text-[var(--t-accent-text)] lg:tracking-[0.2em]">
        {`Season ${seasonLabel(seasonStartYear())} · ${HOME_REGION_LABEL}`}
      </p>
    </section>
  );
}

export default CompetitionsHero;
