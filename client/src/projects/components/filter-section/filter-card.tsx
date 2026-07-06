import React, { useState } from 'react';

interface FilterCardProps {
  title: string;
  /** Сводка выбранного значения в шапке (напр. "25m", "M", "2013–2015") */
  summary?: string;
  /** true — фильтр не в дефолте, сводка подсвечивается акцентом темы */
  isActive?: boolean;
  defaultOpen?: boolean;
  children: React.ReactNode;
}

/**
 * Сворачиваемая карточка фильтра (по прототипу filter drawer из
 * design_handoff_theme_modes): шапка = название + сводка + шеврон,
 * контент раскрывается по клику.
 */
const FilterCard: React.FC<FilterCardProps> = ({
  title,
  summary,
  isActive = false,
  defaultOpen = false,
  children,
}) => {
  const [open, setOpen] = useState(defaultOpen);

  return (
    <div className="rounded-xl border border-[var(--theme-mode-border-drawer)] bg-[var(--theme-mode-surface)] overflow-hidden">
      <div
        className="flex items-center justify-between gap-2 px-[15px] py-[13px] cursor-pointer select-none"
        onClick={() => setOpen((o) => !o)}
      >
        <span className="text-[13px] font-extrabold text-[var(--theme-mode-text)] whitespace-nowrap">
          {title}
        </span>
        <div className="flex items-center gap-2 min-w-0">
          {summary && (
            <span
              className={`text-[13px] font-bold max-w-[150px] truncate ${
                isActive
                  ? 'text-[var(--theme-primary)]'
                  : 'text-[var(--theme-mode-text-muted)]'
              }`}
            >
              {summary}
            </span>
          )}
          <span
            className={`text-[9px] text-[var(--theme-mode-text-muted)] transition-transform duration-200 ${
              open ? 'rotate-180' : ''
            }`}
          >
            ▼
          </span>
        </div>
      </div>
      {open && <div className="px-[15px] pb-[15px]">{children}</div>}
    </div>
  );
};

export default FilterCard;
