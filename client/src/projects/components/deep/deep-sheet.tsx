import React, { useEffect, useId, useRef } from 'react';
import { createPortal } from 'react-dom';
import { DEEP_DESKTOP_QUERY, useMediaQuery } from '../../../hooks/useMediaQuery';
import { useDeepThemeClass } from './use-deep-theme-class';

/**
 * Оболочка форм семьи deep: шторка снизу на телефоне и модал 480 по центру на десктопе
 * (`!design_handoff/design_handoff_trainings_tab` §Forms: «формы — шторки, никогда не в списке»).
 * Таб Trainings группы — первый потребитель; лежит в `components/deep/`, потому что формы
 * понадобятся и другим экранам deep.
 *
 * Два варианта:
 * - `'sheet'` (по умолчанию): < 960px — шторка снизу с ручкой, ≥ 960px — модал 480 по центру;
 * - `'dialog'`: всегда модал по центру (удаление — так и на телефоне), без кнопки ✕: выход —
 *   Cancel / Esc.
 *
 * Что держит сам: портал в `body` + класс темы deep на корне (снаружи страницы роли `--t-*`
 * пустые), `z-[125]` (выше переключателя темы, у него 120), подложка `--t-scrim` с закрытием
 * по клику и Esc (кроме `busy`), прокрутка страницы под слоем погашена, фокус при открытии.
 * Шапка и подвал остаются на месте, тело прокручивается.
 *
 * Не `MobileFiltersDrawer` (тот всегда в разметке ради состояния фильтров) и не `ConfirmDialog`
 * (у того фиксированная раскладка «текст + две кнопки»). Подробнее — docs/ui-components.md.
 */

export type SheetTone = 'accent' | 'coach' | 'neutral';

/** Цвета тона: рамка (верхний край шторки, рамка основной кнопки), фон кнопки и её текст. */
const TONE: Record<SheetTone, { border: string; chip: string; ink: string }> = {
  accent: { border: 'var(--deep-accent-border)', chip: 'var(--deep-accent-chip)', ink: 'var(--deep-accent)' },
  coach: { border: 'var(--deep-ow-border)', chip: 'var(--deep-ow-chip)', ink: 'var(--deep-ow)' },
  neutral: { border: 'var(--deep-card-border)', chip: 'var(--deep-card-bg)', ink: 'var(--t-text)' },
};

const FIELD_BASE =
  'w-full min-w-0 rounded-[10px] border border-[var(--deep-card-border)] bg-[var(--t-input-bg)] px-3 '
  + 'text-[13px] text-[var(--t-text)] outline-none focus:border-[color:var(--sheet-focus)] '
  + 'disabled:cursor-not-allowed disabled:opacity-60';

/** Класс поля формы: высота 44, радиус 10, рамка карточки. Фокус — рамка тона шторки. */
export const SHEET_FIELD_CLS = `h-11 font-bold ${FIELD_BASE}`;

/** Дата и время — моноширинные 13px/800. */
export const SHEET_MONO_FIELD_CLS = `h-11 hp-mono font-extrabold ${FIELD_BASE}`;

/** Многострочное поле: та же рамка, высота 64. */
export const SHEET_TEXTAREA_CLS =
  'h-16 w-full min-w-0 resize-none rounded-[10px] border border-[var(--deep-card-border)] bg-[var(--t-input-bg)] px-3 py-2.5 '
  + 'text-[13px] font-bold text-[var(--t-text)] outline-none focus:border-[color:var(--sheet-focus)]';

/** Подпись сверху + поле (`children`). Подпись — 11px/800, `--t-text-3`. */
export function SheetField({
  label, hint, group, children,
}: {
  label: React.ReactNode;
  /** Внутри не одно поле, а группа кнопок (сегмент): `div` вместо `label`, иначе клик по подписи нажмёт первую кнопку. */
  group?: boolean;
  /** Строка под полем (11px) — «Date is locked…», ограничения. */
  hint?: React.ReactNode;
  children: React.ReactNode;
}) {
  const Tag = group ? 'div' : 'label';
  return (
    <Tag className="flex min-w-0 flex-col gap-[5px]">
      <span className="text-[11px] font-extrabold" style={{ color: 'var(--t-text-3)' }}>{label}</span>
      {children}
      {hint && <span className="text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>{hint}</span>}
    </Tag>
  );
}

/** Ошибка записи — строкой над кнопками подвала; шторка при этом остаётся открытой. */
export function SheetError({ error }: { error: string | null }) {
  if (!error) return null;
  return <p className="m-0 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }} role="alert">{error}</p>;
}

/** Кнопка подвала: высота 48, радиус 12, 13px/800. `primary` красится по `tone`. */
export const SheetButton = React.forwardRef<HTMLButtonElement, {
  kind?: 'cancel' | 'primary' | 'danger';
  tone?: SheetTone;
  /** По умолчанию: cancel — `button`, остальные — `submit` (Enter в поле формы подтверждает). */
  type?: 'button' | 'submit';
  disabled?: boolean;
  onClick?: () => void;
  children: React.ReactNode;
}>(({
  kind = 'primary', tone = 'accent', type, disabled, onClick, children,
}, ref) => {
  const style: React.CSSProperties = kind === 'cancel'
    ? { borderColor: 'var(--deep-card-border)', background: 'transparent', color: 'var(--t-text-2)' }
    : kind === 'danger'
      ? { borderColor: 'var(--deep-danger)', background: 'var(--deep-danger-soft)', color: 'var(--deep-danger)' }
      : { borderColor: TONE[tone].border, background: TONE[tone].chip, color: TONE[tone].ink };
  return (
    <button
      ref={ref}
      type={type ?? (kind === 'cancel' ? 'button' : 'submit')}
      disabled={disabled}
      onClick={onClick}
      className="h-12 w-full cursor-pointer rounded-[12px] border text-[13px] font-extrabold disabled:cursor-not-allowed disabled:opacity-50"
      style={style}
    >
      {children}
    </button>
  );
});
SheetButton.displayName = 'SheetButton';

/** Подвал: Cancel + действие (`1fr 2fr`), одна кнопка во всю ширину, у диалога — `1fr 1fr`. */
export function SheetFooter({ children, even }: { children: React.ReactNode; even?: boolean }) {
  const count = React.Children.count(children);
  return (
    <div
      className="grid gap-2.5"
      style={{ gridTemplateColumns: count > 1 ? (even ? '1fr 1fr' : '1fr 2fr') : '1fr' }}
    >
      {children}
    </div>
  );
}

export interface DeepSheetProps {
  title: React.ReactNode;
  /** Подпись под заголовком, 11.5px/700. */
  sub?: React.ReactNode;
  tone?: SheetTone;
  variant?: 'sheet' | 'dialog';
  /** Идёт запись: закрыть нельзя (ни Esc, ни подложка, ни ✕). */
  busy?: boolean;
  onClose: () => void;
  /** Enter в поле / основная кнопка подвала (`type="submit"`). */
  onSubmit?: () => void;
  /** Кнопки действия — всегда внизу, вне прокрутки. */
  footer?: React.ReactNode;
  /** Кому отдать фокус при открытии; иначе — первое поле ввода, иначе ✕. */
  initialFocusRef?: React.RefObject<HTMLElement>;
  children: React.ReactNode;
}

const FOCUSABLE_FIELD = 'input:not([disabled]):not([type="hidden"]), textarea:not([disabled]), select:not([disabled])';

export function DeepSheet({
  title, sub, tone = 'accent', variant = 'sheet', busy = false, onClose, onSubmit, footer, initialFocusRef, children,
}: DeepSheetProps) {
  const themeClass = useDeepThemeClass();
  const isDesktop = useMediaQuery(DEEP_DESKTOP_QUERY);
  const titleId = useId();
  const panelRef = useRef<HTMLFormElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  // Свежие значения для слушателя Esc — эффект ставится один раз, а не на каждый рендер.
  const live = useRef({ busy, onClose });
  live.current = { busy, onClose };

  // 'dialog' — всегда модал; 'sheet' — модал только на десктопе.
  const modal = variant === 'dialog' || isDesktop;
  const t = TONE[tone];

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !live.current.busy) live.current.onClose();
    };
    window.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.style.overflow = prev;
    };
  }, []);

  // Фокус при открытии: заказанный элемент → первое поле → ✕ (у 'dialog' его нет — первая кнопка).
  useEffect(() => {
    const target = initialFocusRef?.current
      ?? panelRef.current?.querySelector<HTMLElement>(FOCUSABLE_FIELD)
      ?? closeRef.current
      ?? panelRef.current?.querySelector<HTMLElement>('button');
    target?.focus();
    // Только при открытии: смена `modal` при повороте экрана фокус перебивать не должна.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const panelStyle: React.CSSProperties = {
    ['--sheet-focus' as string]: tone === 'coach' ? 'var(--deep-ow-border)' : 'var(--deep-accent-border)',
    background: 'var(--t-surface-strong)',
    color: 'var(--t-text)',
    border: '1px solid var(--deep-card-border)',
    boxShadow: 'var(--deep-card-shadow)',
    maxHeight: '90dvh',
    ...(modal
      ? { width: 480, maxWidth: 'calc(100vw - 32px)', borderRadius: 18, padding: 20 }
      : {
        width: '100%',
        borderRadius: '22px 22px 0 0',
        borderBottom: 0,
        borderTopColor: t.border,
        padding: '8px 16px calc(20px + env(safe-area-inset-bottom))',
      }),
  };

  return createPortal(
    <div
      className={`fixed inset-0 z-[125] flex justify-center ${modal ? 'items-center' : 'items-end'} ${themeClass}`}
      style={{ background: 'var(--t-scrim)' }}
      onClick={() => { if (!busy) onClose(); }}
    >
      <form
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex min-h-0 flex-col gap-3.5"
        style={panelStyle}
        onClick={(e) => e.stopPropagation()}
        onSubmit={(e) => { e.preventDefault(); if (!busy) onSubmit?.(); }}
      >
        {!modal && (
          <div className="mx-auto shrink-0" style={{ width: 36, height: 4, borderRadius: 2, background: 'var(--deep-card-border)' }} aria-hidden="true" />
        )}

        <div className="flex shrink-0 items-start justify-between gap-3">
          <div className="min-w-0">
            <h2
              id={titleId}
              className="m-0 text-[18px] font-normal leading-tight"
              style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--t-text)' }}
            >
              {title}
            </h2>
            {sub && <div className="mt-1 text-[11.5px] font-bold" style={{ color: 'var(--t-text-3)' }}>{sub}</div>}
          </div>
          {variant !== 'dialog' && (
            <button
              ref={closeRef}
              type="button"
              aria-label="Close"
              disabled={busy}
              onClick={onClose}
              className="inline-flex h-9 w-9 shrink-0 cursor-pointer items-center justify-center rounded-full border-0 text-[15px] font-extrabold disabled:opacity-50"
              style={{ background: 'var(--deep-seg-track)', color: 'var(--t-text-3)' }}
            >
              ✕
            </button>
          )}
        </div>

        <div className="flex min-h-0 flex-1 flex-col gap-3.5 overflow-y-auto">{children}</div>

        {footer && <div className="shrink-0">{footer}</div>}
      </form>
    </div>,
    document.body,
  );
}
