import React from 'react';
import type { HubGroupLevel } from '../types';

/**
 * Выпадашка уровня пловца с подуровнями (решение Влада 20.10.2026, docs/plans/lane-plans-plan.md
 * «Подуровни») — одна на карточку `Levels` (таб Admin) и состав (таб Swimmers). У уровня без
 * подуровней один пункт «1 · Advanced», с подуровнями — по пункту на каждый: «1 · Advanced 1»,
 * «1 · Advanced 2». Пункт «без подуровня» есть, только пока пловец на нём стоит (подуровни
 * завели после того, как ему поставили уровень) — тренер видит и выбирает номер.
 */

/** Значение пункта: '' — без уровня, '12' — уровень без подуровня, '12:2' — уровень и подуровень. */
const valueOf = (levelId: number | null, subLevel: number | null | undefined) =>
  (levelId == null ? '' : subLevel == null ? String(levelId) : `${levelId}:${subLevel}`);

function parse(value: string): { levelId: number | null; subLevel: number | null } {
  if (value === '') return { levelId: null, subLevel: null };
  const [level, sub] = value.split(':');
  return { levelId: Number(level), subLevel: sub ? Number(sub) : null };
}

/** «Advanced 2» — имя уровня с подуровнем; без подуровня — просто имя. */
export function levelLabel(level: Pick<HubGroupLevel, 'name'>, subLevel?: number | null): string {
  return subLevel ? `${level.name} ${subLevel}` : level.name;
}

function LevelSelect({
  levels, levelId, subLevel, onChange, disabled, ariaLabel, title, className, withSubLevels = true,
}: {
  levels: HubGroupLevel[];
  levelId: number | null;
  subLevel?: number | null;
  onChange: (levelId: number | null, subLevel: number | null) => void;
  disabled?: boolean;
  ariaLabel: string;
  title?: string;
  className?: string;
  /** false — только уровни (аккаунтам без пловца подуровень не нужен: на перелив он не влияет). */
  withSubLevels?: boolean;
}) {
  const ownLevel = levels.find((l) => l.id === levelId);
  const current = valueOf(levelId, withSubLevels && (ownLevel?.subLevels ?? 1) > 1 ? subLevel : null);
  return (
    <select
      value={current}
      onChange={(e) => { const next = parse(e.target.value); onChange(next.levelId, next.subLevel); }}
      disabled={disabled}
      aria-label={ariaLabel}
      title={title}
      className={className}
    >
      <option value="">No level</option>
      {levels.map((l) => {
        const count = withSubLevels ? l.subLevels ?? 1 : 1;
        if (count <= 1) return <option key={l.id} value={l.id}>{l.rank} · {l.name}</option>;
        return (
          <React.Fragment key={l.id}>
            {current === String(l.id) && <option value={l.id}>{l.rank} · {l.name} (no sub-level)</option>}
            {Array.from({ length: count }, (_, i) => i + 1).map((sub) => (
              <option key={sub} value={`${l.id}:${sub}`}>{l.rank} · {levelLabel(l, sub)}</option>
            ))}
          </React.Fragment>
        );
      })}
    </select>
  );
}

export default LevelSelect;
