import React from 'react';
import type { CompetitionOverview, OverviewBestSwim, OverviewMedalist } from '../types';
import { GENDER_CIRCLE } from '../overview-shared';
import { initials } from './module-defs';
import UI_SwimmerNameCell from '../../../../components/mix/swimmer-name-cell/swimmer-name-cell';

// Карточка модуля Champions (9d): Best swim ♂/♀ (медальон-очки 74px) + Most decorated ♂/♀.
// Реализация по спеке dc.html секция 9d (sc-if is9dBest, композиция 1:1).
//
// ПРАВИЛА КАРТОЧКИ — docs/competition-overview-cards.md (раздел Champions). Кратко:
//   Best swim      — лучший заплыв по международным очкам, не по времени;
//   Most decorated — медали личные + эстафетные (эстафетная идёт КАЖДОЙ ноге через
//                    RelayMembers), порядок золото → серебро → бронза (не по сумме),
//                    при одинаковом наборе медалей показываются ВСЕ.
// Сам расчёт — на сервере (ResultRepository.GetCompetitionOverviewAsync); здесь только показ.

interface Props {
  overview: CompetitionOverview;
  onOpenSwim?(swim: { result_id: number | null; style_name: string; distance: string }): void;
  onOpenClub?(club: string): void;
}

const PANEL_BG: Record<'male' | 'female', string> = {
  male: 'rgba(29,78,216,.06)',
  female: 'rgba(190,24,93,.06)',
};

/** Резолв best_swim по полу с фолбэком на единый best_swim (та же логика, что в старом Overview). */
function resolveBestSwim(overview: CompetitionOverview, gender: 'male' | 'female'): OverviewBestSwim | null {
  const byGender = gender === 'male' ? overview.best_swim_male : overview.best_swim_female;
  if (byGender) return byGender;
  if (overview.best_swim && overview.best_swim.gender === gender) return overview.best_swim;
  return null;
}

/**
 * Резолв «самых титулованных» по полу с фолбэком на общий список.
 * Список, а не один человек: при одинаковом наборе медалей сервер отдаёт всех (is_tie).
 */
function resolveMedalists(overview: CompetitionOverview, gender: 'male' | 'female'): OverviewMedalist[] {
  const byGender = gender === 'male' ? overview.top_medalists_male : overview.top_medalists_female;
  if (byGender?.length) return byGender;
  // top_medalists без пола — фолбэк только когда разбивки по полу нет вовсе.
  const noSplit = !overview.top_medalists_male?.length && !overview.top_medalists_female?.length;
  return noSplit ? (overview.top_medalists ?? []) : [];
}

/** Панель BEST SWIM: тексты слева, медальон очков 74px справа (десктоп; на мобиле — слева).
 *  Имя + клуб — общий UI_SwimmerNameCell с иконкой клуба. */
function BestSwimPanel({ swim, gender, onOpenSwim }: {
  swim: OverviewBestSwim; gender: 'male' | 'female'; onOpenSwim?: Props['onOpenSwim'];
}) {
  return (
    <button
      type="button"
      onClick={() => onOpenSwim?.(swim)}
      className="flex min-w-0 items-center gap-3 rounded-[10px] p-3 text-left"
      style={{ background: PANEL_BG[gender] }}
    >
      <div className="flex min-w-0 flex-1 flex-col gap-[2px]">
        <span className="text-[11px] font-extrabold tracking-[0.05em]" style={GENDER_CIRCLE[gender]}>
          {gender === 'male' ? '♂' : '♀'} BEST SWIM
        </span>
        <UI_SwimmerNameCell
          firstName={swim.is_relay ? (swim.relay_team_name ?? 'Relay Team') : swim.first_name}
          lastName={swim.is_relay ? undefined : swim.last_name}
          firstNameEn={swim.first_name_en}
          lastNameEn={swim.last_name_en}
          club={swim.club}
          showClubIcon
          clubIconSide="left"
          clubIconWidth="10"
          firstLineClassName="truncate text-[15px] font-extrabold"
          secondLineClassName="truncate text-[12px] font-semibold text-[var(--theme-mode-text-secondary)]"
          className="min-w-0"
        />
        <span className="text-[15px] font-extrabold tabular-nums" style={{ color: 'var(--theme-mode-text)' }}>
          {swim.time}{' '}
          <span className="text-[11.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
            {swim.distance}m {swim.style_name}
          </span>
        </span>
      </div>
      <div
        className="order-first flex flex-none flex-col items-center justify-center rounded-full lg:order-last"
        style={{
          width: 74, height: 74,
          background: 'var(--m-surface)',
          border: '2px solid color-mix(in srgb, var(--m) 35%, transparent)',
        }}
      >
        <span className="text-[24px] font-black leading-none" style={{ color: 'var(--m-solid)' }}>{swim.international_points}</span>
        <span className="text-[9px] font-extrabold uppercase tracking-[0.06em]" style={{ color: 'var(--m-solid)' }}>points</span>
      </div>
    </button>
  );
}

/**
 * Панель MOST DECORATED: имя/клуб + пилюля медалей.
 * При ничьей (одинаковый набор медалей) показываем ВСЕХ — набор один на панель, потому
 * что он у них совпадает; имена идут списком, на мобиле в столбик.
 */
function MedalistPanel({ medalists, gender, onOpenClub }: {
  medalists: OverviewMedalist[]; gender: 'male' | 'female'; onOpenClub?: Props['onOpenClub'];
}) {
  const top = medalists[0];
  const relayMedals = Math.max(...medalists.map((m) => m.relay_medals ?? 0));

  return (
    <button
      type="button"
      onClick={() => onOpenClub?.(top.club)}
      className="flex min-w-0 items-center gap-2.5 rounded-[10px] p-2.5 text-left"
      style={{ background: PANEL_BG[gender] }}
    >
      {/* gap-y на всей колонке, а не только между именами: иконка клуба выше строки текста
          и без отступа наезжала на заголовок «MOST DECORATED». */}
      <span className="min-w-0 flex flex-col gap-y-1.5">
        <span className="text-[11px] font-extrabold tracking-[0.05em]" style={GENDER_CIRCLE[gender]}>
          {gender === 'male' ? '♂' : '♀'} MOST DECORATED
        </span>
        <span className="flex min-w-0 flex-col gap-y-2.5 sm:flex-row sm:flex-wrap sm:items-center sm:gap-x-4 sm:gap-y-2.5">
          {medalists.map((m) => (
            <UI_SwimmerNameCell
              key={m.swimmer_id}
              firstName={m.first_name}
              lastName={m.last_name}
              firstNameEn={m.first_name_en}
              lastNameEn={m.last_name_en}
              club={m.club}
              showClubIcon
              clubIconSide="left"
              clubIconWidth="10"
              firstLineClassName="truncate text-[14px] font-extrabold"
              secondLineClassName="truncate text-[12px] font-semibold text-[var(--theme-mode-text-secondary)]"
              className="min-w-0"
            />
          ))}
        </span>
        {relayMedals > 0 && (
          <span className="text-[11px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
            incl. relays: {relayMedals}
          </span>
        )}
      </span>
      <span className="flex-1" />
      <span className="ov2-card__chip flex-none">
        🥇{top.gold} 🥈{top.silver} 🥉{top.bronze}
      </span>
    </button>
  );
}

export default function ModuleCardChampions({ overview, onOpenSwim, onOpenClub }: Props) {
  const bestSwimMale = resolveBestSwim(overview, 'male');
  const bestSwimFemale = resolveBestSwim(overview, 'female');
  const medalistsMale = resolveMedalists(overview, 'male');
  const medalistsFemale = resolveMedalists(overview, 'female');

  if (!bestSwimMale && !bestSwimFemale && !medalistsMale.length && !medalistsFemale.length) return null;

  return (
    <section className="ov2-card" data-module="champions">
      <div className="mb-3 flex items-baseline gap-2.5">
        <span className="ov2-card__title">Champions</span>
        <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
          Best swim · Most decorated · ♂ / ♀
        </span>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        {bestSwimMale && <BestSwimPanel swim={bestSwimMale} gender="male" onOpenSwim={onOpenSwim} />}
        {bestSwimFemale && <BestSwimPanel swim={bestSwimFemale} gender="female" onOpenSwim={onOpenSwim} />}
        {medalistsMale.length > 0 && <MedalistPanel medalists={medalistsMale} gender="male" onOpenClub={onOpenClub} />}
        {medalistsFemale.length > 0 && <MedalistPanel medalists={medalistsFemale} gender="female" onOpenClub={onOpenClub} />}
      </div>
    </section>
  );
}
