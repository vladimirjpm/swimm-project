import React from 'react';
import { SHEET_FIELD_BASE_CLS } from '../../../../components/deep/deep-sheet';
import type { WorkoutSet } from '../../../types';
import { setAudience, setSummary } from './workout-format';
import { LevelTag, levelCellStyle, type WorkoutLevel } from './workout-level-tag';
import WorkoutLineLabel from './workout-line-label';
import WorkoutLineRow, { lineGrid } from './workout-line-row';
import { metersRange, setMeters } from './workout-totals';

/**
 * Сет в таблице workout (CF6, макеты F6): свёрнутый — строка «название · сводка · кому · метры ›»,
 * раскрытый — название, повтор сета, галочки уровней над колонками и строки. Раскрыт один сет за раз
 * (держит редактор) — так таблица остаётся обозримой и на телефоне.
 *
 * Галочка над колонкой — «этот уровень плывёт сет» (решение 8): снята — ячейки колонки в сете
 * выключены, метры уровня сет не считают. Все отмечены — `level_ids: null` («Everyone»).
 */

function WorkoutSetCard({
  set, index, levels, desktop, open, onToggle, onChange, onRemove,
}: {
  set: WorkoutSet;
  index: number;
  levels: WorkoutLevel[];
  desktop: boolean;
  open: boolean;
  onToggle: () => void;
  onChange: (set: WorkoutSet) => void;
  onRemove: () => void;
}) {
  const active = new Set(set.level_ids ?? levels.map((l) => l.id));
  const meters = levels.length > 0 ? levels.map((l) => setMeters(set, l.id)).filter((m) => m > 0) : [setMeters(set, null)];
  const metersText = meters.length > 0 ? metersRange(meters) : '0 m';
  const title = set.title?.trim() || `Set ${index + 1}`;

  if (!open) {
    return (
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={false}
        className="deep-card mb-0 flex w-full min-w-0 cursor-pointer items-center gap-3 text-left"
        style={{ padding: '12px 14px' }}
      >
        <span dir="auto" className="min-w-0 flex-none truncate text-[13.5px] font-extrabold" style={{ color: 'var(--deep-text)', maxWidth: desktop ? 220 : '55%' }}>
          {set.repeat > 1 && <span className="hp-mono">{set.repeat}× </span>}{title}
        </span>
        {desktop && (
          <span dir="auto" className="min-w-0 flex-1 truncate text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }} title={setSummary(set)}>
            {set.lines.map((line, i) => (
              <React.Fragment key={i}>{i > 0 && ' · '}<WorkoutLineLabel line={line} /></React.Fragment>
            ))}
          </span>
        )}
        <span className={`truncate text-[11.5px] font-bold ${desktop ? 'flex-none' : 'min-w-0 flex-1 text-right'}`} style={{ color: 'var(--t-text-3)' }}>
          {setAudience(set, levels)}
        </span>
        <span className="hp-mono flex-none text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{metersText}</span>
        <span aria-hidden="true" className="flex-none text-[14px]" style={{ color: 'var(--t-text-3)' }}>›</span>
      </button>
    );
  }

  const toggleLevel = (id: number) => {
    const next = new Set(active);
    if (next.has(id)) next.delete(id); else next.add(id);
    const all = levels.every((l) => next.has(l.id));
    onChange({ ...set, level_ids: all ? null : levels.filter((l) => next.has(l.id)).map((l) => l.id) });
  };

  const levelChecks = levels.map((level) => {
    const on = active.has(level.id);
    return (
      <label
        key={level.id}
        className="flex h-9 min-w-0 cursor-pointer items-center justify-center gap-1 rounded-[10px] px-1.5"
        style={on ? levelCellStyle(level) : { border: '1px solid var(--deep-card-border)', background: 'transparent' }}
        title={`${level.name}${on ? ' swims this set' : ' skips this set'}`}
      >
        <input
          type="checkbox"
          checked={on}
          onChange={() => toggleLevel(level.id)}
          aria-label={`${level.name} swims this set`}
          className="h-3.5 w-3.5 flex-none cursor-pointer"
          style={{ accentColor: 'var(--deep-ow)' }}
        />
        <LevelTag level={level} size={16} />
      </label>
    );
  });

  const lines = set.lines;
  const patchLine = (i: number, line: WorkoutSet['lines'][number]) =>
    onChange({ ...set, lines: lines.map((l, j) => (j === i ? line : l)) });

  return (
    <section className="deep-card mb-0 flex min-w-0 flex-col gap-2.5" style={{ padding: desktop ? 14 : 12, borderColor: 'var(--deep-ow-border)' }} aria-label={title}>
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <input
          value={set.title ?? ''}
          onChange={(e) => onChange({ ...set, title: e.target.value })}
          dir="auto"
          maxLength={200}
          placeholder={`Set ${index + 1} · paddles + pull buoy`}
          aria-label="Set title"
          className={`${SHEET_FIELD_BASE_CLS} h-9 font-extrabold`}
          style={{ flex: '1 1 200px', maxWidth: desktop ? 320 : undefined }}
        />
        <label className="flex flex-none items-center gap-1 text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }} title="Repeat the whole set">
          Repeat
          <input
            type="number"
            min={1}
            max={20}
            value={set.repeat}
            onChange={(e) => {
              const n = Math.round(Number(e.target.value));
              if (n >= 1 && n <= 20) onChange({ ...set, repeat: n });
            }}
            aria-label="Repeat the whole set"
            className={`${SHEET_FIELD_BASE_CLS} h-9 w-[52px] hp-mono text-center font-extrabold`}
            style={{ paddingInline: 4 }}
          />
        </label>
        <label className="flex flex-none cursor-pointer items-center gap-1 text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }} title="Mark the key set of the workout (it is highlighted on the print sheet)">
          <input
            type="checkbox"
            checked={!!set.main}
            // false не храним: снятая галочка = поля нет, как у документов до появления метки.
            onChange={(e) => onChange({ ...set, main: e.target.checked || undefined })}
            className="h-3.5 w-3.5 flex-none cursor-pointer"
            style={{ accentColor: 'var(--deep-ow)' }}
          />
          Main set
        </label>
        <span className="hp-mono flex-none text-[12.5px] font-extrabold" style={{ color: 'var(--deep-text)' }}>{metersText}</span>
        <span className="ml-auto flex flex-none items-center gap-1">
          <button
            type="button"
            onClick={onToggle}
            aria-expanded
            className="h-8 cursor-pointer rounded-[8px] border-0 bg-transparent px-2 text-[12px] font-extrabold"
            style={{ color: 'var(--t-text-3)' }}
          >
            Done
          </button>
          <button
            type="button"
            onClick={onRemove}
            aria-label="Remove set"
            title="Remove set"
            className="h-8 w-8 cursor-pointer rounded-[8px] border-0 bg-transparent text-[16px] font-bold"
            style={{ color: 'var(--deep-text-mute)' }}
          >
            ×
          </button>
        </span>
      </div>

      {levels.length > 0 && (desktop ? (
        <div className="grid items-center gap-1.5" style={{ gridTemplateColumns: lineGrid(levels.length) }}>
          <span className="col-span-2 text-right text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>
            Send-off by level · untick a level that skips this set
          </span>
          <span className="text-center text-[11px] font-extrabold" style={{ color: 'var(--t-text-3)' }}>V</span>
          {levelChecks}
          <span />
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          <span className="text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>Who swims this set</span>
          <div className="grid gap-1.5" style={{ gridTemplateColumns: `repeat(${Math.min(levels.length, 4)}, minmax(0, 1fr))` }}>{levelChecks}</div>
        </div>
      ))}

      {lines.map((line, i) => (
        <WorkoutLineRow
          key={i}
          line={line}
          levels={levels}
          active={active}
          desktop={desktop}
          onChange={(l) => patchLine(i, l)}
          onRemove={() => onChange({ ...set, lines: lines.filter((_, j) => j !== i) })}
        />
      ))}

      <button
        type="button"
        onClick={() => onChange({ ...set, lines: [...lines, { reps: 1, distance: null, text: null, intensity: null, per_level: null }] })}
        className="cursor-pointer self-start border-0 bg-transparent p-0 py-1 text-[12.5px] font-extrabold"
        style={{ color: 'var(--deep-ow)' }}
      >
        + Add line
      </button>
    </section>
  );
}

export default WorkoutSetCard;
