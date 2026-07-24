import React, { useMemo, useState } from 'react';
import '../../index.css';
import { useTheme } from '../../hooks/useTheme';
import { useMode } from '../../hooks/useMode';
import { useAthleteCareer } from '../../hooks/useAthleteCareer';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_FlagEmoji from '../components/mix/flag-icon/flag-icon';
import UI_MedalIcon from '../components/mix/medal-icon/medal-icon';
import UI_SwimmerGallery from '../components/mix/swimmer-gallery/swimmer-gallery';
import { HelperMedia } from '../../utils/helpers';
import { GalleryItem } from '../../utils/interfaces/results';
import { useFavoritesContext } from '../../hooks/favorites-context';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useSwimmerProfile, SwimmerProfile } from './use-swimmer-profile';
import { useSwimmerMedia } from './use-swimmer-media';

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

// ❤ избранное + ⭐ «это я» (favorites, по swimmer_id). Гостю — ❤, открывающее логин.
function FavoriteControls({ swimmerId }: { swimmerId: number }) {
  const { isAuthenticated, primarySwimmerId, favoriteSwimmerIds, setMeBySwimmer, toggleFavoriteSwimmer } =
    useFavoritesContext();
  const { openLoginModal } = useLoginModal();

  const btnBase =
    'flex h-9 w-9 items-center justify-center rounded-full leading-none transition-transform hover:scale-110';

  if (!isAuthenticated) {
    return (
      <button type="button" onClick={openLoginModal} title="Sign in to save favorites"
        className={btnBase} style={{ background: 'rgba(255,255,255,0.9)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#9aa3af" strokeWidth="2">
          <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
        </svg>
      </button>
    );
  }

  const isFav = favoriteSwimmerIds.has(swimmerId);
  const isMe = swimmerId === primarySwimmerId;
  return (
    <div className="flex items-center gap-2">
      <button type="button" onClick={() => toggleFavoriteSwimmer(swimmerId)}
        title={isFav ? 'Remove from favorites' : 'Add to favorites'} aria-pressed={isFav}
        className={btnBase} style={{ background: 'rgba(255,255,255,0.9)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill={isFav ? '#e23b5a' : 'none'} stroke={isFav ? '#e23b5a' : '#9aa3af'} strokeWidth="2">
          <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
        </svg>
      </button>
      <button type="button" onClick={() => setMeBySwimmer(swimmerId)}
        title={isMe ? 'This is me — unmark' : 'Mark: this is me'} aria-pressed={isMe}
        className={btnBase} style={{ background: isMe ? '#fff6da' : 'rgba(255,255,255,0.75)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill={isMe ? '#f5b800' : 'none'} stroke={isMe ? '#d99a00' : '#9aa3af'} strokeWidth="1.8" strokeLinejoin="round">
          <path d="M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z" />
        </svg>
      </button>
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
      {/* ❤/⭐ — справа на десктопе, под мета на мобиле */}
      <div className="sm:ml-auto">
        <FavoriteControls swimmerId={profile.id} />
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

function MediaTile({ item, onClick }: { item: GalleryItem; onClick: () => void }) {
  const thumb = HelperMedia.resolveThumbUrl(item.type, item.sourceType ?? 'other', item.url ?? '');
  return (
    <button
      type="button"
      onClick={onClick}
      className="group relative aspect-video overflow-hidden rounded-[12px] border"
      style={{ borderColor: 'var(--theme-mode-border)', background: 'var(--theme-mode-surface)' }}
    >
      {thumb ? (
        <img src={thumb} alt="" className="h-full w-full object-cover transition-transform group-hover:scale-105" />
      ) : (
        <div className="flex h-full w-full items-center justify-center text-[28px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          {item.type === 'image' ? '🖼' : '▶'}
        </div>
      )}
      {item.type === 'video' && (
        <span className="absolute inset-0 flex items-center justify-center" style={{ background: 'rgba(0,0,0,0.15)' }}>
          <span className="flex h-9 w-9 items-center justify-center rounded-full bg-black/55 text-white">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M8 5v14l11-7z" />
            </svg>
          </span>
        </span>
      )}
    </button>
  );
}

function MediaGallery({ swimmerId }: { swimmerId: number }) {
  const media = useSwimmerMedia(swimmerId);
  const [openIndex, setOpenIndex] = useState<number | null>(null);
  if (media.length === 0) return null; // пусто — секцию не показываем (аноним видит только public)

  return (
    <div className="rounded-[12px] p-4" style={cardStyle}>
      <SectionTitle>Media</SectionTitle>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        {media.map((item, idx) => (
          <MediaTile key={`${item.url}-${idx}`} item={item} onClick={() => setOpenIndex(idx)} />
        ))}
      </div>
      <UI_SwimmerGallery gallery={media} openIndex={openIndex} onClose={() => setOpenIndex(null)} popupSize="lg" />
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

      {/* Галерея видимого медиа пловца (пусто → секция скрыта). */}
      <MediaGallery swimmerId={profile.id} />
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
