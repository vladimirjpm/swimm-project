import React from 'react';
import type { HubGroupLevel, LanePlan, TrainingRsvpPerson } from '../../../types';
import LevelSelect from '../../level-select';

/**
 * Поля карточки человека для бассейна (docs/plans/trainings-coach-flow-plan.md, CF4): уровень
 * (тот же, что в Following и Admin → Levels), дорожка на это занятие с закреплением и строка
 * «Account: linked to …». Состояние держит `person-sheet.tsx`, здесь только вид.
 */

const CAPTION_CLS = 'text-[10px] font-extrabold uppercase tracking-[.07em]';

/** Где пловец в плане даты: дорожка, Unassigned, «Not today»; плана нет — null. */
export function placeInPlan(plan: LanePlan | null | undefined, swimmerId: number | null | undefined):
  { lane: number | null; inPlan: boolean; pinned: boolean } | null {
  if (!plan || swimmerId == null) return null;
  for (const l of plan.lanes) {
    const s = l.swimmers.find((x) => x.swimmer_id === swimmerId);
    if (s) return { lane: l.lane_no, inPlan: true, pinned: !!s.pinned };
  }
  if (plan.unassigned.some((x) => x.swimmer_id === swimmerId)) return { lane: null, inPlan: true, pinned: false };
  return { lane: null, inPlan: false, pinned: false };
}

export function PersonLevelField({
  levels, levelId, subLevel, forAccount, disabled, onChange,
}: {
  levels: HubGroupLevel[] | null;
  levelId: number | null;
  subLevel: number | null;
  /** Аккаунт без пловца: уровень аккаунта, без подуровней. */
  forAccount: boolean;
  disabled: boolean;
  onChange: (levelId: number | null, subLevel: number | null) => void;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <span className={CAPTION_CLS} style={{ color: 'var(--t-text-3)' }}>Level</span>
      {levels == null ? (
        <span className="text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>Loading…</span>
      ) : levels.length === 0 ? (
        <span className="text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>The group has no levels yet — add them in Admin → Levels.</span>
      ) : (
        <LevelSelect
          levels={levels}
          levelId={levelId}
          subLevel={subLevel}
          withSubLevels={!forAccount}
          disabled={disabled}
          onChange={onChange}
          ariaLabel="Level in the group"
          className="h-11 w-full cursor-pointer rounded-[10px] border border-[var(--deep-card-border)] bg-[var(--deep-card-bg)] px-3 text-[13px] font-extrabold text-[var(--deep-text)]"
        />
      )}
    </div>
  );
}

export function PersonLaneField({
  place, pinned, onPinned, onShow,
}: {
  place: { lane: number | null; inPlan: boolean } | null;
  pinned: boolean;
  onPinned: (on: boolean) => void;
  /** Показать пловца в редакторе дорожек. */
  onShow: (() => void) | null;
}) {
  if (!place) return null;
  const text = place.lane != null ? `Lane ${place.lane}` : place.inPlan ? 'Unassigned — no lane yet' : 'Not today — not in this plan';
  return (
    <div className="flex flex-col gap-1.5">
      <span className={CAPTION_CLS} style={{ color: 'var(--t-text-3)' }}>Lane this training</span>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <span className="text-[13px] font-extrabold" style={{ color: 'var(--t-text)' }}>{text}</span>
        {place.lane != null && (
          <label className="flex cursor-pointer items-center gap-1.5 text-[12px] font-bold" style={{ color: 'var(--t-text-2)' }}>
            <input type="checkbox" checked={pinned} onChange={(e) => onPinned(e.target.checked)} />
            <span><span aria-hidden="true">📌 </span>Pinned — Auto-arrange keeps them here</span>
          </label>
        )}
        {onShow && (
          <button
            type="button"
            onClick={onShow}
            className="ml-auto cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold underline"
            style={{ color: 'var(--deep-ow)' }}
          >
            Show in pool
          </button>
        )}
      </div>
    </div>
  );
}

export function PersonAccountField({ person, onChange }: { person: TrainingRsvpPerson; onChange: (() => void) | null }) {
  const text = person.user_id == null
    ? 'No account — the coach answers for them'
    : person.linked_swimmer_name
      ? <>Linked to <bdi>{person.linked_swimmer_name}</bdi></>
      : 'Not linked to a swimmer';
  return (
    <div className="flex flex-col gap-1.5">
      <span className={CAPTION_CLS} style={{ color: 'var(--t-text-3)' }}>Account</span>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="text-[13px] font-bold" style={{ color: 'var(--t-text)' }}>{text}</span>
        {onChange && (
          <button
            type="button"
            onClick={onChange}
            className="ml-auto cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold underline"
            style={{ color: 'var(--deep-ow)' }}
          >
            {person.linked_swimmer_name ? 'Change' : 'Who is this?'}
          </button>
        )}
      </div>
    </div>
  );
}
