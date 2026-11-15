import React from 'react';
import WorkoutLineLabel from './workout-line-label';
import { LevelRank, levelCellStyle, type WorkoutLevel } from './workout-level-tag';
import { reasonHint, type ParsedWorkout } from './workout-parse';
import { formatKm, kmRange, levelMeters } from './workout-totals';

/**
 * Распознанное из вставленного текста (CF7, макет F5 справа) — в том же виде, что таблица CF6, только
 * для чтения. Подсветка мест для проверки — золото (`--deep-gold*`, как «Needs you»): строка с
 * меткой — золотая подложка и подсказка в `title`; достроенная по шагу ячейка — пунктир золотом;
 * сет «только для части уровней» и несошедшаяся сумма листа — золотой чип у названия.
 */

const GOLD_ROW: React.CSSProperties = { background: 'var(--deep-gold-soft)', boxShadow: 'inset 3px 0 0 var(--deep-gold-bar)' };
const GOLD_CHIP: React.CSSProperties = { background: 'var(--deep-gold-chip)', border: '1px solid var(--deep-gold-border)', color: 'var(--deep-gold)' };

function WorkoutPasteResult({ parsed, levels }: { parsed: ParsedWorkout; levels: WorkoutLevel[] }) {
  const { workout, lineMarks, setMarks, filledCells, totals } = parsed;
  const filled = new Set(filledCells);

  if (workout.sets.length === 0) {
    return (
      <p className="m-0 text-[12.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
        Paste the plan on the left — sets and lines show up here.
      </p>
    );
  }

  return (
    <div className="flex min-w-0 flex-col gap-3">
      {levels.length > 0 && (
        <div className="flex flex-wrap items-center justify-end gap-2.5" aria-hidden="true">
          {levels.map((l) => <LevelRank key={l.id} level={l} size={16} />)}
        </div>
      )}
      {workout.sets.map((set, s) => {
        const marks = setMarks[s] ?? [];
        const total = totals.find((t) => t.afterSet === s);
        return (
          <div key={s} className="flex min-w-0 flex-col gap-1">
            <div className="flex min-w-0 flex-wrap items-center gap-1.5">
              <span dir="auto" className="min-w-0 text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
                {set.title || `Set ${s + 1}`}
              </span>
              {marks.includes('levels') && (
                <span className="rounded-full px-2 py-[1px] text-[10.5px] font-extrabold" style={GOLD_CHIP} title="The sheet names only some levels — untick the others in the table">
                  Some levels only?
                </span>
              )}
            </div>
            {set.lines.map((line, l) => {
              const lm = lineMarks[`${s}:${l}`];
              return (
                <div
                  key={l}
                  className="flex min-w-0 flex-wrap items-center justify-between gap-x-3 gap-y-1 rounded-[8px] px-1.5 py-1"
                  style={lm ? GOLD_ROW : undefined}
                  title={lm ? reasonHint(lm) : undefined}
                >
                  <span className="min-w-0 flex-1 break-words text-[12.5px] font-bold" style={{ color: 'var(--deep-text)', flexBasis: 160 }}>
                    {lm && <span aria-label="Check" style={{ color: 'var(--deep-gold)' }}>⚠ </span>}
                    <WorkoutLineLabel line={line} />
                  </span>
                  {line.per_level && levels.length > 0 && (
                    <span className="flex flex-none gap-1">
                      {levels.map((level) => {
                        const cell = line.per_level?.[String(level.id)];
                        const dashed = filled.has(`${s}:${l}:${level.id}`);
                        return (
                          <span
                            key={level.id}
                            className="hp-mono inline-flex h-7 min-w-[46px] items-center justify-center rounded-[7px] px-1 text-[11.5px] font-extrabold"
                            style={levelCellStyle(level, !cell, dashed)}
                            title={dashed ? `${level.name}: continued with the same step — check` : level.name}
                          >
                            {cell?.reps != null ? `${cell.reps}× ` : ''}{cell?.send_off ?? 'off'}
                          </span>
                        );
                      })}
                    </span>
                  )}
                </div>
              );
            })}
            {total && (
              <div
                className="mt-0.5 self-start rounded-full px-2 py-[1px] text-[10.5px] font-extrabold"
                style={total.ok ? { color: 'var(--deep-live)', border: '1px solid var(--deep-live-border)' } : GOLD_CHIP}
                title={total.ok ? 'The sheet total matches' : 'The sheet total differs from the lines — check reps and sets'}
              >
                Sheet: {total.label} km{total.ok ? ' ✓' : ` · counted ${countedLabel(parsed, s, levels)}`}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** Сколько насчитали к отметке листа: разброс по уровням. */
function countedLabel(parsed: ParsedWorkout, afterSet: number, levels: WorkoutLevel[]): string {
  const upTo = { sets: parsed.workout.sets.slice(0, afterSet + 1) };
  const ids = levels.length > 0 ? levels.map((l) => l.id) : [null];
  return kmRange(ids.map((id) => levelMeters(upTo, id))) ?? formatKm(0);
}

export default WorkoutPasteResult;
