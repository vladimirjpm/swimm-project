import React from 'react';
import type { RsvpNote, TrainingLanePerson, TrainingLaneView } from '../types';

/**
 * Общие кирпичи вида по дорожкам на занятие (хендофф group-club-changes §6, вариант 3b; план
 * docs/plans/entity-hero-roles-plan.md §5, Ш3.3): кружок человека `PoolCircle`, подпись источника
 * раскладки `sourceCaption` и быстрые заметки к ответу `RsvpNotes`. Сами бассейны — `trainings/next/lane-block.tsx`
 * (участник, Т5; тренер, Т6); раскладку считает сервер (`lane_view` в личном ответе RSVP).
 *
 * Цвета — роли темы: вода `--deep-pool-*`, иду `--deep-live`, не уверен `--deep-gold*`, не приду
 * `--deep-danger`; пол — `--deep-male` / `--deep-female`. Свой кружок — с кольцом `--deep-live-border`.
 */

const NOTE_LABEL: Record<RsvpNote, string> = {
  late: 'Late ~10 min', 'first-hour': '1st hour only', 'leaving-early': 'Leaving early',
};
const NOTES: RsvpNote[] = ['late', 'first-hour', 'leaving-early'];

/** Подпись источника раскладки: «Coach's plan» / «Who is in the water» / «Auto · by level». */
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

/**
 * Кружок 26px (или 20px в мини-бассейне вида Next, `size={20}`): сплошной — идёт, пунктир
 * золотом — не уверен; свой — с кольцом.
 */
function PoolCircle({ p, size = 26 }: { p: TrainingLanePerson; size?: 20 | 26 }) {
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
        className={`flex items-center justify-center rounded-full font-extrabold ${size === 20 ? 'h-[20px] w-[20px] text-[9px]' : 'h-[26px] w-[26px] text-[10.5px]'}`}
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

export { RsvpNotes, PoolCircle, sourceCaption };
