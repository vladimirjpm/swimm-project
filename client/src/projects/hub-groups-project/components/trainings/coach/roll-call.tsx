import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { HubGroupBreak, TrainingRsvp, TrainingRsvpPerson } from '../../../types';
import { rsvpPersonKey, type TrainingRsvpState } from '../../../use-training-rsvp';
import { useTrainingsTab, type RollChange } from '../trainings-context';
import { CancelReason } from '../next/session-tag';
import RollToast, { type RollToastData } from './roll-toast';
import {
  avatarBackground, findBreak, groupOf, nextAnswer, personMeta, pillStyle, sortRoll, statusLabel, statusSpoken,
  type RollGroup,
} from './roll-format';

/**
 * Перекличка вида тренера (Т6, хендофф Trainings Tab, 2a / 3b): сводка и полоса ответов,
 * фильтр-чипы, строки людей с ОДНОЙ кнопкой статуса по циклу (— → ✓ → ? → ✕ → —) и ⋮, тост
 * «Undo». Данные — ответы выбранного занятия (`roll`/`r` от секции), перерывы — `breaks` контекста.
 * Изменение ответа даёт тост; шторка человека сообщает о своих изменениях через `onRollChange`
 * контекста — этот компонент его слушает.
 */

type Filter = 'all' | Exclude<RollGroup, 'not_training'>;

const CHIPS: { id: Filter; label: string; spoken: string }[] = [
  { id: 'all', label: 'All', spoken: 'All' },
  { id: 'none', label: '—', spoken: 'No answer' },
  { id: 'yes', label: '✓', spoken: 'Going' },
  { id: 'maybe', label: '?', spoken: 'Not sure' },
  { id: 'no', label: '✕', spoken: 'Can’t come' },
  { id: 'break', label: '⏸', spoken: 'On break' },
];

const LONG_PRESS_MS = 500;
const LONG_PRESS_SLOP = 8;

interface ToastState extends RollToastData {
  /** Строка, изменённая последней, — пока виден тост, она подсвечена. */
  key: string;
  undo: () => Promise<unknown>;
}

function SummaryLine({ r, none }: { r: TrainingRsvp; none: number }) {
  return (
    <span className="hp-mono text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
      <b style={{ color: 'var(--deep-live)' }}>{r.yes}</b> going · <b style={{ color: 'var(--deep-gold)' }}>{r.maybe}</b> maybe
      {' '}· <b style={{ color: 'var(--deep-danger)' }}>{r.no}</b> out · {none} —
    </span>
  );
}

/** Полоса 6px: доли «идут / не уверены / не придут» от знаменателя; остаток — не ответили. */
function SummaryBar({ r }: { r: TrainingRsvp }) {
  const pct = (n: number) => (r.total > 0 ? `${(n / r.total) * 100}%` : '0%');
  return (
    <div
      className="flex h-1.5 overflow-hidden rounded-[3px]"
      style={{ background: 'var(--deep-seg-track)' }}
      role="img"
      aria-label={`${r.yes} going, ${r.maybe} not sure, ${r.no} not going of ${r.total}`}
    >
      <span style={{ width: pct(r.yes), background: 'var(--deep-live)' }} />
      <span style={{ width: pct(r.maybe), background: 'var(--deep-gold-bar)' }} />
      <span style={{ width: pct(r.no), background: 'var(--deep-danger)' }} />
    </div>
  );
}

function FilterChips({
  value, onChange, counts, height,
}: {
  value: Filter;
  onChange: (next: Filter) => void;
  counts: Record<Filter, number>;
  height: number;
}) {
  return (
    <div className="flex flex-wrap gap-[5px]" role="group" aria-label="Filter the roll call">
      {CHIPS.map((c) => {
        const on = value === c.id;
        return (
          <button
            key={c.id}
            type="button"
            aria-pressed={on}
            aria-label={`${c.spoken}: ${counts[c.id]}`}
            onClick={() => onChange(c.id)}
            className="flex cursor-pointer items-center gap-1 whitespace-nowrap border text-[12px] font-extrabold"
            style={{
              height,
              padding: '0 10px',
              borderRadius: 999,
              background: on ? 'var(--deep-ow-chip)' : 'transparent',
              borderColor: on ? 'var(--deep-ow-border)' : 'var(--deep-card-border)',
              color: on ? 'var(--deep-ow)' : 'var(--t-text-3)',
            }}
          >
            <span>{c.label}</span>
            <span className="hp-mono text-[10.5px]">{counts[c.id]}</span>
          </button>
        );
      })}
    </div>
  );
}

function RollRow({
  person, brk, marked, canAnswer, desktop, onPress, onOpen,
}: {
  person: TrainingRsvpPerson;
  brk: HubGroupBreak | null;
  marked: boolean;
  canAnswer: boolean;
  desktop: boolean;
  onPress: () => void;
  onOpen: () => void;
}) {
  const meta = personMeta(person, brk);
  const timer = useRef<number | undefined>(undefined);
  const origin = useRef<{ x: number; y: number } | null>(null);
  /** Долгое нажатие уже открыло шторку — следующий клик по кнопке статуса не должен её нажать. */
  const fired = useRef(false);

  const cancel = useCallback(() => {
    window.clearTimeout(timer.current);
    timer.current = undefined;
    origin.current = null;
  }, []);
  useEffect(() => cancel, [cancel]);

  // Не `preventDefault` на pointerdown — прокрутка списка не должна ломаться.
  const onPointerDown = (e: React.PointerEvent) => {
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    cancel();
    fired.current = false;
    origin.current = { x: e.clientX, y: e.clientY };
    timer.current = window.setTimeout(() => {
      fired.current = true;
      origin.current = null;
      timer.current = undefined;
      onOpen();
    }, LONG_PRESS_MS);
  };
  const onPointerMove = (e: React.PointerEvent) => {
    const o = origin.current;
    if (o && Math.hypot(e.clientX - o.x, e.clientY - o.y) > LONG_PRESS_SLOP) cancel();
  };
  const release = () => {
    cancel();
    // Клик сразу следует за отпусканием; флаг не должен пережить его и проглотить клик с клавиатуры.
    if (fired.current) window.setTimeout(() => { fired.current = false; }, 100);
  };
  const guarded = (action: () => void) => () => {
    if (fired.current) { fired.current = false; return; }
    action();
  };

  const name = person.name;
  return (
    <div
      className="flex select-none items-center gap-2 [-webkit-touch-callout:none]"
      style={{
        minHeight: desktop ? 44 : 52,
        borderRadius: desktop ? 10 : 12,
        padding: desktop ? '3px 0 3px 8px' : '4px 0 4px 8px',
        background: marked ? 'var(--deep-ow-soft)' : 'var(--deep-card-bg-raised)',
      }}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={release}
      onPointerCancel={release}
      onPointerLeave={cancel}
      onContextMenu={(e) => { if (fired.current) e.preventDefault(); }}
    >
      <span
        aria-hidden="true"
        className="flex flex-none items-center justify-center rounded-full font-black"
        style={{
          width: desktop ? 26 : 28,
          height: desktop ? 26 : 28,
          fontSize: desktop ? 10.5 : 11,
          background: avatarBackground(person),
          color: 'var(--deep-accent-ink)',
        }}
      >
        {(name.trim()[0] ?? '?').toUpperCase()}
      </span>
      <div className="min-w-0 flex-1 text-left">
        <div
          dir="auto"
          className="overflow-hidden text-ellipsis whitespace-nowrap font-extrabold"
          style={{ fontSize: desktop ? 12.5 : 13, color: 'var(--deep-text)' }}
        >
          <bdi>{name}</bdi>
        </div>
        {meta && (
          <div
            className="overflow-hidden text-ellipsis whitespace-nowrap font-bold"
            style={{ fontSize: desktop ? 10 : 10.5, color: 'var(--t-text-3)' }}
            title={meta}
          >
            {meta}
          </div>
        )}
      </div>
      <button
        type="button"
        disabled={!canAnswer}
        onClick={guarded(onPress)}
        aria-label={`${name}: ${statusSpoken(person)}. Change`}
        className="flex flex-none cursor-pointer items-center justify-center whitespace-nowrap border font-extrabold disabled:cursor-default disabled:opacity-50"
        style={{
          minWidth: desktop ? 88 : 100,
          height: desktop ? 32 : 40,
          borderRadius: desktop ? 8 : 10,
          padding: desktop ? '0 8px' : '0 10px',
          fontSize: desktop ? 11.5 : 12,
          ...pillStyle(person),
        }}
      >
        {statusLabel(person)}
      </button>
      <button
        type="button"
        onClick={guarded(onOpen)}
        aria-label={`More for ${name}`}
        className="flex flex-none cursor-pointer items-center justify-center border-0 bg-transparent font-black"
        style={{
          width: desktop ? 24 : 30,
          height: desktop ? 32 : 44,
          fontSize: desktop ? 15 : 16,
          color: 'var(--t-text-3)',
        }}
      >
        ⋮
      </button>
    </div>
  );
}

function RollCall({ roll, r }: { roll: TrainingRsvpState; r: TrainingRsvp }) {
  const { isDesktop, breaks, openSheet, setRollChangeListener } = useTrainingsTab();
  const [filter, setFilter] = useState<Filter>('all');
  const [toast, setToast] = useState<ToastState | null>(null);
  const toastId = useRef(0);
  // Слушатель регистрируется один раз и читает свежий `roll` через ref.
  const live = useRef(roll);
  live.current = roll;

  const people = useMemo(() => sortRoll(r.people ?? []), [r.people]);

  const counts = useMemo(() => {
    const c: Record<Filter, number> = { all: people.length, none: 0, yes: 0, maybe: 0, no: 0, break: 0 };
    people.forEach((p) => {
      const g = groupOf(p);
      if (g !== 'not_training') c[g] += 1;
    });
    return c;
  }, [people]);

  const rows = filter === 'all' ? people : people.filter((p) => groupOf(p) === filter);

  // Тост — на смене занятия снимаем: «Undo» и подсветка строки относились к другому дню.
  useEffect(() => { setToast(null); }, [r.session_id]);

  const showToast = useCallback((change: RollChange) => {
    const { answerFor } = live.current;
    toastId.current += 1;
    setToast({
      id: toastId.current,
      key: rsvpPersonKey(change.person),
      name: change.person.name,
      answer: change.next.answer,
      undo: () => answerFor(change.person, change.prev.answer, change.prev.note),
    });
  }, []);

  useEffect(() => {
    setRollChangeListener(showToast);
    return () => setRollChangeListener(null);
  }, [setRollChangeListener, showToast]);

  const press = async (p: TrainingRsvpPerson) => {
    if (!r.can_answer) return;
    const next = nextAnswer(p);
    const prevNote = p.note ?? null;
    const err = await roll.answerFor(p, next, prevNote);
    if (err) return; // текст ошибки — в `roll.error`, строкой под списком
    // Ответ «Going» снимает перерыв на сервере — список перерывов перечитываем.
    if (next === 'yes' && p.on_break) breaks.reload();
    showToast({
      person: p,
      prev: { answer: p.answer, note: prevNote },
      next: { answer: next, note: next ? prevNote : null },
    });
  };

  const undo = () => {
    const t = toast;
    setToast(null);
    if (t) void t.undo();
  };

  // Р9: занятие отменено — вместо строк «Training cancelled» и причина. Ответы на месте (вернут — снова видны).
  if (r.cancelled) {
    return (
      <div className="flex flex-col gap-1">
        <p className="m-0 text-[13px] font-extrabold" style={{ color: 'var(--deep-danger)' }}>Training cancelled</p>
        <CancelReason reason={r.cancel_reason} />
      </div>
    );
  }

  if (people.length === 0) {
    return (
      <p className="m-0 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
        The group has no member accounts yet — people answer after they join the group.
      </p>
    );
  }

  const list = rows.map((p) => (
    <RollRow
      key={rsvpPersonKey(p)}
      person={p}
      brk={p.on_break ? findBreak(breaks.data?.breaks, p) : null}
      marked={toast?.key === rsvpPersonKey(p)}
      canAnswer={r.can_answer}
      desktop={isDesktop}
      onPress={() => { void press(p); }}
      onOpen={() => openSheet({ kind: 'person', person: p })}
    />
  ));

  return (
    <div className={`flex min-w-0 flex-col ${isDesktop ? 'gap-2' : 'gap-3'}`}>
      {isDesktop ? (
        <div className="flex items-center justify-between gap-2.5">
          <span className="flex items-center gap-2 text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
            Roll call
            {counts.none > 0 && (
              <span
                className="hp-mono inline-flex h-[18px] min-w-[18px] items-center justify-center text-[10.5px] tracking-normal"
                style={{ borderRadius: 9, padding: '0 5px', background: 'var(--deep-danger)', color: 'var(--deep-accent-ink)' }}
                aria-label={`${counts.none} not answered`}
              >
                {counts.none}
              </span>
            )}
          </span>
          <SummaryLine r={r} none={counts.none} />
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          <SummaryLine r={r} none={counts.none} />
          <SummaryBar r={r} />
        </div>
      )}
      {isDesktop && <SummaryBar r={r} />}

      <FilterChips value={filter} onChange={setFilter} counts={counts} height={isDesktop ? 30 : 34} />

      {isDesktop ? (
        <>
          <div className="grid gap-1" style={{ gridTemplateColumns: 'repeat(2, minmax(0, 1fr))' }}>{list}</div>
          <div className="text-[10.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>
            Click status: — → ✓ → ? → ✕ · ⋮ for status, break, note
          </div>
        </>
      ) : (
        rows.length > 0 && (
          <div
            className="flex flex-col gap-0.5 border p-1.5"
            style={{ borderRadius: 16, borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)' }}
          >
            {list}
          </div>
        )
      )}

      {roll.error && (
        <p className="m-0 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }} role="alert">{roll.error}</p>
      )}

      <RollToast toast={toast} onUndo={undo} onExpire={() => setToast(null)} />
    </div>
  );
}

export default RollCall;
