import React from 'react';
import type { CompetitionOverview, OverviewClub } from '../types';
import { GENDER_CIRCLE } from '../overview-shared';
import UI_ClubIcon from '../../../../components/mix/club-icon/club-icon';
import { rootActions, useAppDispatch } from '../../../../../store/store';
import { Enums } from '../../../../../utils/interfaces/enums';

// Карточка модуля Top clubs (9d): слева топ-5 + ♂/♀ топ-3, справа витрина клуба-чемпиона.
// Реализация по спеке dc.html секция 9d (sc-if is9dClubs, композиция 1:1).
//
// ПРАВИЛА КАРТОЧКИ — docs/competition-overview-cards.md (раздел Top clubs). Кратко:
//   очки за места по правилу клубных очков соревнования, эстафета — с множителем
//   (обычно ×2); три зачёта (общий/♂/♀); категория заплыва (para/mix) сознательно НЕ
//   учитывается — места протокола уже корректны внутри своей программы.
//
// Медали / шкала очков / панели ♂-♀ выключаются пропсами (showMedals, showPointsRule,
// showGenderPanels) — так ненаградное соревнование показывает голый зачёт очков тем же
// компонентом, без отдельной «упрощённой» карточки.

interface Props {
  overview: CompetitionOverview;
  onOpenTab(tab: 'swims' | 'clubs' | 'media' | 'records'): void;
  onOpenClub?(club: string): void;
  /** Показывать медали (строки зачёта + витрина лидера). false — ненаградное соревнование:
   *  места в протоколе есть, медалей нет, остаётся только зачёт очков. */
  showMedals?: boolean;
  /** Показывать кнопку «Points system» (шкала правила очков попапом). */
  showPointsRule?: boolean;
  /** Показывать панели зачёта ♂/♀ под общим списком. */
  showGenderPanels?: boolean;
}

function ClubRow({ club, rank, big, showMedals = true, onOpenClub }: {
  club: OverviewClub; rank: number; big?: boolean; showMedals?: boolean; onOpenClub?: (club: string) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => onOpenClub?.(club.club)}
      className={`flex min-w-0 w-full items-center gap-2 border-t text-left font-bold ${big ? 'py-2 text-[13px] gap-2.5' : 'py-1.5 text-[12.5px]'}`}
      style={{ borderColor: 'var(--theme-mode-border)', color: 'var(--theme-mode-text)' }}
    >
      <span className={`flex-none ${big ? 'w-4' : 'w-3'}`} style={{ color: 'var(--theme-mode-text-muted)' }}>{rank}</span>
      <span dir="auto" className="min-w-0 flex-1 truncate">{club.club}</span>
      {big && showMedals && (
        <span className="flex-none whitespace-nowrap font-semibold">
          🥇{club.gold} 🥈{club.silver} 🥉{club.bronze}
        </span>
      )}
      <span className="ov2-card__value flex-none text-right" style={{ width: big ? 52 : 'auto' }}>
        {club.points}
      </span>
    </button>
  );
}

function GenderPanel({ title, symbol, gender, clubs, onOpenClub }: {
  title: string; symbol: string; gender: 'male' | 'female'; clubs: OverviewClub[]; onOpenClub?: (club: string) => void;
}) {
  if (!clubs.length) return null;
  const bg = gender === 'male' ? 'rgba(29,78,216,.05)' : 'rgba(190,24,93,.05)';
  return (
    <div className="min-w-0 flex flex-col rounded-[10px] p-3" style={{ background: bg }}>
      <div className="mb-1 flex items-center gap-[7px]">
        <span className="text-[13px] font-black" style={GENDER_CIRCLE[gender]}>{symbol}</span>
        <span className="text-[12px] font-extrabold">{title}</span>
      </div>
      {clubs.slice(0, 3).map((c, i) => (
        <ClubRow key={c.club} club={c} rank={i + 1} onOpenClub={onOpenClub} />
      ))}
    </div>
  );
}

export default function ModuleCardClubs({
  overview, onOpenTab, onOpenClub,
  showMedals = true, showPointsRule = true, showGenderPanels = true,
}: Props) {
  const dispatch = useAppDispatch();
  const clubs = overview.top_clubs.slice(0, 5);
  const leader = overview.top_clubs[0] ?? null;
  const rules = overview.club_points_rules ?? [];
  // Официальные очки расходятся с нашими: у организатора ошибка, наш расчёт сверен вручную.
  // Без подписи расхождение читается как наш баг, поэтому говорим о нём прямо.
  const officialMismatch = overview.club_points_verified === 'mismatch';

  // Шкала очков — попапом: цифра очков без правила необъяснима, а разворачивать 24 места
  // прямо в карточке негде.
  const openPointsRule = () =>
    dispatch(rootActions.updateState({
      isPopup: true,
      popUpType: Enums.PopupType.clubPoints,
      popUpObj: { rules },
    }));

  return (
    <section className="ov2-card grid gap-5 lg:grid-cols-[minmax(0,1.7fr)_268px] items-start" data-module="clubs">
      {/* Список */}
      <div className="min-w-0 flex flex-col gap-3.5">
        <div className="min-w-0 flex flex-col">
          <div className="mb-2 flex items-baseline gap-2.5">
            <span className="ov2-card__title">Top clubs</span>
            <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
              club standings
            </span>
            {showPointsRule && (
              <button
                type="button"
                onClick={openPointsRule}
                className="ov2-card__link"
                title="How club points are scored"
              >
                Points system
              </button>
            )}
            {officialMismatch && (
              <span
                className="rounded-full px-2 py-[2px] text-[11px] font-extrabold"
                style={{
                  background: 'color-mix(in srgb, #dc2626 14%, transparent)',
                  color: '#dc2626',
                  border: '1px solid color-mix(in srgb, #dc2626 30%, transparent)',
                }}
                title="The official standings contain an error. We checked the results by hand — the points shown here are correct."
              >
                ★ Differs from official — ours verified
              </span>
            )}
            <span className="flex-1" />
            <button type="button" onClick={() => onOpenTab('clubs')} className="ov2-card__link">
              Clubs tab →
            </button>
          </div>
          {clubs.map((c, i) => (
            <ClubRow key={c.club} club={c} rank={i + 1} big showMedals={showMedals} onOpenClub={onOpenClub} />
          ))}
          <button type="button" onClick={() => onOpenTab('clubs')} className="mt-2 ov2-card__link text-[12.5px]">
            All {overview.summary.club_count} clubs →
          </button>
        </div>

        {showGenderPanels && (
          <div className="grid grid-cols-2 gap-3">
            <GenderPanel title="Men" symbol="♂" gender="male" clubs={overview.top_clubs_men} onOpenClub={onOpenClub} />
            <GenderPanel title="Women" symbol="♀" gender="female" clubs={overview.top_clubs_women} onOpenClub={onOpenClub} />
          </div>
        )}
      </div>

      {/* Витрина клуба-лидера */}
      {leader && (
        <button
          type="button"
          onClick={() => onOpenClub?.(leader.club)}
          className="ov2-card__hero min-h-11"
        >
          <span
            className="rounded-full px-[11px] py-[3px] text-[10px] font-extrabold uppercase tracking-[0.08em]"
            style={{ background: 'var(--m-solid)', color: 'var(--m-on-solid)' }}
          >
            Champion club
          </span>
          <span
            className="flex h-24 w-24 flex-none items-center justify-center rounded-full"
            style={{ background: 'var(--m-surface)', border: '2px solid color-mix(in srgb, var(--m) 30%, transparent)', boxShadow: '0 6px 18px color-mix(in srgb, var(--m) 14%, transparent)' }}
          >
            {/* Лого клуба-чемпиона; UI_ClubIcon сам падает на no-club.png, полосатый
                плейсхолдер с инициалами остаётся подложкой. */}
            <span className="ov2-logo h-[76px] w-[76px] text-[15px]">
              <UI_ClubIcon clubName={leader.club} iconWidth="full" />
            </span>
          </span>
          <span dir="auto" className="text-[17px] font-black leading-[1.25] text-wrap-pretty">{leader.club}</span>
          <span className="flex items-baseline justify-center gap-1.5">
            <span className="ov2-card__value text-[30px] leading-none">{leader.points}</span>
            <span className="text-[11px] font-extrabold tracking-[0.06em] opacity-70" style={{ color: 'var(--m-solid)' }}>
              POINTS
            </span>
          </span>
          {showMedals && (
            <span className="grid w-full grid-cols-3 gap-1.5">
              <span className="flex flex-col gap-px rounded-[9px] py-1.5" style={{ background: 'var(--m-surface)' }}>
                <span className="text-[15px] font-black">{leader.gold}</span>
                <span className="text-[10px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>🥇</span>
              </span>
              <span className="flex flex-col gap-px rounded-[9px] py-1.5" style={{ background: 'var(--m-surface)' }}>
                <span className="text-[15px] font-black">{leader.silver}</span>
                <span className="text-[10px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>🥈</span>
              </span>
              <span className="flex flex-col gap-px rounded-[9px] py-1.5" style={{ background: 'var(--m-surface)' }}>
                <span className="text-[15px] font-black">{leader.bronze}</span>
                <span className="text-[10px] font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>🥉</span>
              </span>
            </span>
          )}
          {/* «scoring swims», а не «results»: successfulCount — заплывы, ПРИНЁСШИЕ очки, а не
              все заплывы клуба. С «results» падение цифры вдвое при Combine All Results
              читалось как потеря данных (у клуба 132 заплыва при 95 очковых). */}
          <span
            className="text-[11.5px] font-bold"
            style={{ color: 'var(--theme-mode-text-secondary)' }}
            title="Swims that scored club points — not the club's total number of swims"
          >
            {leader.swimmerCount} swimmers · {leader.successfulCount} scoring swims
          </span>
          <span className="ov2-card__link">Club page →</span>
        </button>
      )}
    </section>
  );
}
