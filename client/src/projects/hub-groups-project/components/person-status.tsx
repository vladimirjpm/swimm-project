import React from 'react';
import type { PersonStatus } from '../types';

/**
 * Статус человека в группе — одна выпадашка на список тренера «кто идёт» и Following (Р74,
 * решение Влада 30.09.2026). Статус ОДИН: Active / On break / Doesn't train with us / Inactive.
 *
 *  • On break — временно (травма, отпуск): вне знаменателя и дорожек, «Going» возвращает;
 *  • Doesn't train with us — в группе (Following, результаты, рекорды), но с ней не плавает
 *    (тренер-пловчиха): вне «кто идёт» и дорожек, «Going» статус НЕ снимает;
 *  • Inactive — ушёл из группы: строка состава скрыта, Restore в My groups. Только у пловца
 *    состава (`withInactive`), у аккаунта «уйти» — это убрать из группы, отдельной кнопкой.
 *
 * Сама выпадашка ничего не пишет — `onChange` решает вызывающий (перерыв — `/breaks`,
 * Inactive — скрытие строки состава с подтверждением).
 */

export type RosterStatus = PersonStatus | 'inactive';

const LABEL: Record<RosterStatus, string> = {
  active: 'Active',
  break: '⏸ On break',
  not_training: 'Doesn’t train with us',
  inactive: 'Inactive — left the group',
};

const TONE: Record<RosterStatus, React.CSSProperties> = {
  active: { borderColor: 'var(--t-border)', color: 'var(--deep-text-mute)' },
  break: { borderColor: 'var(--deep-gold-bar)', color: 'var(--deep-gold)' },
  not_training: { borderColor: 'var(--deep-card-border)', color: 'var(--deep-text-mute)', fontStyle: 'italic' },
  inactive: { borderColor: 'color-mix(in srgb, var(--deep-danger) 30%, transparent)', color: 'var(--deep-danger)' },
};

/** Короткая подпись статуса для строки (пусто у Active). */
export function statusNote(status: RosterStatus): string | null {
  if (status === 'break') return 'on break';
  if (status === 'not_training') return 'doesn’t train with us';
  return null;
}

/** Статус из флагов ответа сервера (RSVP / перерывы). */
export function statusOf(flags: { on_break?: boolean; not_training?: boolean }): PersonStatus {
  if (flags.not_training) return 'not_training';
  if (flags.on_break) return 'break';
  return 'active';
}

/** Короткие подписи — для тесной строки списка «кто идёт» (рядом аватар, имя и три кнопки). */
const SHORT: Record<RosterStatus, string> = {
  active: 'Active',
  break: '⏸ Break',
  not_training: 'Not training',
  inactive: 'Inactive',
};

function PersonStatusSelect({
  value, onChange, withInactive = false, compact = false, disabled, name,
}: {
  value: RosterStatus;
  onChange: (next: RosterStatus) => void;
  withInactive?: boolean;
  /** Короткие подписи и узкая выпадашка — для строки списка тренера. */
  compact?: boolean;
  disabled?: boolean;
  /** Кого — для aria-label. */
  name: string;
}) {
  const options: RosterStatus[] = withInactive
    ? ['active', 'break', 'not_training', 'inactive']
    : ['active', 'break', 'not_training'];
  const labels = compact ? SHORT : LABEL;
  return (
    <select
      value={value}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value as RosterStatus)}
      aria-label={`Status of ${name}`}
      title={`Status in the group: ${LABEL[value]}`}
      className={`h-[28px] ${compact ? 'max-w-[96px]' : 'max-w-[150px]'} min-w-0 cursor-pointer rounded-[8px] border bg-[var(--t-input-bg)] px-1.5 text-[11.5px] font-extrabold disabled:cursor-default disabled:opacity-50`}
      style={TONE[value]}
    >
      {options.map((o) => <option key={o} value={o}>{labels[o]}</option>)}
    </select>
  );
}

export default PersonStatusSelect;
