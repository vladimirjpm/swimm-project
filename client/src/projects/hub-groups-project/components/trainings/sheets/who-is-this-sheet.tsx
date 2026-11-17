import React, { useMemo, useState } from 'react';
import { DeepSheet, SheetButton, SheetError, SheetFooter } from '../../../../components/deep/deep-sheet';
import { useGroupLevels } from '../../use-group-levels';
import { accountLinkApi } from '../account-link-api';
import { useTrainingsTab } from '../trainings-context';
import type { TrainingsSheetProps } from '../trainings-sheets';

/**
 * «Who is this?» (docs/plans/trainings-coach-flow-plan.md §5.7, решение 13; тон `coach`): кем
 * аккаунт стоит в группе. Открывается из «Needs you» (заявка на вступление, аккаунт без связи) и
 * из строки «Account» карточки человека.
 *
 * Исходы: «This swimmer» — пловец состава (сверху подсказка сервера: кого человек отметил «Me»,
 * иначе совпадение по имени) и кто это: он сам или родитель; «Not a swimmer» — родитель,
 * наблюдатель, без привязки (запоминается, строка в «Needs you» не возвращается); у заявки ещё
 * «Decline». У заявки «This swimmer» / «Not a swimmer» ставят метку и сразу одобряют.
 *
 * Состав — пловцы видимого состава из уровней группы (та же ручка, что у Following). Человеку при
 * подаче заявки состав не показываем — решает тренер.
 */

export interface WhoIsThisRequest {
  userId: number;
  name: string;
  /** Заявка на вступление (pending): есть «Decline», привязка сразу одобряет. */
  request: boolean;
  suggestedSwimmerId?: number | null;
  /** Пловец, с которым аккаунт уже связан (смена привязки из карточки). */
  currentSwimmerId?: number | null;
}

type Outcome = 'swimmer' | 'not-swimmer' | 'decline';
type Relation = 'self' | 'parent';

const CAPTION_CLS = 'text-[10px] font-extrabold uppercase tracking-[.07em]';

function WhoIsThisSheet({ sheet, onClose }: TrainingsSheetProps<'who-is-this'>) {
  const { who } = sheet;
  const { group, selectedRoll, reloadLanePlans } = useTrainingsTab();
  const roll = selectedRoll;
  const levels = useGroupLevels(group.id);

  const initialSwimmer = who.currentSwimmerId ?? who.suggestedSwimmerId ?? null;
  const [outcome, setOutcome] = useState<Outcome>('swimmer');
  const [swimmerId, setSwimmerId] = useState<number | null>(initialSwimmer);
  const [relation, setRelation] = useState<Relation>('self');
  const [query, setQuery] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Кем уже стоят другие аккаунты — в списке с пометкой, чтобы не связать двоих с одним пловцом случайно.
  const taken = useMemo(() => {
    const map = new Map<number, string>();
    roll?.rsvp?.people?.forEach((p) => {
      if (p.user_id != null && p.user_id !== who.userId && p.linked_swimmer_id != null) map.set(p.linked_swimmer_id, p.name);
    });
    return map;
  }, [roll?.rsvp?.people, who.userId]);

  const swimmers = levels.data?.swimmers ?? [];
  const suggested = swimmers.find((s) => s.swimmerId === who.suggestedSwimmerId) ?? null;
  const q = query.trim().toLowerCase();
  const list = swimmers.filter((s) => !q || `${s.name} ${s.nameEn}`.toLowerCase().includes(q));

  const save = async () => {
    if (busy) return;
    setError(null);
    setBusy(true);
    let res;
    if (outcome === 'decline') {
      res = await accountLinkApi.decline(group.id, who.userId);
    } else if (outcome === 'swimmer') {
      if (swimmerId == null) { setError('Pick the swimmer.'); setBusy(false); return; }
      res = await accountLinkApi.setLabel(group.id, who.userId, swimmerId, relation === 'parent' ? 'parent' : null);
    } else {
      res = await accountLinkApi.setNotSwimmer(group.id, who.userId);
    }
    if (res.ok && who.request && outcome !== 'decline') res = await accountLinkApi.approve(group.id, who.userId);
    setBusy(false);
    if (!res.ok) { setError(res.error ?? 'Could not save. Try again.'); return; }
    // Перекличка, «Needs you» и план (идущий пловец теперь аккаунт) — перечитать.
    roll?.reload();
    reloadLanePlans();
    onClose();
  };

  const option = (id: Outcome, title: string, desc: string) => {
    const on = outcome === id;
    return (
      <button
        key={id}
        type="button"
        role="radio"
        aria-checked={on}
        onClick={() => setOutcome(id)}
        className="flex min-h-[48px] w-full cursor-pointer items-center gap-2.5 border text-left"
        style={{
          borderRadius: 12,
          padding: '0 12px',
          borderColor: on ? 'var(--deep-ow-border)' : 'var(--deep-card-border)',
          background: on ? 'var(--deep-ow-soft)' : 'transparent',
          color: 'var(--t-text)',
        }}
      >
        <span
          aria-hidden="true"
          className="flex flex-none items-center justify-center rounded-full"
          style={{ width: 18, height: 18, border: `2px solid ${on ? 'var(--deep-ow)' : 'var(--deep-card-border)'}` }}
        >
          {on && <span className="rounded-full" style={{ width: 8, height: 8, background: 'var(--deep-ow)' }} />}
        </span>
        <span className="min-w-0">
          <span className="block text-[13px] font-extrabold">{title}</span>
          <span className="block text-[10.5px] font-semibold" style={{ color: 'var(--t-text-3)' }}>{desc}</span>
        </span>
      </button>
    );
  };

  const submitLabel = outcome === 'decline' ? 'Decline' : who.request ? 'Approve' : 'Save';

  return (
    <DeepSheet
      title={<>Who is <bdi>{who.name}</bdi>?</>}
      sub={who.request ? 'wants to join the group' : 'account in the group'}
      tone="coach"
      busy={busy}
      onClose={onClose}
      onSubmit={() => { void save(); }}
      footer={(
        <div className="flex flex-col gap-3.5">
          <SheetError error={error} />
          <SheetFooter>
            <SheetButton kind="cancel" onClick={onClose}>Cancel</SheetButton>
            <SheetButton tone="coach" disabled={busy}>{submitLabel}</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <div className="flex flex-col gap-1" role="radiogroup" aria-label="Who is this">
        {option('swimmer', 'This swimmer', 'The account answers for a swimmer of the group')}
        {option('not-swimmer', 'Not a swimmer', 'A parent or a follower — no swimmer, not asked again')}
        {who.request && option('decline', 'Decline', 'The request is removed')}
      </div>

      {outcome === 'swimmer' && (
        <div className="flex flex-col gap-1.5">
          <span className={CAPTION_CLS} style={{ color: 'var(--t-text-3)' }}>Swimmer</span>
          {suggested && swimmerId !== suggested.swimmerId && (
            <button
              type="button"
              onClick={() => setSwimmerId(suggested.swimmerId)}
              className="cursor-pointer self-start border-0 bg-transparent p-0 text-left text-[12px] font-extrabold underline"
              style={{ color: 'var(--deep-ow)' }}
            >
              Suggested: <bdi>{suggested.name || suggested.nameEn}</bdi>
            </button>
          )}
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search the roster"
            aria-label="Search the roster"
            dir="auto"
            className="h-11 w-full rounded-[10px] border border-[var(--deep-card-border)] bg-[var(--deep-card-bg)] px-3 text-[13px] font-bold text-[var(--deep-text)]"
          />
          <div className="flex max-h-[220px] flex-col gap-0.5 overflow-y-auto" role="listbox" aria-label="Roster">
            {levels.data == null && (
              <span className="text-[12px] font-bold" style={{ color: 'var(--t-text-3)' }}>
                {levels.loadError ? 'Could not load the roster.' : 'Loading…'}
              </span>
            )}
            {list.map((s) => {
              const on = swimmerId === s.swimmerId;
              const other = taken.get(s.swimmerId);
              return (
                <button
                  key={s.swimmerId}
                  type="button"
                  role="option"
                  aria-selected={on}
                  onClick={() => setSwimmerId(s.swimmerId)}
                  className="flex min-h-[40px] w-full cursor-pointer items-center gap-2 border text-left text-[13px] font-bold"
                  style={{
                    borderRadius: 10,
                    padding: '0 10px',
                    // Выбор должен читаться сразу: заливка `--deep-ow-soft` в светлой теме почти не видна.
                    borderColor: on ? 'var(--deep-ow)' : 'transparent',
                    background: on ? 'var(--deep-ow-soft)' : 'transparent',
                    color: 'var(--t-text)',
                  }}
                >
                  <span aria-hidden="true" className="w-3 flex-none text-[13px] font-extrabold" style={{ color: 'var(--deep-ow)' }}>{on ? '✓' : ''}</span>
                  <bdi className="min-w-0 flex-1 truncate">{s.name || s.nameEn}</bdi>
                  {s.swimmerId === who.suggestedSwimmerId && (
                    <span className="flex-none text-[10.5px] font-extrabold" style={{ color: 'var(--deep-gold)' }}>suggested</span>
                  )}
                  {other && (
                    <span className="flex-none truncate text-[10.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
                      linked: <bdi>{other}</bdi>
                    </span>
                  )}
                </button>
              );
            })}
          </div>
          <div className="mt-1 grid grid-cols-2" style={{ borderRadius: 12, background: 'var(--deep-seg-track)', padding: 3 }} role="group" aria-label="Relation">
            {(['self', 'parent'] as const).map((rel) => {
              const on = relation === rel;
              return (
                <button
                  key={rel}
                  type="button"
                  aria-pressed={on}
                  onClick={() => setRelation(rel)}
                  className="h-10 cursor-pointer border text-[12.5px] font-extrabold"
                  style={{
                    borderRadius: 9,
                    background: on ? 'var(--deep-ow)' : 'transparent',
                    color: on ? 'var(--deep-accent-ink)' : 'var(--t-text-3)',
                    borderColor: 'transparent',
                  }}
                >
                  {rel === 'self' ? 'It’s the swimmer' : 'A parent'}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </DeepSheet>
  );
}

export default WhoIsThisSheet;
