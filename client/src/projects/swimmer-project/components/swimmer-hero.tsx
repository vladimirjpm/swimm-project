import React from 'react';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_SwimmerIdentityHero from '../../components/mix/swimmer-identity/swimmer-identity-hero';
import type { SwimmerProfile } from '../use-swimmer-profile';
import type { SwimmerSummary } from '../use-swimmer-page';
import type { NormativeLevelInfo } from '../../../utils/interfaces/normative-level-info';

/**
 * Шапка страницы спортсмена (BLOCKS.md §1–2, вариант 2a «Card DNA»).
 *
 * Устройство десктопа: аватар с флагом и действиями | имя и мета | KPI-плитки.
 * В мобайле KPI уезжают отдельным рядом под баром.
 *
 * Сама идентичность (аватар, имя, возраст, клуб, ♡/★) живёт в общем
 * `UI_SwimmerIdentityHero` — том же семействе, что карточка-попап и будущий мини-вариант.
 * Здесь остались ЦИФРЫ страницы: KPI-плитки, медали, разряд, достижения — они приезжают
 * в шапку слотом `aside`.
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
  return (
    <UI_SwimmerIdentityHero
      identity={{
        id: profile.id,
        name: profile.fullName,
        birthYear: profile.birthYear,
        ageInSeason: profile.ageInSeason,
        clubName: profile.clubName,
        clubId: profile.clubId,
        countryCode: profile.countryCode,
        avatarUrl: profile.avatarUrl,
        gender: profile.gender,
      }}
      chips={(
        <>
          {profile.ageGroup && (
            <span className="deep-chip deep-chip--accent">
              {profile.ageGroup.badge ? `${profile.ageGroup.badge} · ` : ''}{profile.ageGroup.label}
            </span>
          )}
          {(profile.programs ?? []).includes('open') && (
            <span className="deep-chip deep-chip--ow">🌊 open water</span>
          )}
          {profile.origin === 'local' && <span className="deep-chip">local</span>}
        </>
      )}
      aside={<Kpi summary={summary} level={level} achievements={achievements} />}
    />
  );
}

export default SwimmerHero;
