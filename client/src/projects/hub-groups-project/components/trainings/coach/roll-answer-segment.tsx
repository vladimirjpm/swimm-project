import React from 'react';
import type { RsvpAnswer } from '../../../types';
import { ANSWER_PILL } from './roll-format';

/**
 * Сегмент ответа в строке переклички тренера: Going / Maybe / Out (макет F2 потока тренера,
 * решение Влада 18.11.2026 — вместо одной кнопки статуса по циклу). Нажатие на выбранный
 * ответ снимает его (как в шторке человека). Перерыв и «не тренируется» без ответа — ни один
 * не выбран, статус виден в мете строки.
 */

const OPTIONS: { id: RsvpAnswer; label: string; spoken: string }[] = [
  { id: 'yes', label: 'Going', spoken: 'Going' },
  { id: 'maybe', label: 'Maybe', spoken: 'Not sure' },
  { id: 'no', label: 'Out', spoken: 'Can’t come' },
];

function RollAnswerSegment({
  name, answer, disabled, desktop, onPick,
}: {
  name: string;
  answer: RsvpAnswer | null;
  disabled: boolean;
  desktop: boolean;
  onPick: (answer: RsvpAnswer) => void;
}) {
  return (
    <div
      className="flex flex-none overflow-hidden border"
      style={{
        borderRadius: desktop ? 8 : 10,
        borderColor: 'var(--deep-card-border)',
        background: 'var(--deep-seg-track)',
      }}
      role="group"
      aria-label={`${name}: answer`}
    >
      {OPTIONS.map((o, i) => {
        const on = answer === o.id;
        return (
          <button
            key={o.id}
            type="button"
            aria-pressed={on}
            aria-label={`${name}: ${o.spoken}`}
            disabled={disabled}
            onClick={() => onPick(o.id)}
            className="cursor-pointer whitespace-nowrap border-0 font-extrabold disabled:cursor-default disabled:opacity-50"
            style={{
              height: desktop ? 30 : 38,
              // Узко: на телефоне 386px имени остаётся ~85px — шире сегмент делать нельзя.
              minWidth: 40,
              padding: '0 6px',
              fontSize: desktop ? 11 : 11.5,
              borderLeft: i > 0 ? '1px solid var(--deep-card-border)' : undefined,
              ...(on
                ? { background: ANSWER_PILL[o.id].background, color: ANSWER_PILL[o.id].color }
                : { background: 'transparent', color: 'var(--t-text-3)' }),
            }}
          >
            {o.label}
          </button>
        );
      })}
    </div>
  );
}

export default RollAnswerSegment;
