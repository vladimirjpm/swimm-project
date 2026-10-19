import React from 'react';
import {
  IconCaretDown,
  IconCaretLeft,
  IconCaretRight,
  IconCaretUp,
  IconFunnel,
  IconLotus,
  IconSquaresFour,
  IconSwim,
  IconWaves,
} from './calendar-icons';
import { MEET_TYPES, type CalendarView } from './calendar-state';
import type { MeetType } from './calendar-types';

/*
 * Строка управления календарём. Десктоп (≥ lg): ‹ период ›, сегмент видов, чипы типов в одну
 * строку. Телефон (< lg): ‹ период › (в Month подпись — тоггл разворота месяца), сегмент
 * Year·Month·Meet + кнопка Type, под ней плавно раскрывающаяся панель типов.
 * Своя вёрстка сегмента: `.deep-seg` — пилюля без обводки и разделителей, макету нужен
 * radiogroup с разделителями (docs/ui-components.md §6).
 */

const VIEW_OPTIONS: { value: CalendarView; label: string }[] = [
  { value: 'year', label: 'Full year' },
  { value: 'month', label: 'Full month' },
  { value: 'meets', label: 'Meet days' },
];

const TYPE_OPTIONS: { value: MeetType | 'all'; label: string; Icon: typeof IconSwim }[] = [
  { value: 'all', label: 'All', Icon: IconSquaresFour },
  { value: 'pool', label: 'Pool swimming', Icon: IconSwim },
  { value: 'open_water', label: 'Open water', Icon: IconWaves },
  { value: 'artistic', label: 'Artistic swimming', Icon: IconLotus },
];

export interface CalendarControlsProps {
  /** «October 2026» / «Season 2026/27». */
  title: string;
  onPrev: () => void;
  onNext: () => void;
  prevDisabled: boolean;
  nextDisabled: boolean;
  view: CalendarView;
  onView: (view: CalendarView) => void;
  disabledViews: CalendarView[];
  typeFilter: MeetType[];
  onTypeFilter: (next: MeetType[]) => void;
  counts: Record<MeetType | 'all', number>;
  /** Телефон, вид Month: месяц развёрнут целиком (иначе одна неделя). */
  monthExpanded: boolean;
  onToggleMonth: () => void;
}

/** Короткие подписи для телефона. */
const VIEW_SHORT: Record<CalendarView, string> = { year: 'Year', month: 'Month', meets: 'Meet' };
const TYPE_SHORT: Record<MeetType | 'all', string> = {
  all: 'All',
  pool: 'Pool',
  open_water: 'Open w.',
  artistic: 'Artistic',
};

function NavButton({
  label,
  disabled,
  onClick,
  children,
}: {
  label: string;
  disabled: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      aria-disabled={disabled || undefined}
      onClick={() => {
        if (!disabled) onClick();
      }}
      className={`cc-focus flex h-8 w-8 items-center justify-center rounded-[8px] border-0 bg-transparent p-0 text-[var(--t-accent-text)] ${
        disabled ? 'cursor-not-allowed opacity-[.45]' : 'cursor-pointer'
      }`}
    >
      {children}
    </button>
  );
}

function CalendarControls({
  title,
  onPrev,
  onNext,
  prevDisabled,
  nextDisabled,
  view,
  onView,
  disabledViews,
  typeFilter,
  onTypeFilter,
  counts,
  monthExpanded,
  onToggleMonth,
}: CalendarControlsProps) {
  const [typesOpen, setTypesOpen] = React.useState(false);
  const typesId = React.useId();
  const toggleType = (value: MeetType | 'all') => {
    if (value === 'all') {
      onTypeFilter([]);
      return;
    }
    const next = typeFilter.includes(value)
      ? typeFilter.filter((t) => t !== value)
      : MEET_TYPES.filter((t) => t === value || typeFilter.includes(t));
    onTypeFilter(next);
  };

  return (
    <>
    <div className="hidden flex-wrap items-center justify-between gap-x-6 gap-y-3 lg:flex">
      <div className="flex flex-wrap items-center gap-x-5 gap-y-3">
        <div className="flex items-center gap-1">
          <NavButton label="Previous" disabled={prevDisabled} onClick={onPrev}>
            <IconCaretLeft size={20} />
          </NavButton>
          <h2
            aria-live="polite"
            className="m-0 min-w-[190px] text-center text-[22px] font-extrabold leading-tight text-[var(--t-text)]"
          >
            {title}
          </h2>
          <NavButton label="Next" disabled={nextDisabled} onClick={onNext}>
            <IconCaretRight size={20} />
          </NavButton>
        </div>

        <div
          role="radiogroup"
          aria-label="Calendar view"
          className="inline-flex h-[38px] overflow-hidden rounded-[10px] border border-[var(--t-divider)]"
        >
          {VIEW_OPTIONS.map((opt, i) => {
            const selected = view === opt.value;
            const disabled = disabledViews.includes(opt.value);
            return (
              <button
                key={opt.value}
                type="button"
                role="radio"
                aria-checked={selected}
                aria-disabled={disabled || undefined}
                onClick={() => {
                  if (!disabled) onView(opt.value);
                }}
                className={`cc-focus px-4 text-[15px] font-bold ${
                  i > 0 ? 'border-l border-solid border-l-[var(--t-divider)]' : ''
                } ${
                  selected
                    ? 'bg-[var(--deep-seg-active-bg)] text-[var(--deep-seg-active-fg)]'
                    : 'bg-transparent text-[var(--t-text)]'
                } ${disabled ? 'cursor-not-allowed opacity-[.45]' : 'cursor-pointer'}`}
              >
                {opt.label}
              </button>
            );
          })}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Meet type">
        {TYPE_OPTIONS.map(({ value, label, Icon }) => {
          const selected = value === 'all' ? typeFilter.length === 0 : typeFilter.includes(value);
          return (
            <button
              key={value}
              type="button"
              aria-pressed={selected}
              onClick={() => toggleType(value)}
              className={`cc-focus flex h-[34px] cursor-pointer items-center gap-2 rounded-[10px] border px-3 text-[var(--t-text)] hover:border-[var(--t-accent)] ${
                selected
                  ? 'border-[var(--t-accent)] bg-[var(--t-accent-soft)]'
                  : 'border-[var(--t-border)] bg-[var(--t-surface)]'
              }`}
            >
              <Icon size={16} />
              <span className="text-[14px] font-semibold">{label}</span>
              <span className="text-[12px] text-[var(--t-text-2)]">{counts[value]}</span>
            </button>
          );
        })}
      </div>
    </div>

    {/* Телефон (< lg) */}
    <div className="lg:hidden">
      <div className="flex items-center gap-1">
        <NavButton label="Previous" disabled={prevDisabled} onClick={onPrev}>
          <IconCaretLeft size={20} />
        </NavButton>
        {view === 'month' ? (
          <button
            type="button"
            aria-expanded={monthExpanded}
            aria-label={`${title}, ${monthExpanded ? 'collapse' : 'expand'} month`}
            onClick={onToggleMonth}
            className="cc-focus flex cursor-pointer items-center gap-[6px] rounded-[8px] border-0 bg-transparent px-2 py-1 text-[16px] font-medium text-[var(--t-text)]"
          >
            {title}
            {monthExpanded ? (
              <IconCaretUp size={13} className="text-[var(--t-text-2)]" />
            ) : (
              <IconCaretDown size={13} className="text-[var(--t-text-2)]" />
            )}
          </button>
        ) : (
          <span aria-live="polite" className="px-2 text-[16px] font-medium text-[var(--t-text)]">
            {title}
          </span>
        )}
        <NavButton label="Next" disabled={nextDisabled} onClick={onNext}>
          <IconCaretRight size={20} />
        </NavButton>
      </div>

      <div className="mt-2 grid grid-cols-[minmax(0,1fr)_auto] gap-2">
        <div
          role="radiogroup"
          aria-label="Calendar view"
          className="grid h-[38px] grid-cols-3 overflow-hidden rounded-[10px] border border-[var(--t-divider)]"
        >
          {VIEW_OPTIONS.map((opt, i) => {
            const selected = view === opt.value;
            const disabled = disabledViews.includes(opt.value);
            return (
              <button
                key={opt.value}
                type="button"
                role="radio"
                aria-checked={selected}
                aria-disabled={disabled || undefined}
                onClick={() => {
                  if (!disabled) onView(opt.value);
                }}
                className={`cc-focus text-[14px] font-bold ${
                  i > 0 ? 'border-l border-solid border-l-[var(--t-divider)]' : ''
                } ${
                  selected
                    ? 'bg-[var(--deep-seg-active-bg)] text-[var(--deep-seg-active-fg)]'
                    : 'bg-transparent text-[var(--t-text)]'
                } ${disabled ? 'cursor-not-allowed opacity-[.45]' : 'cursor-pointer'}`}
              >
                {VIEW_SHORT[opt.value]}
              </button>
            );
          })}
        </div>

        <button
          type="button"
          aria-expanded={typesOpen}
          aria-controls={typesId}
          onClick={() => setTypesOpen((o) => !o)}
          className={`cc-focus flex h-[38px] cursor-pointer items-center gap-[6px] rounded-[10px] border bg-[var(--t-surface)] px-3 text-[13px] font-semibold text-[var(--t-text)] ${
            typeFilter.length > 0 ? 'border-[var(--t-accent)]' : 'border-[var(--t-border)]'
          }`}
        >
          <IconFunnel size={15} />
          Type
          {typeFilter.length > 0 && <span className="text-[var(--t-accent-text)]">{typeFilter.length}</span>}
        </button>
      </div>

      <div id={typesId} aria-hidden={!typesOpen} className={`cc-types ${typesOpen ? 'cc-types--open' : ''}`}>
        <div>
          <div className="grid grid-cols-4 gap-[6px] p-[3px] pt-2">
            {TYPE_OPTIONS.map(({ value, label, Icon }) => {
              const selected = value === 'all' ? typeFilter.length === 0 : typeFilter.includes(value);
              return (
                <button
                  key={value}
                  type="button"
                  tabIndex={typesOpen ? 0 : -1}
                  aria-pressed={selected}
                  aria-label={label}
                  onClick={() => toggleType(value)}
                  className={`cc-focus flex cursor-pointer flex-col items-center gap-[3px] rounded-[10px] border px-1 py-2 text-[var(--t-text)] ${
                    selected
                      ? 'border-[var(--t-accent)] bg-[var(--t-accent-soft)]'
                      : 'border-[var(--t-border)] bg-[var(--t-surface)]'
                  }`}
                >
                  <Icon size={16} />
                  <span className="text-[11px] font-semibold">{TYPE_SHORT[value]}</span>
                  <span className="text-[10px] text-[var(--t-text-2)]">{counts[value]}</span>
                </button>
              );
            })}
          </div>
        </div>
      </div>
    </div>
    </>
  );
}

export default CalendarControls;
