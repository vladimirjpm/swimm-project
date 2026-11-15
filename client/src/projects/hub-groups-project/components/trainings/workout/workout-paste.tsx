import React, { useMemo, useState } from 'react';
import { SHEET_TEXTAREA_CLS } from '../../../../components/deep/deep-sheet';
import ConfirmDialog from '../../../../components/confirm-dialog/confirm-dialog';
import type { Workout } from '../../../types';
import type { WorkoutLevel } from './workout-level-tag';
import { checkSummary, parseWorkoutText } from './workout-parse';
import WorkoutPasteResult from './workout-paste-result';

/**
 * «Paste text» редактора workout (docs/plans/trainings-coach-flow-plan.md, §5.6, CF7; макет F5): слева
 * текст тренера с листа (Word, WhatsApp), рядом — распознанное в виде таблицы только для чтения, над
 * ним одна строка «сколько мест проверить». Разбор — `parseWorkoutText` на клиенте, на лету.
 *
 * Кнопка переводит результат в таблицу CF6. Workout не пустой — `ConfirmDialog`: заменить или
 * добавить в конец. Исходный текст держит редактор (`text`), пока тренер не подтвердил: ушёл в
 * таблицу по «← Back to the table» и вернулся — текст на месте.
 */
function WorkoutPaste({
  text, onText, levels, current, desktop, onApply, onClose,
}: {
  text: string;
  onText: (text: string) => void;
  /** Уровни группы по рангу: времена листа раскладываются сверху вниз. */
  levels: WorkoutLevel[];
  current: Workout;
  desktop: boolean;
  /** Подтверждено: новый workout целиком (замена или с добавленными в конец сетами). */
  onApply: (workout: Workout) => void;
  onClose: () => void;
}) {
  const levelIds = useMemo(() => levels.map((l) => l.id), [levels]);
  const parsed = useMemo(() => parseWorkoutText(text, levelIds), [text, levelIds]);
  const summary = checkSummary(parsed);
  const [asking, setAsking] = useState(false);
  const [mode, setMode] = useState<'replace' | 'append'>('replace');
  const empty = parsed.workout.sets.length === 0;

  const apply = (how: 'replace' | 'append') => onApply(how === 'replace'
    ? { note: current.note ?? null, sets: parsed.workout.sets }
    : { ...current, sets: [...current.sets, ...parsed.workout.sets] });

  const confirm = () => {
    if (current.sets.length > 0) setAsking(true);
    else apply('replace');
  };

  const source = (
    <div className="flex min-w-0 flex-col gap-1.5">
      <span className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>Your text</span>
      <textarea
        value={text}
        onChange={(e) => onText(e.target.value)}
        dir="auto"
        spellCheck={false}
        aria-label="Workout text from your sheet"
        placeholder="Paste the workout here…"
        className={`${SHEET_TEXTAREA_CLS} resize-y leading-[1.55]`}
        style={{ height: desktop ? 420 : 220 }}
      />
      <span className="text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
        Paste the plan the way you write it. Times separated by “|” or after “יציאה” go to the levels from the top one down.
      </span>
    </div>
  );

  const result = (
    <div className="flex min-w-0 flex-col gap-2.5 rounded-[14px] p-3.5" style={{ background: 'var(--deep-card-bg-row)', border: '1px solid var(--deep-card-border)' }}>
      <span className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>Recognised</span>
      {summary && (
        <p className="m-0 rounded-[10px] px-3 py-2 text-[12px] font-extrabold" role="status"
          style={{ background: 'var(--deep-gold-chip)', border: '1px solid var(--deep-gold-border)', color: 'var(--deep-gold)' }}>
          ⚠ {summary}
        </p>
      )}
      <WorkoutPasteResult parsed={parsed} levels={levels} />
    </div>
  );

  return (
    <div className="flex min-w-0 flex-col gap-3.5">
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
        <div className="min-w-0">
          <div className="text-[18px]" style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}>Paste from your sheet</div>
          <div className="text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>Next you can correct anything in the table.</div>
        </div>
      </div>
      {desktop ? (
        <div className="grid items-start gap-3.5" style={{ gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1.25fr)' }}>
          {source}
          {result}
        </div>
      ) : (
        <>
          {source}
          {result}
        </>
      )}
      <div className="flex flex-wrap justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          className="h-11 cursor-pointer rounded-[12px] border px-4 text-[13px] font-extrabold"
          style={{ borderColor: 'var(--deep-card-border)', background: 'transparent', color: 'var(--t-text)' }}
        >
          Back to the table
        </button>
        <button
          type="button"
          onClick={confirm}
          disabled={empty}
          className="h-11 cursor-pointer rounded-[12px] border-0 px-4 text-[13px] font-extrabold disabled:cursor-not-allowed disabled:opacity-50"
          style={{ background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }}
        >
          Looks right — open the table ›
        </button>
      </div>

      {asking && (
        <ConfirmDialog
          title="The workout already has sets"
          confirmLabel={mode === 'replace' ? 'Replace' : 'Add to the end'}
          onConfirm={async () => { setAsking(false); apply(mode); return { success: true }; }}
          onClose={() => setAsking(false)}
        >
          <div className="mt-3 flex flex-col gap-2 text-[13px] font-bold" style={{ color: 'var(--t-text-2)' }} role="radiogroup">
            {([
              ['replace', `Replace all ${current.sets.length} ${current.sets.length === 1 ? 'set' : 'sets'} with the pasted ones`],
              ['append', 'Add the pasted sets after the current ones'],
            ] as const).map(([value, label]) => (
              <label key={value} className="flex cursor-pointer items-center gap-2">
                <input
                  type="radio"
                  name="workout-paste-mode"
                  checked={mode === value}
                  onChange={() => setMode(value)}
                  style={{ accentColor: 'var(--deep-ow)' }}
                />
                {label}
              </label>
            ))}
          </div>
        </ConfirmDialog>
      )}
    </div>
  );
}

export default WorkoutPaste;
