import React from 'react';
import type { NextTraining } from '../../../types';
import { DEFAULT_TRAINING_TYPE, trainingTypeOf } from '../training-types';

/**
 * Пометки занятия в виде Next (Р9, docs/plans/trainings-tab-redesign-plan.md): название разовой
 * тренировки со значком типа и пилюля «Cancelled» с причиной. Общие для строки ответов участника
 * (`answers-block.tsx`) и навигации тренера (`coach/session-nav.tsx`) — один вид, одно место.
 */

/**
 * Название тренировки (`<bdi>`) и значок типа — рядом с датой занятия. Значок — кроме swim (он на
 * каждой тренировке и ничего не говорит). Нет ни названия, ни значка (обычное занятие по
 * расписанию) — ничего не рисуем.
 */
export function SessionTag({ session, className = '' }: { session: NextTraining; className?: string }) {
  const title = session.title?.trim() || null;
  const type = session.type ? trainingTypeOf({ type: session.type }) : null;
  const glyph = type && type.id !== DEFAULT_TRAINING_TYPE ? type.glyph : null;
  if (!title && !glyph) return null;
  return (
    <span
      className={`inline-flex min-w-0 items-center gap-1 text-[11.5px] font-extrabold ${className}`}
      style={{ color: 'var(--t-text-2)' }}
    >
      {glyph && <span aria-hidden="true" title={type?.label}>{glyph}</span>}
      {title && <bdi className="truncate">{title}</bdi>}
    </span>
  );
}

/** Пилюля «Cancelled»: рамка и текст — роль `--deep-danger`, 11px/800. */
export function CancelledPill() {
  return (
    <span
      className="inline-flex flex-none items-center rounded-full border px-2 py-[1px] text-[11px] font-extrabold"
      style={{ borderColor: 'var(--deep-danger)', color: 'var(--deep-danger)' }}
    >
      Cancelled
    </span>
  );
}

/** Причина отмены строкой 12px; нет причины — ничего. */
export function CancelReason({ reason }: { reason?: string | null }) {
  const text = reason?.trim();
  if (!text) return null;
  return (
    <div dir="auto" className="text-[12px] font-semibold" style={{ color: 'var(--t-text-3)' }}>
      <bdi>{text}</bdi>
    </div>
  );
}
