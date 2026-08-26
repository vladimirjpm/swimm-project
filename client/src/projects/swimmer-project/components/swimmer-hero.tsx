import React from 'react';
import UI_ClubIcon from '../../components/mix/club-icon/club-icon';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import { useFavoritesContext } from '../../../hooks/favorites-context';
import { useLoginModal } from '../../components/login-modal/login-modal-context';
import { routes } from '../../../utils/routes';
import type { SwimmerProfile } from '../use-swimmer-profile';
import type { SwimmerSummary } from '../use-swimmer-page';
import type { NormativeLevelInfo } from '../../../utils/interfaces/normative-level-info';

/**
 * Шапка страницы спортсмена (BLOCKS.md §1–2, вариант 2a «Card DNA»).
 *
 * Устройство десктопа: аватар с флагом и действиями | имя и мета | KPI-плитки.
 * В мобайле KPI уезжают отдельным рядом под баром.
 *
 * Гость видит ВЕСЬ публичный контент — скрыты только действия (♡/★), вместо них полоса
 * «Sign in». Заглушек «войдите» внутри контентных блоков нет (§10 хендоффа).
 */

/**
 * Вход плитки достижений. Правила, что и когда показывать, — в
 * `docs/swimmer-achievements-tile.md`; здесь только отрисовка.
 */
export interface HeroAchievements {
  /** Подпись сверху: «All time» либо метка сезона («2025/26»). */
  scopeLabel: string;
  /** Официальные рекорды — ВСЕГДА за карьеру: у записи справочника нет сезона. */
  records: number;
  /** Первых мест среди сверстников в сезоне, за который посчитаны места. */
  seasonBests: number;
  /** За какой сезон посчитаны season bests — может отличаться от scopeLabel в режиме ∞. */
  seasonBestsLabel: string;
}

interface Props {
  profile: SwimmerProfile;
  summary: SwimmerSummary | null;
  /** Лучший достигнутый уровень — считает страница из лучших времён (нормативы клиентские). */
  level: NormativeLevelInfo | null;
  achievements: HeroAchievements;
}

/** ♡ избранное + ★ «это я». Гостю — полоса с приглашением войти (GuestFavoritesCta). */
function Actions({ swimmerId }: { swimmerId: number }) {
  const { isAuthenticated, primarySwimmerId, favoriteSwimmerIds, setMeBySwimmer, toggleFavoriteSwimmer } =
    useFavoritesContext();
  const { openLoginModal } = useLoginModal();

  if (!isAuthenticated) {
    return (
      <button type="button" onClick={openLoginModal} className="deep-hero-cta">
        Sign in to save favorites
      </button>
    );
  }

  const isFav = favoriteSwimmerIds.has(swimmerId);
  const isMe = swimmerId === primarySwimmerId;

  return (
    <div className="flex items-center gap-2">
      <button
        type="button"
        onClick={() => toggleFavoriteSwimmer(swimmerId)}
        title={isFav ? 'Remove from favorites' : 'Add to favorites'}
        aria-pressed={isFav}
        className={`deep-hero-action${isFav ? ' deep-hero-action--fav' : ''}`}
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill={isFav ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="2">
          <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
        </svg>
      </button>
      <button
        type="button"
        onClick={() => setMeBySwimmer(swimmerId)}
        title={isMe ? 'This is me — unmark' : 'Mark: this is me'}
        aria-pressed={isMe}
        className={`deep-hero-action${isMe ? ' deep-hero-action--me' : ''}`}
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill={isMe ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round">
          <path d="M12 2.6l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 17.45 6.2 20.5l1.1-6.45-4.7-4.6 6.5-.95z" />
        </svg>
      </button>
    </div>
  );
}

/** Одна цифра плитки достижений: значение сверху, подпись под ним. */
function Stat({ value, caption, gold = false, title }: {
  value: React.ReactNode; caption: string; gold?: boolean; title?: string;
}) {
  return (
    <span className="deep-achv__stat" title={title}>
      <span className={`deep-achv__num${gold ? ' deep-achv__num--gold' : ''}`}>{value}</span>
      <span className="deep-achv__cap">{caption}</span>
    </span>
  );
}

/**
 * Плитка достижений (правила — docs/swimmer-achievements-tile.md).
 * Коротко: есть рекорды и/или season bests — показываем их; нет ни того ни другого —
 * старты и очки, чтобы плитка не пустовала.
 */
function AchievementsTile({ a, summary }: { a: HeroAchievements; summary: SwimmerSummary | null }) {
  const hasAny = a.records > 0 || a.seasonBests > 0;

  return (
    <div className="deep-kpi-tile">
      <div className="deep-kpi-scope">{a.scopeLabel}</div>
      <div className="deep-achv">
        {hasAny ? (
          <>
            {a.records > 0 && (
              <Stat
                value={<>🏆 {a.records}</>}
                caption={a.records === 1 ? 'record' : 'records'}
                gold
                title="Official records held — all time: the federation register has no season"
              />
            )}
            {a.seasonBests > 0 && (
              <Stat
                value={<>SB {a.seasonBests}</>}
                caption={a.seasonBests === 1 ? 'season best' : 'season bests'}
                gold
                title={`Fastest in the age group — season ${a.seasonBestsLabel}`}
              />
            )}
          </>
        ) : (
          <>
            <Stat value={summary ? summary.competitionCount : '—'} caption="meets" />
            <Stat
              value={summary ? summary.points.toLocaleString('en-US') : '—'}
              caption="points"
            />
          </>
        )}
      </div>
      <div className="deep-kpi-label">{hasAny ? 'Achievements' : 'Activity'}</div>
    </div>
  );
}

function Kpi({ summary, level, achievements }: {
  summary: SwimmerSummary | null;
  level: NormativeLevelInfo | null;
  achievements: HeroAchievements;
}) {
  return (
    <div className="deep-kpi-row">
      <AchievementsTile a={achievements} summary={summary} />

      <div className="deep-kpi-tile">
        {/* Нулевые номиналы приглушены, а не спрятаны: пустая полка это тоже факт (§2). */}
        <div className="deep-kpi-medals">
          {(['1', '2', '3'] as const).map((place, i) => {
            const count = summary
              ? [summary.medals.gold, summary.medals.silver, summary.medals.bronze][i]
              : 0;
            return (
              <span key={place} className={count > 0 ? '' : 'deep-medal--empty'}>
                <UI_MedalIcon
                  place={place}
                  styleType="icon-place"
                  styleSize="medal-24"
                  placeReplace={String(count)}
                />
              </span>
            );
          })}
        </div>
        <div className="deep-kpi-label">Medals</div>
      </div>

      <div className="deep-kpi-tile">
        <div className="deep-kpi-level">
          {level && level.currentLevel !== 'none' ? (
            <UI_NormativeLevelIcon
              levelName={level.currentLevel}
              styleType="gauge"
              styleSize="size-2"
              // Полоса мастерса под дугой — тот же признак, что в строках результата.
              isMasters={!!level.normativeAgeGroup}
              normativeAgeGroup={level.normativeAgeGroup}
              progressPercent={level.progressToNextLevel}
              nextTime={level.nextTime}
              showProgress
              disableClick
            />
          ) : (
            <span className="deep-kpi-value">—</span>
          )}
        </div>
        <div className="deep-kpi-label">Level</div>
      </div>
    </div>
  );
}

function SwimmerHero({ profile, summary, level, achievements }: Props) {
  const initial = (profile.fullName.trim().charAt(0) || '?').toUpperCase();

  return (
    <div className="deep-hero">
      <div className="deep-hero__id">
        <div className="deep-hero__avatar-box">
          {profile.avatarUrl ? (
            <img src={profile.avatarUrl} alt="" className="deep-hero__avatar" />
          ) : (
            <span className="deep-hero__avatar deep-hero__avatar--empty">{initial}</span>
          )}
          {profile.countryCode && (
            <span className="deep-hero__flag">
              <UI_FlagEmoji countryCode={profile.countryCode} size="28x21" />
            </span>
          )}
        </div>
        <Actions swimmerId={profile.id} />
      </div>

      <div className="deep-hero__main">
        {/* Чип «🏆 N records» у имени снят: счётчик рекордов живёт в плитке достижений
            (docs/swimmer-achievements-tile.md), и одна и та же цифра в шапке дважды —
            это не акцент, а шум. */}
        <h1 dir="auto" className="deep-hero__name">{profile.fullName}</h1>

        <div className="deep-hero__age">
          {profile.ageInSeason != null
            ? `${profile.ageInSeason} year (${profile.birthYear})`
            : profile.birthYear > 0 ? `b. ${profile.birthYear}` : '—'}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {profile.clubName && (
            profile.clubId ? (
              <a href={routes.club(profile.clubId)} className="deep-club-chip" dir="auto">
                <span className="deep-club-chip__logo">
                  <UI_ClubIcon clubName={profile.clubName} iconWidth="22px" />
                </span>
                {profile.clubName}
              </a>
            ) : (
              <span className="deep-club-chip" dir="auto">
                <span className="deep-club-chip__logo">
                  <UI_ClubIcon clubName={profile.clubName} iconWidth="22px" />
                </span>
                {profile.clubName}
              </span>
            )
          )}
          {profile.ageGroup && (
            <span className="deep-chip deep-chip--accent">
              {profile.ageGroup.badge ? `${profile.ageGroup.badge} · ` : ''}{profile.ageGroup.label}
            </span>
          )}
          {(profile.programs ?? []).includes('open') && (
            <span className="deep-chip deep-chip--ow">🌊 open water</span>
          )}
          {profile.origin === 'local' && <span className="deep-chip">local</span>}
        </div>
      </div>

      <Kpi summary={summary} level={level} achievements={achievements} />
    </div>
  );
}

export default SwimmerHero;
