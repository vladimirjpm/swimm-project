import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { PersonStatus } from '../types';

/**
 * Статус человека в группе — один контрол на список тренера «кто идёт» и Following (Р74,
 * решение Влада 30.09.2026). Статус ОДИН: Active / On break / Doesn't train with us / Inactive.
 *
 *  • On break — временно (травма, отпуск): вне знаменателя и дорожек, «Going» возвращает;
 *  • Doesn't train with us — в группе (Following, результаты, рекорды), но с ней не плавает
 *    (тренер-пловчиха): вне «кто идёт» и дорожек, «Going» статус НЕ снимает;
 *  • Inactive — ушёл из группы: строка состава скрыта, Restore в My groups. Только у пловца
 *    состава (`withInactive`), у аккаунта «уйти» — это убрать из группы, отдельной кнопкой.
 *
 * Контрол — кнопка + меню с описаниями (макет 6a хендоффа Trainings Tab): у каждого пункта
 * вторая строка «что этот статус значит», у текущего — ✓.
 * Само меню ничего не пишет — `onChange` решает вызывающий (перерыв — `/breaks`,
 * Inactive — скрытие строки состава с подтверждением).
 */

export type RosterStatus = PersonStatus | 'inactive';

const LABEL: Record<RosterStatus, string> = {
  active: 'Active',
  break: '⏸ On break',
  not_training: 'Doesn’t train with us',
  inactive: 'Inactive — left the group',
};

/** Что статус значит — вторая строка пункта меню (по Р74; перерыв, поставленный тренером, бессрочный). */
const DESCRIPTION: Record<RosterStatus, string> = {
  active: 'Trains with the group',
  break: 'Out of answers and lanes; answering Going ends it',
  not_training: 'In records, never in the pool or lanes',
  inactive: 'Hidden from the list; restore in My groups',
};

/** Ширина меню статуса (макет 6a); на узком экране — по экрану минус поля. */
const MENU_WIDTH = 250;

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
  /** Короткая подпись и узкая кнопка — для строки списка тренера. */
  compact?: boolean;
  disabled?: boolean;
  /** Кого — для aria-label. */
  name: string;
}) {
  const options: RosterStatus[] = withInactive
    ? ['active', 'break', 'not_training', 'inactive']
    : ['active', 'break', 'not_training'];
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);
  /**
   * Где встаёт меню относительно кнопки: по умолчанию правым краем к правому краю кнопки, но
   * целиком в экране с отступом 16px. Кнопка не всегда в конце строки (в Following справа от неё
   * выпадашка уровня) — «просто right: 0» на телефоне уводило меню за левый край (−59px).
   */
  const [place, setPlace] = useState<{ left: number; width: number } | null>(null);

  useLayoutEffect(() => {
    if (!open || !buttonRef.current) { setPlace(null); return; }
    const r = buttonRef.current.getBoundingClientRect();
    const gutter = 16;
    const width = Math.min(MENU_WIDTH, window.innerWidth - gutter * 2);
    const leftInViewport = Math.max(gutter, Math.min(r.right - width, window.innerWidth - gutter - width));
    setPlace({ left: leftInViewport - r.left, width });
  }, [open]);

  // Закрытие по клику мимо и по Escape (образец — DeepMenuChip). mousedown, а не click:
  // клик по триггеру другого статуса закрывает это меню, а то же нажатие открывает то.
  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { setOpen(false); buttonRef.current?.focus(); }
    };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  // При открытии фокус — на текущий пункт (дальше стрелки). Ждём `place`: до него меню скрыто
  // (`visibility: hidden`), а в скрытый элемент фокус не встаёт.
  useEffect(() => {
    if (!open || !place) return;
    optionRefs.current[Math.max(0, options.indexOf(value))]?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, place]);

  // Контрол заблокировали при открытом меню (идёт запись) — меню закрываем.
  useEffect(() => { if (disabled) setOpen(false); }, [disabled]);

  const choose = (next: RosterStatus) => {
    setOpen(false);
    buttonRef.current?.focus();
    if (next !== value) onChange(next);
  };

  const onMenuKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    const cur = optionRefs.current.findIndex((el) => el === document.activeElement);
    const last = options.length - 1;
    const focusAt = (i: number) => { e.preventDefault(); optionRefs.current[i]?.focus(); };
    switch (e.key) {
      case 'ArrowDown': focusAt(cur < 0 || cur >= last ? 0 : cur + 1); break;
      case 'ArrowUp': focusAt(cur <= 0 ? last : cur - 1); break;
      case 'Home': focusAt(0); break;
      case 'End': focusAt(last); break;
      case 'Enter':
      case ' ':
        e.preventDefault();
        if (cur >= 0) choose(options[cur]);
        break;
      case 'Tab':
        // Фокус — на триггер до того, как браузер сдвинет его дальше: иначе он уйдёт
        // со снятого пункта меню в body.
        setOpen(false);
        buttonRef.current?.focus();
        break;
      default:
    }
  };

  return (
    <div ref={rootRef} className="relative inline-flex min-w-0">
      <button
        ref={buttonRef}
        type="button"
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={`Status of ${name}`}
        title={`Status in the group: ${LABEL[value]}`}
        onClick={() => setOpen((v) => !v)}
        className={`flex h-[34px] ${compact ? 'max-w-[96px]' : 'max-w-[150px]'} min-w-0 cursor-pointer items-center justify-between gap-1.5 rounded-[8px] border bg-[var(--t-input-bg)] px-2.5 text-[12px] font-extrabold disabled:cursor-default disabled:opacity-50`}
        style={TONE[value]}
      >
        <span className="min-w-0 truncate">{(compact ? SHORT : LABEL)[value]}</span>
        <span aria-hidden="true" className="shrink-0" style={{ color: 'var(--deep-text-mute)' }}>▾</span>
      </button>

      {open && (
        <div
          role="listbox"
          aria-label={`Status of ${name}`}
          onKeyDown={onMenuKeyDown}
          className="absolute top-[calc(100%+6px)] z-30 flex flex-col gap-0.5 rounded-[12px] border p-1"
          style={{
            left: place?.left ?? 0,
            width: place?.width ?? MENU_WIDTH,
            visibility: place ? 'visible' : 'hidden',
            background: 'var(--t-surface-strong)',
            borderColor: 'var(--deep-ow-border)',
            boxShadow: 'var(--deep-card-shadow)',
          }}
        >
          {options.map((o, i) => {
            const current = o === value;
            return (
              <button
                key={o}
                ref={(el) => { optionRefs.current[i] = el; }}
                type="button"
                role="option"
                aria-selected={current}
                tabIndex={-1}
                onClick={() => choose(o)}
                className={`flex min-h-[44px] w-full cursor-pointer items-start gap-1 rounded-[8px] border-0 px-2.5 py-2 text-left outline-none hover:bg-[var(--deep-ow-chip)] focus:bg-[var(--deep-ow-chip)] ${current ? 'bg-[var(--deep-ow-soft)]' : 'bg-transparent'}`}
              >
                <span
                  aria-hidden="true"
                  className="w-3 shrink-0 text-[12px] font-black leading-[1.3]"
                  style={{ color: 'var(--deep-ow)' }}
                >
                  {current ? '✓' : ''}
                </span>
                <span className="min-w-0">
                  <span
                    className="block text-[12.5px] font-extrabold leading-[1.3]"
                    style={{ color: o === 'inactive' ? 'var(--deep-danger)' : current ? 'var(--deep-ow)' : 'var(--deep-text)' }}
                  >
                    {LABEL[o]}
                  </span>
                  <span className="block text-[10.5px] font-semibold leading-[1.35]" style={{ color: 'var(--deep-text-mute)' }}>
                    {DESCRIPTION[o]}
                  </span>
                </span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

export default PersonStatusSelect;
