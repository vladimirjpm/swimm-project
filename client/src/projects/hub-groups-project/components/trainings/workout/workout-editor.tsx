import React, { useState } from 'react';
import { useMediaQuery } from '../../../../../hooks/useMediaQuery';
import { SHEET_TEXTAREA_CLS } from '../../../../components/deep/deep-sheet';
import ConfirmDialog from '../../../../components/confirm-dialog/confirm-dialog';
import type { Workout, WorkoutSet } from '../../../types';
import type { SessionWorkout } from './use-session-workout';
import type { WorkoutLevel } from './workout-level-tag';
import WorkoutPaste from './workout-paste';
import WorkoutPreview from './workout-preview';
import WorkoutSetCard from './workout-set-card';
import WorkoutTotalsRow from './workout-totals-row';

/**
 * Редактор workout занятия (docs/plans/trainings-coach-flow-plan.md, §5.5, CF6; макеты F6) — вид
 * рабочего стола во всю ширину с «← Back» (решение владельца 15.11.2026: как Lanes, не модал), и на
 * десктопе, и на телефоне. Слева сеты (раскрыт один), снизу итог по уровням; справа (уже 1200px —
 * ниже таблицы) превью «что получит дорожка».
 *
 * Кнопки Save нет: правки сохраняются сами (`use-session-workout.ts`), как дорожки (CF3). Удаление
 * сета со строками — через `ConfirmDialog`. «Paste text» (CF7) — вид вставки `workout-paste.tsx` вместо
 * таблицы; текст вставки живёт здесь, пока тренер не подтвердил.
 */

const newLine = () => ({ reps: 1, distance: null, text: null, intensity: null, per_level: null });
const newSet = (): WorkoutSet => ({ title: null, repeat: 1, level_ids: null, lines: [newLine()] });

/** В сете есть что терять: строки с дистанцией или текстом. */
const hasContent = (set: WorkoutSet) => !!set.title?.trim() || set.lines.some((l) => l.distance != null || !!l.text?.trim());

function WorkoutEditor({
  ws, levels, dateLabel, desktop, onBack,
}: {
  ws: SessionWorkout;
  levels: WorkoutLevel[];
  dateLabel: string;
  desktop: boolean;
  onBack: () => void;
}) {
  const { workout, setWorkout } = ws;
  const [open, setOpen] = useState<number | null>(workout.sets.length > 0 ? 0 : null);
  const [removing, setRemoving] = useState<number | null>(null);
  const [pasting, setPasting] = useState(false);
  // Превью справа — только когда таблице хватает ширины под колонки уровней; уже — под таблицей.
  const wide = useMediaQuery('(min-width: 1200px)');
  const [pasteText, setPasteText] = useState('');

  const patchSet = (i: number, set: WorkoutSet) =>
    setWorkout((w) => ({ ...w, sets: w.sets.map((s, j) => (j === i ? set : s)) }));
  const removeSet = (i: number) => {
    setWorkout((w) => ({ ...w, sets: w.sets.filter((_, j) => j !== i) }));
    setOpen((o) => (o == null || o === i ? null : o > i ? o - 1 : o));
  };
  const addSet = () => {
    setWorkout((w) => ({ ...w, sets: [...w.sets, newSet()] }));
    setOpen(workout.sets.length);
  };

  const status = ws.status === 'error' ? null : ws.status === 'saved' ? 'Saved' : 'Saving…';

  const header = (
    <div className="flex flex-col gap-2">
      <div>
        <button
          type="button"
          onClick={() => { void ws.flush(); onBack(); }}
          className="cursor-pointer border-0 bg-transparent p-0 text-[13px] font-extrabold"
          style={{ color: 'var(--t-text-3)' }}
        >
          ← Back to training
        </button>
      </div>
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
        <div className="min-w-0">
          <div className="text-[20px]" style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}>Workout · {dateLabel}</div>
          <div className="text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>
            One plan for the whole pool. Levels differ in send-off times and in which sets they swim.
          </div>
        </div>
        <div className="flex flex-none items-center gap-3">
          {status && <span className="text-[11.5px] font-extrabold" style={{ color: 'var(--t-text-3)' }} aria-live="polite">{status}</span>}
          {!pasting && (
            <button
              type="button"
              onClick={() => setPasting(true)}
              className="h-9 cursor-pointer rounded-[10px] border px-3.5 text-[12.5px] font-extrabold"
              style={{ borderColor: 'var(--deep-ow-border)', background: 'var(--deep-ow-chip)', color: 'var(--deep-ow)' }}
            >
              Paste text
            </button>
          )}
        </div>
      </div>
      {ws.status === 'error' && (
        <p className="m-0 flex flex-wrap items-center gap-2 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }} role="alert">
          {ws.error}
          <button type="button" onClick={ws.retry} className="cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold underline" style={{ color: 'var(--deep-danger)' }}>
            Retry
          </button>
        </p>
      )}
    </div>
  );

  const sets = (
    <div className="flex min-w-0 flex-col gap-2.5">
      <textarea
        value={workout.note ?? ''}
        onChange={(e) => setWorkout((w: Workout) => ({ ...w, note: e.target.value }))}
        dir="auto"
        maxLength={1000}
        placeholder="Note for everyone (optional) — e.g. warm-up together"
        aria-label="Workout note"
        className={SHEET_TEXTAREA_CLS}
        style={{ height: 44 }}
      />
      {workout.sets.length === 0 && (
        <p className="m-0 rounded-[12px] p-4 text-center text-[12.5px] font-bold" style={{ border: '1px dashed var(--deep-card-border)', color: 'var(--t-text-3)' }}>
          No sets yet. Add a set, or paste the plan the way you write it on the sheet.
        </p>
      )}
      {workout.sets.map((set, i) => (
        <WorkoutSetCard
          key={i}
          set={set}
          index={i}
          levels={levels}
          desktop={desktop}
          open={open === i}
          onToggle={() => setOpen((o) => (o === i ? null : i))}
          onChange={(s) => patchSet(i, s)}
          onRemove={() => (hasContent(set) ? setRemoving(i) : removeSet(i))}
        />
      ))}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <button
          type="button"
          onClick={addSet}
          className="h-10 cursor-pointer rounded-[10px] border px-4 text-[12.5px] font-extrabold"
          style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)', color: 'var(--deep-text)' }}
        >
          + Add set
        </button>
        <span className="text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
          Type the first two times in a line — the rest continue with the same step.
        </span>
      </div>
      <div className="pt-1">
        <WorkoutTotalsRow workout={workout} levels={levels} full={desktop} />
      </div>
    </div>
  );

  const preview = levels.length > 0 && workout.sets.length > 0
    ? <WorkoutPreview workout={workout} levels={levels} plan={ws.plan} />
    : null;

  return (
    // `--sheet-focus`: поля шторки вне шторки — рамка фокуса цветом тренера.
    <div className="flex min-w-0 flex-col gap-3.5" style={{ ['--sheet-focus' as string]: 'var(--deep-ow)' }}>
      {header}
      {pasting ? (
        <WorkoutPaste
          text={pasteText}
          onText={setPasteText}
          levels={levels}
          current={workout}
          desktop={desktop}
          onClose={() => setPasting(false)}
          onApply={(next) => {
            // Добавили в конец — раскрыт первый добавленный сет, заменили — первый.
            const appended = next.sets.length > workout.sets.length && next.sets[0] === workout.sets[0];
            setWorkout(() => next);
            setOpen(appended ? workout.sets.length : 0);
            setPasteText('');
            setPasting(false);
          }}
        />
      ) : desktop && wide ? (
        <div className="grid items-start gap-3.5" style={{ gridTemplateColumns: 'minmax(0, 1fr) 300px' }}>
          {sets}
          <div className="sticky top-3">{preview}</div>
        </div>
      ) : (
        <>
          {sets}
          {preview}
        </>
      )}
      {removing != null && (
        <ConfirmDialog
          title={`Remove ${workout.sets[removing]?.title?.trim() || `set ${removing + 1}`}?`}
          confirmLabel="Remove set"
          onConfirm={async () => { removeSet(removing); setRemoving(null); return { success: true }; }}
          onClose={() => setRemoving(null)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">Its lines and send-off times are removed from the workout.</p>
        </ConfirmDialog>
      )}
    </div>
  );
}

export default WorkoutEditor;
