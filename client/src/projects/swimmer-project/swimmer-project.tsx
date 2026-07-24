import React, { useMemo } from 'react';
import '../../index.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import { useAthleteCareer } from '../../hooks/useAthleteCareer';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_FlagEmoji from '../components/mix/flag-icon/flag-icon';
import UI_MedalIcon from '../components/mix/medal-icon/medal-icon';
import { useSwimmerProfile, SwimmerProfile } from './use-swimmer-profile';

// Самостоятельная страница пловца (swimmer.html?swimmer=<id>). Профиль тянется по id
// (/api/swimmers/{id}), карьера all-time — по полному имени (useAthleteCareer, тот же
// контракт, что у попапа-карточки). Диплинкуема; шапка/тема — как на остальных страницах.

const cardStyle: React.CSSProperties = {
  background: 'var(--theme-mode-surface)',
  border: '1px solid var(--theme-mode-border)',
  boxShadow: 'var(--theme-mode-card-shadow)',
};

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <div
      className="mb-2 text-[11px] font-extrabold uppercase tracking-[0.14em]"
      style={{ color: 'var(--theme-mode-text-muted)' }}
    >
      {children}
    </div>
  );
}

function Hero({ profile }: { profile: SwimmerProfile }) {
  const initials = (profile.fullName.trim().charAt(0) || '?').toUpperCase();
  const genderLabel = profile.gender === 'M' ? 'Male' : profile.gender === 'F' ? 'Female' : null;
  return (
    <div
      className="flex flex-col items-center gap-4 rounded-[16px] p-5 sm:flex-row sm:items-center"
      style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}
    >
      {profile.avatarUrl ? (
        <img
          src={profile.avatarUrl}
          alt=""
          className="h-[76px] w-[76px] shrink-0 rounded-full object-cover"
          style={{ border: '2px solid var(--theme-mode-accent-text)' }}
        />
      ) : (
        <span
          className="flex h-[76px] w-[76px] shrink-0 items-center justify-center rounded-full text-[30px] font-black"
          style={{ background: 'var(--theme-mode-accent-text)', color: 'var(--theme-primary)' }}
        >
          {initials}
        </span>
      )}
      <div className="flex min-w-0 flex-col items-center gap-1.5 sm:items-start">
        <h1 dir="auto" className="text-center text-[24px] font-extrabold leading-tight sm:text-left">
          {profile.fullName}
        </h1>
        <div className="flex flex-wrap items-center justify-center gap-x-3 gap-y-1 text-[13px] font-semibold opacity-90 sm:justify-start">
          {profile.clubName && (
            <span className="inline-flex items-center gap-1.5" dir="auto">
              <UI_ClubIcon clubName={profile.clubName} iconWidth="18px" />
              {profile.clubName}
            </span>
          )}
          {profile.countryCode && (
            <span className="inline-flex items-center gap-1.5">
              <UI_FlagEmoji countryCode={profile.countryCode} size="20x15" />
              {profile.countryName || profile.countryCode}
            </span>
          )}
          {profile.birthYear > 0 && <span>b. {profile.birthYear}</span>}
          {genderLabel && <span>{genderLabel}</span>}
          {profile.origin === 'local' && (
            <span
              className="rounded-full px-2 py-0.5 text-[11px] font-bold"
              style={{ background: 'rgba(255,255,255,0.18)' }}
            >
              local
            </span>
          )}
        </div>
      </div>
    </div>
  );
}

function StatTile({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-[12px] p-3.5 text-center" style={cardStyle}>
      <div className="text-[22px] font-extrabold" style={{ color: 'var(--theme-primary)' }}>{value}</div>
      <div className="mt-0.5 text-[11px] font-semibold uppercase tracking-[0.1em]" style={{ color: 'var(--theme-mode-text-muted)' }}>
        {label}
      </div>
    </div>
  );
}

function SwimmerContent({ profile }: { profile: SwimmerProfile }) {
  const career = useAthleteCareer(profile.fullName);
  const hasCareer = career.races > 0;

  return (
    <div className="mt-4 flex flex-col gap-4">
      {/* Сводка */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatTile label="Competitions" value={career.competitions} />
        <StatTile label="Races" value={career.races} />
        <StatTile label="Since" value={career.since || '—'} />
        <StatTile label="Total points" value={career.totalPoints} />
      </div>

      {/* Медали */}
      {(career.gold + career.silver + career.bronze) > 0 && (
        <div className="rounded-[12px] p-4" style={cardStyle}>
          <SectionTitle>Medals</SectionTitle>
          <div className="flex items-center gap-4 text-[15px] font-extrabold">
            <span className="inline-flex items-center gap-1.5">
              <UI_MedalIcon place="1" styleType="icon-place" styleSize="medal-24" placeReplace={String(career.gold)} /> gold
            </span>
            <span className="inline-flex items-center gap-1.5">
              <UI_MedalIcon place="2" styleType="icon-place" styleSize="medal-24" placeReplace={String(career.silver)} /> silver
            </span>
            <span className="inline-flex items-center gap-1.5">
              <UI_MedalIcon place="3" styleType="icon-place" styleSize="medal-24" placeReplace={String(career.bronze)} /> bronze
            </span>
          </div>
        </div>
      )}

      {/* Лучшие времена по стилям */}
      <div className="rounded-[12px] p-4" style={cardStyle}>
        <SectionTitle>Best times by event</SectionTitle>
        {hasCareer && career.bestByStyle.length > 0 ? (
          <div className="flex flex-col">
            {career.bestByStyle.map((b, i) => (
              <div
                key={`${b.stroke}-${b.distance}-${i}`}
                className="flex items-center justify-between gap-3 border-t py-2 text-[13px] first:border-t-0"
                style={{ borderColor: 'var(--theme-mode-border)' }}
              >
                <span className="min-w-0 font-bold" dir="auto">
                  {b.distance}m {b.stroke}
                  <span className="ml-2 font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
                    {b.pool} · {b.competition}
                  </span>
                </span>
                <span className="flex shrink-0 items-center gap-2">
                  {b.points > 0 && (
                    <span className="text-[12px] font-bold" style={{ color: 'var(--theme-mode-text-muted)' }}>
                      {b.points} pts
                    </span>
                  )}
                  <span className="font-mono font-extrabold" style={{ color: 'var(--theme-primary)' }}>{b.time}</span>
                </span>
              </div>
            ))}
          </div>
        ) : (
          <div className="py-2 text-[13px] italic" style={{ color: 'var(--theme-mode-text-muted)' }}>
            No career results yet.
          </div>
        )}
      </div>
    </div>
  );
}

function SwimmerProject() {
  useTheme();
  useMode();

  const swimmerId = useMemo<number | null>(() => {
    const raw = new URLSearchParams(window.location.search).get('swimmer');
    const n = raw != null ? Number(raw) : NaN;
    return Number.isFinite(n) && n > 0 ? n : null;
  }, []);

  const state = useSwimmerProfile(swimmerId);

  return (
    <div className="min-h-screen bg-[var(--theme-mode-page-bg)] md:p-4 pt-safe pb-safe">
      <div className="sticky top-0 z-50 md:-m-4 md:mb-0">
        <AppTopbar />
      </div>
      <UI_ModeToggle />

      <div className="mx-auto mt-4 w-full max-w-[880px] px-2 md:px-0">
        {state.status === 'loading' && (
          <div className="flex min-h-[160px] items-center justify-center text-[14px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
            Loading swimmer…
          </div>
        )}
        {state.status === 'notfound' && (
          <div
            className="flex min-h-[160px] items-center justify-center rounded-[14px] px-4 text-center text-[14px] font-semibold"
            style={{ border: '1px dashed var(--theme-mode-border-input)', background: 'var(--theme-mode-surface)', color: 'var(--theme-mode-text-muted)' }}
          >
            {swimmerId == null ? 'No swimmer specified (?swimmer=<id>).' : 'Swimmer not found.'}
          </div>
        )}
        {state.status === 'error' && (
          <div
            className="flex min-h-[160px] items-center justify-center rounded-[14px] px-4 text-center text-[14px] font-semibold"
            style={{ border: '1px dashed var(--theme-mode-border-input)', background: 'var(--theme-mode-surface)', color: 'var(--theme-mode-text-muted)' }}
          >
            Failed to load swimmer. Please try again.
          </div>
        )}
        {state.status === 'ok' && (
          <>
            <Hero profile={state.profile} />
            <SwimmerContent profile={state.profile} />
          </>
        )}
      </div>
    </div>
  );
}

export default SwimmerProject;
