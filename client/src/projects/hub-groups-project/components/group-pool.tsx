import React, { useState } from 'react';
import { todayInIsrael, formatPlanDate } from './lane-plans-api';
import type { RsvpAnswer, RsvpNote, TrainingLanePerson, TrainingLaneView } from '../types';

/**
 * Вид по дорожкам на занятие (хендофф group-club-changes §6, вариант 3b; план
 * docs/plans/entity-hero-roles-plan.md §5, Ш3.3): «вода» с дорожками, кружки идущих, счётчики,
 * переключатель ответа на три положения и быстрые заметки к ответу. Раскладку считает сервер
 * (`lane_view` в личном ответе RSVP) — здесь только рисуем.
 *
 * Цвета — роли темы: вода `--deep-pool-*`, иду `--deep-live`, не уверен `--deep-gold*`, не приду
 * `--deep-danger`; пол — `--deep-male` / `--deep-female`. Свой кружок — с кольцом `--deep-live-border`.
 */

/** Больше стольких на дорожке — остальные прячутся в кружок «+N» (хендофф: ~5). */
const MAX_PER_LANE = 5;

const NOTE_LABEL: Record<RsvpNote, string> = {
  late: 'Late ~10 min', 'first-hour': '1st hour only', 'leaving-early': 'Leaving early',
};
const NOTES: RsvpNote[] = ['late', 'first-hour', 'leaving-early'];

function sourceCaption(view: TrainingLaneView): string {
  if (view.source === 'plan') return "Coach's plan";
  if (view.source === 'water') return 'Who is in the water';
  return view.lanes.some((l) => l.level) ? 'Auto · by level' : 'Auto';
}

function personTitle(p: TrainingLanePerson): string {
  const parts = [p.name ?? 'Member', p.answer === 'maybe' ? 'not sure' : 'going'];
  if (p.note) parts.push(NOTE_LABEL[p.note].toLowerCase());
  if (p.claims > 1) parts.push(`${p.claims} accounts say this is them`);
  return parts.join(' · ');
}

/** Кружок 26px: сплошной — идёт, пунктир золотом — не уверен; свой — с кольцом. */
function PoolCircle({ p }: { p: TrainingLanePerson }) {
  const hidden = p.name == null;
  const genderBg = p.gender === 'female' ? 'var(--deep-female)' : p.gender === 'male' ? 'var(--deep-male)' : 'var(--deep-text-mute)';
  const maybe = p.answer === 'maybe';
  const style: React.CSSProperties = maybe
    ? {
      background: 'color-mix(in srgb, var(--deep-card-bg) 70%, transparent)',
      color: 'var(--deep-gold)',
      border: '2px dashed var(--deep-gold-bar)',
    }
    : {
      background: hidden ? 'var(--deep-text-ghost)' : genderBg,
      color: 'var(--deep-accent-ink)',
      border: '2px solid var(--deep-card-bg)',
    };
  if (p.is_me) style.boxShadow = '0 0 0 3px var(--deep-live-border)';
  else if (!maybe) style.boxShadow = '0 1px 3px rgba(8,60,84,.18)';

  return (
    <span className="relative flex-none" title={personTitle(p)}>
      <span
        className="flex h-[26px] w-[26px] items-center justify-center rounded-full text-[10.5px] font-extrabold"
        style={style}
      >
        {hidden ? '' : (p.name!.trim()[0] ?? '?').toUpperCase()}
      </span>
      {p.claims > 1 && (
        <span
          aria-label={`${p.claims} accounts claim this swimmer`}
          className="hp-mono absolute -right-1 -top-1 flex h-[13px] min-w-[13px] items-center justify-center rounded-full px-[2px] text-[8.5px] font-extrabold"
          style={{ background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }}
        >
          {p.claims}
        </span>
      )}
    </span>
  );
}

function LaneRow({
  label, people, rope, wrap,
}: {
  label: string;
  people: TrainingLanePerson[];
  rope: boolean;
  /** Одна общая «вода» — не режем «+N», а переносим строкой. */
  wrap?: boolean;
}) {
  const shown = wrap ? people : people.slice(0, MAX_PER_LANE);
  const rest = people.length - shown.length;
  return (
    <div
      className={`flex items-center gap-1.5 px-2.5 ${wrap ? 'min-h-[40px] flex-wrap py-[7px]' : 'h-10'}`}
      style={{ borderTop: rope ? '2px dashed var(--deep-pool-rope)' : undefined }}
    >
      <span
        className="hp-mono w-[46px] flex-none truncate text-[9.5px] font-extrabold"
        style={{ color: 'var(--deep-pool-label)' }}
        title={label}
      >
        {label}
      </span>
      {shown.map((p, i) => <PoolCircle key={p.user_id ?? `hidden-${i}`} p={p} />)}
      {rest > 0 && (
        <span
          className="hp-mono flex h-[26px] min-w-[26px] flex-none items-center justify-center rounded-full px-1 text-[10px] font-extrabold"
          style={{ background: 'var(--deep-card-bg)', color: 'var(--deep-text-mute)', border: '1px solid var(--deep-pool-border)' }}
          title={people.slice(MAX_PER_LANE).map((p) => p.name ?? 'Member').join(', ')}
        >
          +{rest}
        </span>
      )}
    </div>
  );
}

/**
 * Бассейн: дорожки 40px с пунктирными «канатами», подпись «1 · fast», полоса «no lane» для
 * тех, кого раскладка не поставила (нет уровня / план их не разложил), и счётчики под водой.
 */
function LanePool({ view, counts }: { view: TrainingLaneView; counts: { yes: number; maybe: number; no: number } }) {
  const water = view.source === 'water';
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
        <span className="text-[10px] font-extrabold uppercase tracking-[.08em]" style={{ color: 'var(--deep-text-mute)' }}>
          {sourceCaption(view)}
        </span>
        {view.names_hidden && (
          <span className="text-[11px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
            names are visible to coaches
          </span>
        )}
      </div>

      <div
        className="overflow-hidden rounded-[14px] border"
        style={{ borderColor: 'var(--deep-pool-border)', background: 'var(--deep-pool-water)' }}
        role="list"
        aria-label="Lanes"
      >
        {view.lanes.map((lane, i) => (
          <div role="listitem" key={lane.lane_no}>
            <LaneRow
              label={water ? 'pool' : lane.level ? `${lane.lane_no} · ${lane.level.name.toLowerCase()}` : String(lane.lane_no)}
              people={lane.people}
              rope={i > 0}
              wrap={water}
            />
          </div>
        ))}
        {view.no_lane.length > 0 && (
          <div role="listitem">
            <LaneRow label="no lane" people={view.no_lane} rope wrap />
          </div>
        )}
      </div>

      <div className="hp-mono flex justify-between text-[11px] font-extrabold">
        <span style={{ color: 'var(--deep-live)' }}>{counts.yes} in the water</span>
        <span style={{ color: 'var(--deep-gold)' }}>{counts.maybe} maybe</span>
        <span style={{ color: 'var(--deep-danger)' }}>{counts.no} out</span>
      </div>
    </div>
  );
}

const SEGMENT: { id: RsvpAnswer; label: string; fg: string; fill: string }[] = [
  { id: 'yes', label: '✓ Going', fg: 'var(--deep-live)', fill: 'var(--deep-live)' },
  { id: 'maybe', label: '? Not sure', fg: 'var(--deep-gold)', fill: 'var(--deep-gold-bar)' },
  { id: 'no', label: '✕ Can’t', fg: 'var(--deep-danger)', fill: 'var(--deep-danger)' },
];

/**
 * Переключатель ответа на три положения со скользящим ползунком (хендофф §6). Повторный тап
 * по выбранному снимает ответ (`null`), как у кнопок шапки.
 */
function RsvpSegment({
  current, onAnswer, disabled,
}: {
  current: RsvpAnswer | null;
  onAnswer: (next: RsvpAnswer | null) => void;
  disabled?: boolean;
}) {
  const idx = SEGMENT.findIndex((s) => s.id === current);
  return (
    <div
      className="relative grid h-11 grid-cols-3 rounded-[12px] border p-[3px]"
      style={{ background: 'var(--deep-card-bg)', borderColor: 'var(--deep-card-border)' }}
      role="group"
      aria-label="Are you coming?"
    >
      <span
        aria-hidden="true"
        className="absolute bottom-[3px] top-[3px] rounded-[9px] transition-[left,background-color] duration-200 ease-out motion-reduce:transition-none"
        style={{
          left: idx < 0 ? '3px' : `calc(3px + ${idx} * (100% - 6px) / 3)`,
          width: 'calc((100% - 6px) / 3)',
          background: idx < 0 ? 'transparent' : SEGMENT[idx].fill,
          opacity: idx < 0 ? 0 : 1,
        }}
      />
      {SEGMENT.map((s) => {
        const on = current === s.id;
        return (
          <button
            key={s.id}
            type="button"
            disabled={disabled}
            aria-pressed={on}
            onClick={() => onAnswer(on ? null : s.id)}
            className="relative cursor-pointer rounded-[9px] border-0 bg-transparent text-[12.5px] font-extrabold disabled:cursor-default disabled:opacity-50"
            style={{ color: on ? 'var(--deep-accent-ink)' : s.fg }}
          >
            {s.label}
          </button>
        );
      })}
    </div>
  );
}

/** Быстрые заметки к «иду / не уверен» — одна на выбор; повторный тап снимает. */
function RsvpNotes({
  current, onPick, disabled,
}: {
  current: RsvpNote | null;
  onPick: (next: RsvpNote | null) => void;
  disabled?: boolean;
}) {
  return (
    <div className="flex flex-wrap gap-1.5" role="group" aria-label="Add a note">
      {NOTES.map((n) => {
        const on = current === n;
        return (
          <button
            key={n}
            type="button"
            disabled={disabled}
            aria-pressed={on}
            onClick={() => onPick(on ? null : n)}
            className="h-[30px] cursor-pointer rounded-full border px-2.5 text-[11.5px] font-extrabold disabled:cursor-default disabled:opacity-50"
            style={on
              ? { background: 'var(--deep-accent-chip)', borderColor: 'var(--deep-accent-border)', color: 'var(--deep-accent)' }
              : { background: 'var(--deep-card-bg)', borderColor: 'var(--deep-card-border)', color: 'var(--deep-text-mute)' }}
          >
            {NOTE_LABEL[n]}
          </button>
        );
      })}
    </div>
  );
}

/** «+N дней» → «5 days» / «3 months» — пометка тренеру «back after …». */
function formatBreakLength(days: number): string {
  if (days < 45) return `${days} ${days === 1 ? 'day' : 'days'}`;
  const months = Math.round(days / 30);
  return `${months} ${months === 1 ? 'month' : 'months'}`;
}

/** Последний допустимый день перерыва — как у сервера (HubGroupBreakRules.MaxDaysAhead = 366). */
function maxBreakDate(today: string): string {
  const [y, m, d] = today.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + 366)).toISOString().slice(0, 10);
}

/**
 * Свой перерыв участника (Ш3.1, «травма, отпуск, армия»): до даты, после неё — снова в строю
 * сам. На перерыве — строка «On break until …» и «I'm back»; иначе — свёрнутое «Taking a break?».
 */
function MyBreakControl({
  until, onBreak, busy, onSet,
}: {
  until: string | null;
  onBreak: boolean;
  busy: boolean;
  onSet: (input: { on_break: boolean; until?: string | null }) => Promise<string | null>;
}) {
  const today = todayInIsrael();
  const [open, setOpen] = useState(false);
  const [date, setDate] = useState(today);
  const [error, setError] = useState<string | null>(null);

  const run = async (input: { on_break: boolean; until?: string | null }) => {
    setError(null);
    const e = await onSet(input);
    if (e) setError(e);
    else setOpen(false);
  };

  return (
    <div className="text-[12px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
      {onBreak ? (
        <div className="flex flex-wrap items-center gap-2">
          <span style={{ color: 'var(--deep-gold)' }}>
            ⏸ On break{until ? ` until ${formatPlanDate(until)}` : ''}
          </span>
          <button
            type="button"
            disabled={busy}
            onClick={() => run({ on_break: false })}
            className="cursor-pointer rounded-[8px] border bg-transparent px-2 py-[3px] text-[11.5px] font-extrabold disabled:opacity-50"
            style={{ borderColor: 'var(--deep-card-border)', color: 'var(--deep-accent)' }}
          >
            I&apos;m back
          </button>
        </div>
      ) : !open ? (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="cursor-pointer border-0 bg-transparent p-0 text-[12px] font-bold underline decoration-dotted underline-offset-2"
          style={{ color: 'var(--deep-text-mute)' }}
        >
          Taking a break?
        </button>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          <label htmlFor="my-break-until">Back after</label>
          <input
            id="my-break-until"
            type="date"
            value={date}
            min={today}
            max={maxBreakDate(today)}
            onChange={(e) => setDate(e.target.value)}
            className="rounded-[9px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2 py-[4px] text-[12px] text-[var(--t-text)]"
          />
          <button
            type="button"
            disabled={busy || !date}
            onClick={() => run({ on_break: true, until: date })}
            className="cursor-pointer rounded-[8px] border-0 px-2.5 py-[5px] text-[11.5px] font-extrabold disabled:opacity-50"
            style={{ background: 'var(--deep-accent)', color: 'var(--deep-accent-ink)' }}
          >
            Save
          </button>
          <button
            type="button"
            onClick={() => { setOpen(false); setError(null); }}
            className="cursor-pointer border-0 bg-transparent p-0 text-[11.5px] font-bold"
            style={{ color: 'var(--deep-text-mute)' }}
          >
            Cancel
          </button>
        </div>
      )}
      {error && <p className="m-0 mt-1 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }}>{error}</p>}
    </div>
  );
}

export { LanePool, RsvpSegment, RsvpNotes, MyBreakControl, formatBreakLength };
