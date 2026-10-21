import React from 'react';
import type { NextTraining, TrainingLane, TrainingLanePerson, TrainingRsvp } from '../../../types';
import { PoolCircle, sourceCaption } from '../../group-pool';
import { formatNextDate } from '../../group-training-slots';
import { useTrainingsTab } from '../trainings-context';
import { MarkDot } from '../trainings-section';
import {
  dayNumLabel, desktopKicker, timeRangeLabel, untilLabel, workoutOneLine,
} from './next-format';
import { CancelledPill } from './session-tag';

/**
 * Ближайшее занятие участника: шапка, «Your lane», бассейн и счётчики (Т5, хендофф Trainings Tab,
 * 2a / 3a). Данные — готовый `lane_view` личного ответа RSVP (раскладку считает сервер); здесь
 * только рисуем. Телефон — мини-бассейн (дорожка 32px, кружки 20px) целиком кнопкой «Open lanes»;
 * десктоп — полный бассейн (сетка «дорожка · задание · люди · YOU») и ссылка «Lanes ›».
 */

/** Своя дорожка: та, где среди `people` есть `is_me`. */
function myLane(lanes: TrainingLane[]): TrainingLane | null {
  return lanes.find((l) => l.people.some((p) => p.is_me)) ?? null;
}

/** Подпись дорожки: «2 · medium» (уровень в нижнем регистре, если он есть) либо «2». */
function laneLabel(lane: TrainingLane): string {
  return lane.level ? `${lane.lane_no} · ${lane.level.name.toLowerCase()}` : String(lane.lane_no);
}

function personKey(p: TrainingLanePerson, i: number): string {
  return p.user_id != null ? `u${p.user_id}` : p.swimmer_id != null ? `s${p.swimmer_id}` : `hidden-${i}`;
}

/** Ответ на ближайшее занятие, если он пришёл именно на него. */
export function useNextRsvp(): { next: NextTraining | null; r: TrainingRsvp | null } {
  const { group, rsvp } = useTrainingsTab();
  const next = group.next_training ?? group.next_trainings?.[0] ?? null;
  const r = next && rsvp?.rsvp && rsvp.rsvp.session_id === next.id ? rsvp.rsvp : null;
  return { next, r };
}

/** Число жирным цветом роли + подпись обычным: «**5** going». */
function Count({ n, color, label }: { n: number; color: string; label: string }) {
  return (
    <span>
      <b style={{ color }}>{n}</b> {label}
    </span>
  );
}

/** Счётчики десктопа: «5 in · 1 maybe · 2 out». */
function DesktopCounts({ r }: { r: TrainingRsvp }) {
  return (
    <div className="hp-mono flex flex-none flex-wrap items-center gap-x-1.5 text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>
      <Count n={r.yes} color="var(--deep-live)" label="in" />
      <span aria-hidden="true">·</span>
      <Count n={r.maybe} color="var(--deep-gold)" label="maybe" />
      <span aria-hidden="true">·</span>
      <Count n={r.no} color="var(--deep-danger)" label="out" />
    </div>
  );
}

/** Шапка занятия: телефон — «Wed 30 · 20:00» + отсчёт; десктоп (3a) — кикер, крупная дата, место, счётчики. */
export function NextHeader({
  variant, next, r,
}: {
  variant: 'phone' | 'desktop';
  next: NextTraining;
  r: TrainingRsvp | null;
}) {
  const { group } = useTrainingsTab();
  const until = untilLabel(next.date, next.start, next.end);
  const times = timeRangeLabel(next.start, next.end);

  // Р9: отменённое занятие (ответ пришёл с `cancelled`, список ещё не перечитан) — «Cancelled» вместо отсчёта.
  const cancelled = next.cancelled === true || r?.cancelled === true;

  if (variant === 'phone') {
    return (
      <div className="flex items-baseline justify-between gap-3">
        <div className="min-w-0 whitespace-nowrap">
          <span className="text-[15px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{dayNumLabel(next.date)} · </span>
          <span className="hp-mono text-[15px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{times}</span>
        </div>
        {cancelled ? <CancelledPill /> : until && (
          <span className="flex-none text-[10.5px] font-extrabold" style={{ color: 'var(--deep-live)' }}>{until}</span>
        )}
      </div>
    );
  }

  const place = group.training_schedule?.place ?? next.place ?? null;
  const pool = group.training_schedule?.pool_type ?? next.pool_type ?? null;
  return (
    <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
      <div className="min-w-0">
        <div
          className="flex items-center gap-1.5 text-[10px] font-extrabold uppercase tracking-[.07em]"
          style={{ color: 'var(--deep-live)' }}
        >
          <span aria-hidden="true" className="inline-block flex-none rounded-full" style={{ width: 7, height: 7, background: 'var(--deep-live)' }} />
          {cancelled ? <CancelledPill /> : desktopKicker(next.date, next.start, next.end)}
        </div>
        <div className="mt-1 flex flex-wrap items-baseline gap-x-1" style={{ color: 'var(--deep-text)' }}>
          <span className="text-[22px]" style={{ fontFamily: 'var(--deep-font-display)' }}>{formatNextDate(next.date)} · </span>
          <span className="hp-mono text-[20px] font-extrabold">{times}</span>
        </div>
        {(place || pool) && (
          <div className="mt-0.5 text-[12.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
            {place && <bdi>{place}</bdi>}
            {pool && <>{place ? ' · ' : ''}{pool}</>}
          </div>
        )}
      </div>
      {r && <DesktopCounts r={r} />}
    </div>
  );
}

/**
 * «YOUR LANE»: дорожка и задание либо подсказка, как её получить. Нет вида по дорожкам (или
 * источник «вода» — дорожек не знаем) — блока нет.
 */
export function YourLane({ r }: { r: TrainingRsvp }) {
  const view = r.lane_view;
  if (!view || view.source === 'water') return null;
  const lane = myLane(view.lanes);
  const workout = lane ? workoutOneLine(lane.workout) : null;
  const answer = r.mine?.answer ?? null;

  return (
    <div className="flex min-w-0 flex-col gap-1">
      {lane ? (
        <>
          <div className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--deep-pool-label)' }}>
            Your lane
          </div>
          <div className="text-[18px]" style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}>
            {laneLabel(lane)}
          </div>
          {workout && (
            <div dir="auto" className="hp-mono text-[12px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{workout}</div>
          )}
        </>
      ) : (
        <p className="m-0 text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>
          {answer === 'yes' || answer === 'maybe'
            ? 'You’re on the list — the coach hasn’t picked a lane yet.'
            : 'Answer “Going” or “Not sure” to get a lane.'}
        </p>
      )}
    </div>
  );
}

function MiniRow({
  label, people, mine, first,
}: {
  label: string;
  people: TrainingLanePerson[];
  mine: boolean;
  first: boolean;
}) {
  return (
    <div
      className="flex min-h-[32px] items-center gap-2 px-2.5 py-1"
      style={{
        borderTop: first ? undefined : '1px dashed var(--deep-pool-rope)',
        background: mine ? 'var(--deep-accent-chip)' : undefined,
      }}
    >
      <span className="hp-mono w-[76px] flex-none truncate text-[10px] font-extrabold" style={{ color: 'var(--deep-pool-label)' }} title={label}>
        {label}
      </span>
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-1">
        {people.map((p, i) => <PoolCircle key={personKey(p, i)} p={p} size={20} />)}
      </div>
    </div>
  );
}

/**
 * Мини-бассейн телефона: вся «вода» — кнопка «Open lanes». `footer` (вид тренера, Т6) — подвал
 * внутри той же рамки, но ВНЕ кнопки: в нём свои кнопки («Lanes ›»), вложить их в «Open lanes» нельзя.
 */
export function MiniPool({ r, footer }: { r: TrainingRsvp; footer?: React.ReactNode }) {
  const { openLanes } = useTrainingsTab();
  const view = r.lane_view;
  if (!view) return null;
  const open = () => openLanes(r.date);
  const water = view.source === 'water';

  return (
    <div
      className="overflow-hidden rounded-[12px] border"
      style={{ borderColor: 'var(--deep-pool-border)', background: 'var(--deep-pool-water)' }}
    >
      <div
        role="button"
        tabIndex={0}
        aria-label="Open lanes"
        onClick={open}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); } }}
        className="cursor-pointer"
      >
        {water ? (
          <MiniRow label="pool" people={view.lanes.flatMap((l) => l.people)} mine={false} first />
        ) : (
          view.lanes.map((lane, i) => (
            <MiniRow
              key={lane.lane_no}
              label={laneLabel(lane)}
              people={lane.people}
              mine={lane.people.some((p) => p.is_me)}
              first={i === 0}
            />
          ))
        )}
        {view.no_lane.length > 0 && (
          <MiniRow label="no lane" people={view.no_lane} mine={view.no_lane.some((p) => p.is_me)} first={water ? false : view.lanes.length === 0} />
        )}
      </div>
      {footer}
    </div>
  );
}

/** Под мини-бассейном: «5 going · 1 maybe» слева, «Lanes ›» справа. */
export function PoolFooter({ r }: { r: TrainingRsvp }) {
  const { openLanes, marks } = useTrainingsTab();
  return (
    <div className="flex items-center justify-between gap-3">
      <div className="hp-mono flex flex-wrap items-center gap-x-1.5 text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>
        <Count n={r.yes} color="var(--deep-live)" label="going" />
        <span aria-hidden="true">·</span>
        <Count n={r.maybe} color="var(--deep-gold)" label="maybe" />
      </div>
      <button
        type="button"
        onClick={() => openLanes(r.date)}
        className="inline-flex cursor-pointer items-center gap-1.5 border-0 bg-transparent p-0 text-[12px] font-extrabold"
        style={{ color: 'var(--deep-accent)' }}
      >
        Lanes ›
        {marks.lanes && <MarkDot mark={{ ...marks.lanes, count: undefined }} />}
      </button>
    </div>
  );
}

const FULL_GRID = '110px 170px minmax(0, 1fr) 40px';
/** Вид тренера: колонки YOU нет. */
const FULL_GRID_COACH = '110px 170px minmax(0, 1fr)';

function FullRow({
  label, workout, people, mine, first, showMe = true,
}: {
  label: string;
  workout: string | null;
  people: TrainingLanePerson[];
  mine: boolean;
  first: boolean;
  showMe?: boolean;
}) {
  return (
    <div
      className="grid min-h-[46px] items-center gap-2.5 px-3.5 py-1.5"
      style={{
        gridTemplateColumns: showMe ? FULL_GRID : FULL_GRID_COACH,
        borderTop: first ? undefined : '1px dashed var(--deep-pool-rope)',
        background: mine && showMe ? 'var(--deep-accent-chip)' : undefined,
      }}
    >
      <span className="hp-mono truncate text-[11px] font-extrabold" style={{ color: 'var(--deep-pool-label)' }} title={label}>{label}</span>
      <span dir="auto" className="hp-mono truncate text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }} title={workout ?? undefined}>
        {workout ?? ''}
      </span>
      <div className="flex min-w-0 flex-wrap items-center gap-[5px]">
        {people.map((p, i) => <PoolCircle key={personKey(p, i)} p={p} size={26} />)}
      </div>
      {showMe && (
        <span className="text-right text-[10px] font-extrabold" style={{ color: 'var(--deep-accent)' }}>{mine ? 'YOU' : ''}</span>
      )}
    </div>
  );
}

/**
 * Полный бассейн десктопа: подпись источника + «Lanes ›» сверху, дорожки сеткой.
 * `coach` (Т6): без своей шапки (над бассейном у тренера строка статуса плана и «✎ Edit lanes»)
 * и без колонки YOU / подсветки «моей» дорожки.
 */
export function FullPool({ r, coach = false }: { r: TrainingRsvp; coach?: boolean }) {
  const { openLanes, marks } = useTrainingsTab();
  const view = r.lane_view;
  if (!view) return null;
  const water = view.source === 'water';

  return (
    <div className="flex flex-col gap-2">
      {!coach && (
        <div className="flex items-baseline justify-between gap-3">
          <span className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
            Lanes · {sourceCaption(view)}
            {/* Вид строится только по опубликованному плану — «published» зелёным, как в макете. */}
            {view.source === 'plan' && <> · <span style={{ color: 'var(--deep-live)' }}>published</span></>}
          </span>
          <button
            type="button"
            onClick={() => openLanes(r.date)}
            className="inline-flex cursor-pointer items-center gap-1.5 border-0 bg-transparent p-0 text-[12px] font-extrabold"
            style={{ color: 'var(--deep-accent)' }}
          >
            Lanes ›
            {marks.lanes && <MarkDot mark={{ ...marks.lanes, count: undefined }} />}
          </button>
        </div>
      )}
      <div
        className="overflow-hidden rounded-[12px] border"
        style={{ borderColor: 'var(--deep-pool-border)', background: 'var(--deep-pool-water)' }}
        role="list"
        aria-label="Lanes"
      >
        {water ? (
          <div role="listitem">
            <FullRow label="pool" workout={null} people={view.lanes.flatMap((l) => l.people)} mine={false} first showMe={!coach} />
          </div>
        ) : (
          view.lanes.map((lane, i) => (
            <div role="listitem" key={lane.lane_no}>
              <FullRow
                label={laneLabel(lane)}
                workout={workoutOneLine(lane.workout)}
                people={lane.people}
                mine={lane.people.some((p) => p.is_me)}
                first={i === 0}
                showMe={!coach}
              />
            </div>
          ))
        )}
        {view.no_lane.length > 0 && (
          <div role="listitem">
            <FullRow
              label="no lane"
              workout={null}
              people={view.no_lane}
              mine={view.no_lane.some((p) => p.is_me)}
              first={water ? false : view.lanes.length === 0}
              showMe={!coach}
            />
          </div>
        )}
      </div>
    </div>
  );
}
