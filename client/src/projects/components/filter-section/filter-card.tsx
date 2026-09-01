import React, { useState } from 'react';
// Карточка тащит стили сегментных кнопок сама: иначе на странице, собранной без
// `filter-section.tsx`, карточка покрасится (Tailwind в общем бандле), а `.fseg` внутри — нет.
import './filter-section.css';

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
 *
 * ПАЛИТРА (Ф1 плана `docs/plans/filters-reusable-panel-plan.md`). Цвета берутся из локальных
 * токенов `--fc-*` с фоллбеком на палитру results (`--theme-*`) — то есть на results всё
 * ровно как было, а deep-страница (`/season-best`, спортсмен, клуб) переопределяет токены
 * у себя и получает ту же карточку в своих цветах, вместо того чтобы писать свой двойник
 * (`SbCard`). Полный список: `--fc-bg`, `--fc-border`, `--fc-title`, `--fc-summary`,
 * `--fc-summary-active`, `--fc-chevron`, `--fc-radius`.
 *
 * Геометрия (отступы, кегль) намеренно НЕ токенизирована: панель фильтров должна выглядеть
 * одинаково на всех страницах, разной может быть только палитра. Исключение — `--fc-radius`:
 * у deep свой радиус плиток (`--deep-radius-tile`), и карточка с чужим скруглением рядом с
 * такими плитками читается как деталь с другого экрана.
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
    <div className="rounded-[var(--fc-radius,0.75rem)] border border-[var(--fc-border,var(--theme-mode-border-drawer))] bg-[var(--fc-bg,var(--theme-mode-surface))] overflow-hidden">
      <div
        className="flex items-center justify-between gap-2 px-[15px] py-[13px] cursor-pointer select-none"
        onClick={() => setOpen((o) => !o)}
      >
        <span className="text-[13px] font-extrabold text-[var(--fc-title,var(--theme-mode-text))] whitespace-nowrap">
          {title}
        </span>
        <div className="flex items-center gap-2 min-w-0">
          {summary && (
            <span
              className={`text-[13px] font-bold max-w-[150px] truncate ${
                isActive
                  ? 'text-[var(--fc-summary-active,var(--theme-primary))]'
                  : 'text-[var(--fc-summary,var(--theme-mode-text-muted))]'
              }`}
            >
              {summary}
            </span>
          )}
          <span
            className={`text-[9px] text-[var(--fc-chevron,var(--theme-mode-text-muted))] transition-transform duration-200 ${
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
