import React, { useState } from 'react';
import {
  DeepSheet, SheetButton, SheetField, SheetFooter, SHEET_FIELD_CLS, SHEET_MONO_FIELD_CLS,
} from '../../../../components/deep/deep-sheet';
import type { HubGroupLevel } from '../../../types';
import { shortPlanKey } from '../../lane-plan-key';
import type { TrainingsSheetProps } from '../trainings-sheets';

/**
 * «Pool setup» (docs/plans/trainings-coach-flow-plan.md, CF2; макет F3; тон `coach`): число дорожек,
 * запас (в том же блоке, что число дорожек: «6 lanes · 1 reserved» — сколько последних дорожек
 * Auto-arrange всегда оставляет пустыми; решение Влада 14.11.2026), лимит людей на дорожку, уровень каждой
 * дорожки, «Copy from …» и удаление плана. Заменила прежний тулбар над дорожками (CF2).
 *
 * Шторка ничего не грузит и не сохраняет: открывает её редактор дорожек (`group-lane-editor.tsx`) и
 * отдаёт снимок своего черновика (`PoolSetup`). Правки копятся здесь и уходят в черновик по «Done»
 * (`onApply`); сохраняет план по-прежнему редактор. «Copy from …» и «Delete plan» закрывают шторку
 * и зовут действия редактора — подтверждения (`ConfirmDialog`) остаются у него и у `GroupLanes`.
 *
 * Уровни приходят на ВСЕ дорожки (до `maxLanes`), а не только на видимые: уменьшил число дорожек и
 * вернул — уровень убранной дорожки на месте, как и её люди в черновике редактора.
 */

/** Лимит людей на дорожку — до 20 (сервер: LanePlanRules.MaxPerLane). */
const MAX_PER_LANE = 20;

export interface PoolSetup {
  laneCount: number;
  /** Лимит людей на дорожку (перелив по подуровням); null — без лимита. */
  maxPerLane: number | null;
  /** Сколько дорожек Auto-arrange оставляет пустыми про запас. */
  reservedLanes: number;
  /** Уровень дорожки: индекс — номер − 1, длина `maxLanes`. */
  laneLevels: (number | null)[];
  maxLanes: number;
  /** Уровни группы. */
  levels: HubGroupLevel[];
  /** Дата плана-источника копии; null — копировать не с чего. */
  copyFrom: string | null;
  /** План уже сохранён — его можно удалить. */
  canDelete: boolean;
  onApply: (next: {
    laneCount: number; maxPerLane: number | null; reservedLanes: number; laneLevels: (number | null)[];
  }) => void;
  onCopy: () => void;
  onDelete: () => void;
}

/** Счётчик «N подпись − +» — половина общего блока дорожек (всего / из них запас). */
function Stepper({
  label, value, unit, min, max, onStep, fewer, more,
}: {
  label: string;
  value: number;
  /** Подпись после числа: «lanes», «reserved». */
  unit: string;
  min: number;
  max: number;
  onStep: (delta: number) => void;
  fewer: string;
  more: string;
}) {
  const btn =
    'h-8 w-8 flex-none cursor-pointer rounded-[8px] border-0 text-[15px] font-black leading-none disabled:cursor-not-allowed disabled:opacity-40';
  const style = { background: 'var(--deep-seg-track)', color: 'var(--deep-text)' };
  return (
    <div className="flex flex-none items-center gap-1.5" role="group" aria-label={label}>
      <span className="hp-mono mr-1 text-[13px] font-extrabold" style={{ color: 'var(--t-text)' }} aria-live="polite">
        {value} {unit}
      </span>
      <button type="button" className={btn} style={style} onClick={() => onStep(-1)} disabled={value <= min} aria-label={fewer}>−</button>
      <button type="button" className={btn} style={style} onClick={() => onStep(1)} disabled={value >= max} aria-label={more}>+</button>
    </div>
  );
}

function PoolSetupSheet({ sheet, onClose }: TrainingsSheetProps<'pool-setup'>) {
  const { setup } = sheet;
  const [laneCount, setLaneCount] = useState(setup.laneCount);
  const [maxPerLane, setMaxPerLane] = useState<number | null>(setup.maxPerLane);
  const [laneLevels, setLaneLevels] = useState(setup.laneLevels);
  const [reservedRaw, setReserved] = useState(setup.reservedLanes);
  // Запас — не больше, чем «все дорожки, кроме одной»: хоть одна должна остаться людям.
  const maxReserved = laneCount - 1;
  const reservedLanes = Math.min(reservedRaw, maxReserved);

  const changeLaneCount = (delta: number) =>
    setLaneCount((c) => Math.min(setup.maxLanes, Math.max(1, c + delta)));

  const done = () => {
    setup.onApply({ laneCount, maxPerLane, reservedLanes, laneLevels });
    onClose();
  };

  /** Действие редактора вместо правок шторки: её черновик при этом не применяется. */
  const leaveTo = (action: () => void) => {
    onClose();
    action();
  };

  return (
    <DeepSheet
      title="Pool setup"
      sub="Lanes for this training"
      tone="coach"
      onClose={onClose}
      onSubmit={done}
      footer={(
        <SheetFooter>
          <SheetButton kind="cancel" onClick={onClose}>Cancel</SheetButton>
          <SheetButton tone="coach">Done</SheetButton>
        </SheetFooter>
      )}
    >
      {/* Один блок, два числа (решение Влада 14.11.2026): всего дорожек и сколько из них запас. */}
      <SheetField
        label="Lanes"
        group
        hint={reservedLanes > 0
          ? `${laneCount - reservedLanes} for swimmers · the last ${reservedLanes === 1 ? 'lane stays' : `${reservedLanes} lanes stay`} empty after Auto-arrange`
          : 'Auto-arrange uses every lane'}
      >
        <div
          className="flex min-h-11 flex-wrap items-center gap-x-3 gap-y-1.5 rounded-[10px] border px-3 py-1.5"
          style={{ borderColor: 'var(--deep-card-border)', background: 'var(--t-input-bg)' }}
        >
          <Stepper
            label="Number of lanes"
            value={laneCount}
            unit={laneCount === 1 ? 'lane' : 'lanes'}
            min={1}
            max={setup.maxLanes}
            onStep={changeLaneCount}
            fewer="Fewer lanes"
            more="More lanes"
          />
          {/* На узком телефоне счётчики встают в две строки — разделитель там лишний. */}
          <span aria-hidden="true" className="hidden sm:inline" style={{ color: 'var(--t-text-3)' }}>·</span>
          <Stepper
            label="Lanes kept empty in reserve"
            value={reservedLanes}
            unit="reserved"
            min={0}
            max={maxReserved}
            onStep={(delta) => setReserved(Math.min(maxReserved, Math.max(0, reservedLanes + delta)))}
            fewer="Fewer reserved lanes"
            more="More reserved lanes"
          />
        </div>
      </SheetField>
      <div>
        <SheetField label="Max per lane">
          <select
            value={maxPerLane ?? ''}
            onChange={(e) => setMaxPerLane(e.target.value === '' ? null : Number(e.target.value))}
            className={`${SHEET_MONO_FIELD_CLS} cursor-pointer`}
          >
            <option value="">No limit</option>
            {Array.from({ length: MAX_PER_LANE }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </SheetField>
      </div>
      {maxPerLane != null && (
        <p className="m-0 text-[11.5px] font-semibold" style={{ color: 'var(--t-text-3)' }}>
          When a lane is full, the bottom sub-level of its level moves to the next level’s lane.
        </p>
      )}

      <SheetField
        label="Lane levels"
        group
        hint="Auto-arrange sets the levels again by how many are coming."
      >
        <div className="flex flex-col gap-2">
          {Array.from({ length: laneCount }, (_, i) => (
            <div key={i} className="flex items-center gap-2.5">
              <span
                className="w-6 flex-none text-center text-[18px] leading-none"
                style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--t-text-3)' }}
                aria-hidden="true"
              >
                {i + 1}
              </span>
              <select
                value={laneLevels[i] ?? ''}
                onChange={(e) => {
                  const value = e.target.value === '' ? null : Number(e.target.value);
                  setLaneLevels((ls) => ls.map((l, at) => (at === i ? value : l)));
                }}
                aria-label={`Level of lane ${i + 1}`}
                className={`${SHEET_FIELD_CLS} cursor-pointer`}
              >
                <option value="">No level</option>
                {setup.levels.map((l) => <option key={l.id} value={l.id}>{l.rank} · {l.name}</option>)}
              </select>
            </div>
          ))}
        </div>
      </SheetField>

      {(setup.copyFrom || setup.canDelete) && (
        <div className="flex flex-col gap-2.5 border-t pt-3.5" style={{ borderColor: 'var(--deep-card-border)' }}>
          {setup.copyFrom && (
            <SheetButton kind="cancel" onClick={() => leaveTo(setup.onCopy)}>
              Copy from {shortPlanKey(setup.copyFrom)}
            </SheetButton>
          )}
          {setup.canDelete && (
            <SheetButton kind="danger" type="button" onClick={() => leaveTo(setup.onDelete)}>
              Delete plan
            </SheetButton>
          )}
        </div>
      )}
    </DeepSheet>
  );
}

export default PoolSetupSheet;
