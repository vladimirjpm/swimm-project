import React, { useMemo, useState } from 'react';
import {
  DeepSheet, SheetButton, SheetError, SheetFooter, SHEET_MONO_FIELD_CLS,
} from '../../../../components/deep/deep-sheet';
import { todayInIsrael } from '../../lane-plans-api';
import { useTrainingsTab } from '../trainings-context';
import type { TrainingsSheetProps } from '../trainings-sheets';
import { addDays, addOneMonth, formatSheetDate, isoWeekday } from './link-helpers';

/**
 * «Taking a break» (тон accent): участник остаётся в группе, его просто не спрашивают.
 *
 * ⚠ Семантика сервера: `until` — ПОСЛЕДНИЙ день перерыва (включительно), а «Back on» — день
 * возвращения, поэтому `until = backOn − 1 день`. Сервер не принимает `until` дальше 366 дней
 * от сегодня (`HubGroupBreakRules.MaxDaysAhead`) → «Pick date» ограничен сегодня + 367 по
 * «Back on». Даты — строки `yyyy-MM-dd` по Израилю, считаем в UTC.
 */

type Preset = 'week' | 'weeks' | 'month' | 'pick';

const PRESETS: { id: Preset; label: string }[] = [
  { id: 'week', label: '1 week' },
  { id: 'weeks', label: '2 weeks' },
  { id: 'month', label: '1 month' },
  { id: 'pick', label: 'Pick date' },
];

const MAX_DAYS_AHEAD = 366;

function BreakSheet({ onClose }: TrainingsSheetProps<'break'>) {
  const { group, breaks } = useTrainingsTab();
  const today = todayInIsrael();
  const tomorrow = addDays(today, 1);
  const maxBackOn = addDays(today, MAX_DAYS_AHEAD + 1);

  const [preset, setPreset] = useState<Preset>('weeks');
  const [picked, setPicked] = useState(addDays(today, 7));
  const [error, setError] = useState<string | null>(null);

  const backOn = preset === 'week' ? addDays(today, 7)
    : preset === 'weeks' ? addDays(today, 14)
      : preset === 'month' ? addOneMonth(today)
        : picked;
  const backOnValid = /^\d{4}-\d{2}-\d{2}$/.test(backOn) && backOn >= tomorrow && backOn <= maxBackOn;
  const until = addDays(backOn, -1);

  // Сколько занятий расписания пропускает перерыв: с завтра по `until` включительно.
  const slots = group.training_schedule?.slots;
  const skipped = useMemo(() => {
    if (!slots || slots.length === 0 || !backOnValid) return null;
    let n = 0;
    for (let d = tomorrow; d <= until; d = addDays(d, 1)) {
      const wd = isoWeekday(d);
      n += slots.filter((s) => s.day === wd).length;
    }
    return n;
  }, [slots, backOnValid, tomorrow, until]);

  const start = async () => {
    if (!backOnValid || breaks.busy) return;
    setError(null);
    const e = await breaks.setBreak({ on_break: true, until });
    if (e) setError(e);
    else onClose();
  };

  return (
    <DeepSheet
      title="Taking a break"
      sub="You stay in the group; you just won't be asked"
      tone="accent"
      busy={breaks.busy}
      onClose={onClose}
      onSubmit={() => { void start(); }}
      footer={(
        <div className="flex flex-col gap-3.5">
          <SheetError error={error} />
          <SheetFooter>
            <SheetButton tone="accent" disabled={breaks.busy || !backOnValid}>Start break</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <div className="flex flex-col gap-[5px]">
        <span className="text-[11px] font-extrabold" style={{ color: 'var(--t-text-3)' }}>How long</span>
        <div className="grid grid-cols-4 gap-2" role="group" aria-label="How long">
          {PRESETS.map((p) => {
            const on = preset === p.id;
            return (
              <button
                key={p.id}
                type="button"
                aria-pressed={on}
                onClick={() => setPreset(p.id)}
                className="h-11 cursor-pointer border px-1 text-[12px] font-extrabold"
                style={{
                  borderRadius: 10,
                  background: on ? 'var(--deep-accent-chip)' : 'transparent',
                  borderColor: on ? 'var(--deep-accent-border)' : 'var(--deep-card-border)',
                  color: on ? 'var(--deep-accent)' : 'var(--t-text-2)',
                }}
              >
                {p.label}
              </button>
            );
          })}
        </div>
        {preset === 'pick' && (
          <input
            type="date"
            value={picked}
            min={tomorrow}
            max={maxBackOn}
            onChange={(e) => setPicked(e.target.value)}
            aria-label="Back on"
            className={`${SHEET_MONO_FIELD_CLS} mt-1.5`}
          />
        )}
      </div>

      <div
        className="flex items-center justify-between border"
        style={{ borderRadius: 12, padding: '12px 14px', background: 'var(--deep-card-bg)', borderColor: 'var(--deep-card-border)' }}
      >
        <span className="text-[12px]" style={{ color: 'var(--t-text-3)' }}>Back on</span>
        <span className="hp-mono text-[15px] font-extrabold" style={{ color: 'var(--t-text)' }}>
          {backOnValid ? formatSheetDate(backOn) : '—'}
        </span>
      </div>

      <div className="flex flex-col gap-1 text-[11.5px] font-semibold" style={{ color: 'var(--t-text-3)' }}>
        <p className="m-0">
          {skipped != null ? `Skips ${skipped} ${skipped === 1 ? 'training' : 'trainings'} · you` : 'You'}
          {' '}won&apos;t count as &quot;no answer&quot;.
        </p>
        <p className="m-0">Answering &quot;Going&quot; before that ends the break.</p>
      </div>
    </DeepSheet>
  );
}

export default BreakSheet;
