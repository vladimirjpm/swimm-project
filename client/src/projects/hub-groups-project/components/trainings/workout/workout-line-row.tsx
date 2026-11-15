import React from 'react';
import { SHEET_FIELD_BASE_CLS } from '../../../../components/deep/deep-sheet';
import type { WorkoutCell, WorkoutLine } from '../../../types';
import CommitInput from './commit-input';
import { cellLabel, parseCellLabel, parseRepsDistance, repsDistanceLabel } from './workout-format';
import { LevelTag, levelCellStyle, type WorkoutLevel } from './workout-level-tag';
import { fillByStep } from './workout-time';

/**
 * Строка сета в таблице workout (CF6, макеты F6): «повторы × дистанция», текст, интенсивность и
 * ячейки уровней — время выхода строкой, в ней же своё число повторов («3× 2:10»). Ячейки колонки
 * окрашены бледным тоном цвета уровня; уровень сет не плывёт — «off».
 *
 * Заполнение по шагу (§3 решение 9): ушли из ячейки, а первые два времени строки введены — пустые
 * ячейки дальше продолжаются тем же шагом (`fillByStep`). Считаются только колонки уровней,
 * которые плывут этот сет.
 *
 * Десктоп — одна строка сетки `lineGrid` (той же сеткой стоят галочки над колонками в сете);
 * телефон — две строки: поля строки, под ними ячейки уровней с подписью над каждой.
 */

const FIELD = `${SHEET_FIELD_BASE_CLS} h-9 font-bold`;
const MONO = `${SHEET_FIELD_BASE_CLS} h-9 hp-mono font-extrabold text-center`;
/** Узкие поля: отступ меньше, чем у поля шторки (px-3 класса не перебить порядком утилит). */
const TIGHT: React.CSSProperties = { paddingInline: 4 };

/** Сетка десктопа: повторы×дистанция · текст · V · уровни · удалить. */
export function lineGrid(levelCount: number): string {
  return `68px minmax(120px, 1fr) 52px repeat(${levelCount}, minmax(52px, 80px)) 28px`;
}

function WorkoutLineRow({
  line, levels, active, desktop, onChange, onRemove,
}: {
  line: WorkoutLine;
  /** Уровни группы по рангу — колонки. */
  levels: WorkoutLevel[];
  /** Уровни, которые плывут сет: остальные ячейки выключены. */
  active: ReadonlySet<number>;
  desktop: boolean;
  onChange: (line: WorkoutLine) => void;
  onRemove: () => void;
}) {
  const cellOf = (id: number) => line.per_level?.[String(id)];

  const setCell = (levelId: number, raw: string) => {
    const per: Record<string, WorkoutCell> = { ...(line.per_level ?? {}) };
    const cell = parseCellLabel(raw);
    if (cell.send_off || cell.reps != null) per[String(levelId)] = cell;
    else delete per[String(levelId)];

    // По шагу — по колонкам уровней, которые плывут сет.
    const order = levels.filter((l) => active.has(l.id));
    const { values, filled } = fillByStep(order.map((l) => per[String(l.id)]?.send_off ?? ''));
    order.forEach((l, i) => {
      if (filled[i]) per[String(l.id)] = { ...(per[String(l.id)] ?? {}), send_off: values[i] };
    });
    onChange({ ...line, per_level: Object.keys(per).length > 0 ? per : null });
  };

  const repsDistance = (
    <CommitInput
      value={repsDistanceLabel(line.reps, line.distance)}
      onCommit={(raw) => {
        const parsed = parseRepsDistance(raw);
        if (parsed) onChange({ ...line, ...parsed });
      }}
      placeholder="4×100"
      aria-label="Reps × distance"
      className={MONO}
      style={TIGHT}
    />
  );
  const text = (
    <input
      value={line.text ?? ''}
      onChange={(e) => onChange({ ...line, text: e.target.value })}
      dir="auto"
      maxLength={500}
      placeholder="free, paddles…"
      aria-label="What to swim"
      className={FIELD}
    />
  );
  const intensity = (
    <input
      value={line.intensity ?? ''}
      onChange={(e) => onChange({ ...line, intensity: e.target.value })}
      maxLength={12}
      placeholder="—"
      aria-label="Intensity, e.g. 2V"
      title="Intensity zone, e.g. 2V or 2-3V"
      className={MONO}
      style={TIGHT}
    />
  );
  const remove = (
    <button
      type="button"
      onClick={onRemove}
      aria-label="Remove line"
      title="Remove line"
      className="h-9 w-7 cursor-pointer rounded-[8px] border-0 bg-transparent text-[15px] font-bold"
      style={{ color: 'var(--deep-text-mute)' }}
    >
      ×
    </button>
  );
  const cell = (level: WorkoutLevel) => {
    const on = active.has(level.id);
    return on ? (
      <CommitInput
        key={level.id}
        value={cellLabel(cellOf(level.id))}
        onCommit={(raw) => setCell(level.id, raw)}
        aria-label={`Send-off, ${level.name}`}
        title={`${level.name} · send-off; “3× 2:10” — own number of reps`}
        className={`${SHEET_FIELD_BASE_CLS} h-9 hp-mono text-center font-extrabold`}
        style={{ ...levelCellStyle(level), ...TIGHT }}
      />
    ) : (
      <span
        key={level.id}
        className="flex h-9 items-center justify-center rounded-[10px] text-[11px] font-bold"
        style={levelCellStyle(level, true)}
        title={`${level.name} skips this set`}
      >
        off
      </span>
    );
  };

  if (desktop) {
    return (
      <div className="grid items-center gap-1.5" style={{ gridTemplateColumns: lineGrid(levels.length) }}>
        {repsDistance}
        {text}
        {intensity}
        {levels.map(cell)}
        {remove}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1.5 border-t pt-2.5" style={{ borderColor: 'var(--deep-divider)' }}>
      <div className="grid items-center gap-1.5" style={{ gridTemplateColumns: '64px minmax(0, 1fr) 48px 28px' }}>
        {repsDistance}
        {text}
        {intensity}
        {remove}
      </div>
      {levels.length > 0 && (
        <div className="grid gap-1.5" style={{ gridTemplateColumns: `repeat(${levels.length}, minmax(0, 1fr))` }}>
          {levels.map((level) => (
            <div key={level.id} className="flex min-w-0 flex-col items-center gap-0.5">
              <LevelTag level={level} size={14} />
              {cell(level)}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default WorkoutLineRow;
