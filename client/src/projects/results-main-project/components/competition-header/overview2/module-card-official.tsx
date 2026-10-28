import React from 'react';
import type {
  CompetitionOverview, OverviewOfficialAwardGroup, OverviewOfficialAwardPlace, OverviewOfficialAwardsDay,
} from '../types';
import { GENDER_CIRCLE } from '../overview-shared';
import UI_SwimmerNameCell from '../../../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_MedalIcon from '../../../../components/mix/medal-icon/medal-icon';
import { routes } from '../../../../../utils/routes';

// Карточка модуля Official awards: награждение ПО РЕГЛАМЕНТУ серии турнира — рядом со
// стандартным High Point, а не вместо него (docs/plans/official-awards-plan.md §5.5).
//
// ПРАВИЛА КАРТОЧКИ — docs/competition-overview-cards.md (раздел Official awards). Кратко:
//   считает сервер — по наградам части серии, к которой привязан день, и строкам ЭТОГО дня;
//   группы — по сезонному возрасту из регламента, не группы протокола; места — rank()
//   (ничья делит место, следующее пропускается); помеченные строки (Under review) не идут.
// Блок на каждый день: у дней одного события регламенты разные.

interface Props {
  overview: CompetitionOverview;
}

/** «Fri 23/10» из dd/MM/yyyy. */
function dayLabel(date: string): string {
  const [d, m, y] = date.split('/').map(Number);
  if (!d || !m || !y) return date;
  const weekday = new Date(y, m - 1, d).toLocaleDateString('en-GB', { weekday: 'short' });
  return `${weekday} ${String(d).padStart(2, '0')}/${String(m).padStart(2, '0')}`;
}

/**
 * Подпись группы: «Girls 10–11», «Boys 13–14», «Women 16+», «Men», «All».
 * До 17 лет включительно — девочки/мальчики, старше или без верхней границы — женщины/мужчины.
 */
export function groupLabel(g: Pick<OverviewOfficialAwardGroup, 'gender' | 'min_age' | 'max_age'>): string {
  const young = g.max_age != null && g.max_age <= 17;
  const who = g.gender === 'female' ? (young ? 'Girls' : 'Women')
    : g.gender === 'male' ? (young ? 'Boys' : 'Men')
    : 'All';
  const range = g.min_age != null && g.max_age != null
    ? (g.min_age === g.max_age ? `${g.min_age}` : `${g.min_age}–${g.max_age}`)
    : g.min_age != null ? `${g.min_age}+`
    : g.max_age != null ? `≤${g.max_age}`
    : '';
  return range ? `${who} ${range}` : who;
}

function PlaceRow({ p }: { p: OverviewOfficialAwardPlace }) {
  return (
    <div className="flex min-w-0 items-center gap-2.5">
      <span className="flex-none" title={p.is_tie ? `Shared place ${p.place}` : undefined}>
        <UI_MedalIcon place={String(p.place)} styleSize="medal-24" />
      </span>
      <a href={routes.swimmer(p.swimmer_id)} className="min-w-0 flex-1">
        <UI_SwimmerNameCell
          firstName={p.first_name}
          lastName={p.last_name}
          firstNameEn={p.first_name_en}
          lastNameEn={p.last_name_en}
          club={p.club}
          clubId={p.club_id}
          showClubIcon
          clubIconSide="left"
          clubIconWidth="8"
          firstLineClassName="truncate text-[14px] font-extrabold"
          secondLineClassName="truncate text-[12px] font-semibold text-[var(--theme-mode-text-secondary)]"
          className="min-w-0"
        />
      </a>
      {/* Очки, разбивка и приз — одной колонкой справа: чип приза в ряд с именем съедал
          ширину, и ивритское имя обрезалось с начала. */}
      <span className="flex max-w-[45%] flex-none flex-col items-end text-right">
        <span className="text-[15px] font-black tabular-nums leading-tight" style={{ color: 'var(--m-solid)' }}>
          {p.points}
        </span>
        {p.parts.length > 1 && (
          <span className="text-[10.5px] font-semibold tabular-nums" style={{ color: 'var(--theme-mode-text-muted)' }}>
            {p.parts.join('+')}
          </span>
        )}
        {p.prize && (
          <span className="max-w-full truncate text-[11px] font-extrabold" style={{ color: 'var(--m-solid)' }} title={p.prize}>
            {p.prize}
          </span>
        )}
      </span>
    </div>
  );
}

function GroupPanel({ g }: { g: OverviewOfficialAwardGroup }) {
  const tone = g.gender === 'male' || g.gender === 'female' ? GENDER_CIRCLE[g.gender] : undefined;
  return (
    <div className="flex min-w-0 flex-col gap-2 rounded-[10px] p-2.5" style={{ background: 'var(--m-hover)' }}>
      <span className="self-start rounded-full px-2 py-px text-[11px] font-extrabold tracking-[0.04em]"
        style={tone ?? { background: 'var(--m-chip)', color: 'var(--m-solid)' }}>
        {g.gender === 'male' ? '♂ ' : g.gender === 'female' ? '♀ ' : ''}{groupLabel(g)}
      </span>
      {g.places.length === 0
        ? <span className="text-[12px]" style={{ color: 'var(--theme-mode-text-muted)' }}>No eligible swimmers</span>
        : g.places.map((p) => <PlaceRow key={`${p.place}-${p.swimmer_id}`} p={p} />)}
    </div>
  );
}

function DayBlock({ day, showDay }: { day: OverviewOfficialAwardsDay; showDay: boolean }) {
  const part = day.part_name_en || day.part_name;
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
        <span className="text-[13px] font-extrabold" style={{ color: 'var(--theme-mode-text)' }} dir="auto">
          {showDay ? `${dayLabel(day.date)} · ${part}` : part}
        </span>
        {day.regulation.source_url && (
          <a href={day.regulation.source_url} target="_blank" rel="noopener noreferrer"
            className="text-[12px] font-semibold underline-offset-2 hover:underline" style={{ color: 'var(--m-solid)' }}>
            per meet regulations ↗
          </a>
        )}
      </div>
      {day.awards.map((a) => (
        <div key={a.title} className="flex flex-col gap-2">
          <div className="flex flex-wrap items-baseline gap-x-2">
            <span className="text-[12px] font-extrabold uppercase tracking-[0.05em]" style={{ color: 'var(--m-solid)' }}>
              {a.title}
            </span>
            <span className="text-[11.5px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
              {a.count_best ? `sum of best ${a.count_best} swims · FINA points` : 'sum of all swims · FINA points'}
            </span>
          </div>
          <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
            {a.groups.map((g) => <GroupPanel key={`${g.gender}-${g.min_age}-${g.max_age}`} g={g} />)}
          </div>
        </div>
      ))}
      {!day.regulation.checked && (
        <span className="text-[11px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          * computed by the series regulations in force since {day.regulation.effective_from}; this year's document not yet verified
        </span>
      )}
    </div>
  );
}

export default function ModuleCardOfficial({ overview }: Props) {
  const days = overview.official_awards ?? [];
  if (days.length === 0) return null;
  const series = days[0].series_name_en || days[0].series_name;

  return (
    <section className="ov2-card flex flex-col gap-4" data-module="official">
      <div className="flex flex-wrap items-baseline gap-2.5">
        <span className="ov2-card__title">🏅 Official awards</span>
        <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }} dir="auto">
          {series}
        </span>
      </div>
      {days.map((d, i) => (
        <React.Fragment key={d.competition_id}>
          {i > 0 && <hr className="border-0 border-t" style={{ borderColor: 'var(--m-hairline)' }} />}
          <DayBlock day={d} showDay={days.length > 1 || d.day_number != null} />
        </React.Fragment>
      ))}
    </section>
  );
}
