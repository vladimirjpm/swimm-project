import React from 'react';
import type { ClubKpi, ClubProfile } from '../../../hooks/useClubOverview';
import UI_ClubLogo from '../../components/mix/club-logo/club-logo';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';

/**
 * Hero страницы клуба: логотип (или инициалы — это штатный вид, а не пустое состояние),
 * имя на иврите крупно + латиницей мелко, бейджи и KPI-ряд.
 *
 * Фото клуба из макета не рисуем — данных для него нет (решение Влада 2026-08-01,
 * docs/plans/club-page-model.md §6).
 */

interface Props {
  club: ClubProfile;
  kpi: ClubKpi;
}

// Подписи скоупа у плиток свои (см. ниже), поэтому общий scopeLabel страницы шапке
// больше не нужен: у неё нет ни одной цифры, которая слушала бы карусель сезонов.
function ClubHero({ club, kpi }: Props) {
  return (
    <section
      className="mb-4 rounded-2xl border p-6"
      style={{
        background: 'var(--deep-hero-grad)',
        borderColor: 'var(--deep-card-border)',
      }}
    >
      <div className="flex items-start gap-5">
        <UI_ClubLogo clubName={club.name} size={96} />

        <div className="min-w-0 flex-1">
          <h1
            className="truncate text-[40px] leading-tight"
            style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
          >
            {club.name}
          </h1>
          {club.name_en && (
            <div className="text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
              {club.name_en}
            </div>
          )}

          <div className="mt-3 flex flex-wrap items-center gap-2">
            {club.country_code && (
              <Badge>
                <UI_FlagEmoji countryCode={club.country_code} /> {club.country_name ?? club.country_code}
              </Badge>
            )}
            {club.official_group_slug && (
              <a href={`/groups/${club.official_group_slug}`} className="no-underline">
                <Badge accent>Official group</Badge>
              </a>
            )}
            {/* Бейджа «N swimmers» тут больше нет: пловцы стали плиткой KPI, а две
                одинаковые цифры в одной шапке читаются как ошибка. */}
            {club.first_season != null && <Badge>since {club.first_season}</Badge>}
          </div>
        </div>
      </div>

      {/* Плитки (решение Влада 2026-08-09). Скоуп у них РАЗНЫЙ и потому подписан у каждой:
          чемпионаты и победы — за всю историю клуба (карусель сезонов на них не влияет),
          рекорды — действующие, season bests — за витринный сезон, который режется
          последним зимним чемпионатом (docs/season-boundary-rule.md). */}
      <div className="mt-6 flex flex-wrap gap-8">
        <Kpi label="Championships" value={kpi.championships} hint="all time" />
        <Kpi
          label="Championship wins"
          value={kpi.championship_wins}
          hint="all time"
          gold={kpi.championship_wins > 0}
        />
        <Kpi label="Records" value={kpi.records} hint="in force" />
        <Kpi
          label="Season bests"
          value={kpi.season_bests}
          hint={kpi.showcase_season_from ? `since ${kpi.showcase_season_from}` : 'this season'}
        />
        <Kpi label="Swimmers" value={club.swimmer_count} hint="current roster" />
      </div>
    </section>
  );
}

function Badge({ children, accent }: { children: React.ReactNode; accent?: boolean }) {
  return (
    <span
      className="rounded-full border px-3 py-1 text-[11.5px] font-extrabold"
      style={{
        background: accent ? 'var(--deep-accent-chip)' : 'var(--deep-card-bg-raised)',
        borderColor: accent ? 'var(--deep-accent-border)' : 'var(--deep-card-border)',
        color: accent ? 'var(--deep-accent)' : 'var(--deep-text-mute)',
      }}
    >
      {children}
    </span>
  );
}

function Kpi({
  label,
  value,
  hint,
  gold,
}: {
  label: string;
  value: number | string;
  hint: string;
  gold?: boolean;
}) {
  return (
    <div>
      <div
        className="text-[34px] leading-none"
        style={{
          fontFamily: 'var(--deep-font-display)',
          color: gold ? 'var(--deep-gold)' : 'var(--deep-text)',
        }}
      >
        {value}
      </div>
      <div className="mt-1 text-[11px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--deep-text-mute)' }}>
        {label}
      </div>
      <div className="text-[10.5px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
        {hint}
      </div>
    </div>
  );
}

export default ClubHero;
