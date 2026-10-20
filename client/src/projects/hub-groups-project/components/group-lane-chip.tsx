import React from 'react';
import LaneCircle from './group-lane-circle';

/**
 * Плашка пловца в редакторе дорожек (Т7, хендофф Trainings Tab: 1e / 6b).
 * - телефон: пилюля 36px — кружок уровня + имя; перенос — ТАПОМ по плашке (строка «Move … to»
 *   рисуется в карточке дорожки), `draggable` на телефоне не ставим — он мешает прокрутке;
 * - десктоп: строка 36px во всю колонку — ручка «⠿», кружок, имя, «⋮» (выбрать для переноса
 *   кнопками); плашку можно тащить мышью, бросок на другую плашку вставляет ПЕРЕД ней.
 * Выбранная плашка — рамка `--deep-ow`, фон `--deep-ow-chip`.
 */
function LaneChip({
  name, nameEn, color, subLevel = null, leftGroup, selected, desktop, onSelect, dragProps, dropActive,
}: {
  name: string;
  nameEn: string;
  /** Цвет ТЕКУЩЕГО уровня пловца; null — без уровня. */
  color: string | null;
  /** ТЕКУЩИЙ подуровень (1 — верхний): мелкая цифра после имени — видно, кто перешёл с уровня выше. */
  subLevel?: number | null;
  /** Ушёл из группы (план — снимок): имя приглушено. */
  leftGroup: boolean;
  selected: boolean;
  desktop: boolean;
  onSelect: () => void;
  /** Только десктоп: `draggable` + обработчики drag/drop. */
  dragProps?: React.HTMLAttributes<HTMLElement>;
  /** Над плашкой держат другую — вставим перед ней. */
  dropActive?: boolean;
}) {
  const label = name || nameEn;
  const border = selected ? 'var(--deep-ow)' : 'var(--deep-card-border)';
  const bg = selected ? 'var(--deep-ow-chip)' : 'var(--deep-card-bg)';
  const nameColor = leftGroup ? 'var(--t-text-3)' : 'var(--deep-text)';
  const outline = dropActive ? '2px solid var(--deep-accent)' : undefined;
  const sub = subLevel != null && (
    <span className="hp-mono flex-none text-[10.5px] font-extrabold" style={{ color: 'var(--t-text-3)' }} title={`Sub-level ${subLevel}`}>
      {subLevel}
    </span>
  );

  if (!desktop) {
    return (
      <button
        type="button"
        onClick={onSelect}
        aria-pressed={selected}
        aria-label={`Move ${label}`}
        className="inline-flex h-[36px] max-w-full cursor-pointer items-center gap-1.5 rounded-full border text-left"
        style={{ padding: '0 10px 0 4px', borderColor: border, background: bg, outline }}
      >
        <LaneCircle name={label} color={color} />
        <span dir="auto" className="min-w-0 truncate text-[12px] font-extrabold" style={{ color: nameColor }}>
          <bdi>{label}</bdi>
        </span>
        {sub}
      </button>
    );
  }

  return (
    <div
      {...dragProps}
      className="flex h-[36px] min-w-0 cursor-grab items-center gap-1.5 rounded-[10px] border pl-2 active:cursor-grabbing"
      style={{ borderColor: border, background: bg, outline }}
    >
      <span aria-hidden="true" className="flex-none text-[11px]" style={{ color: 'var(--t-text-3)' }}>⠿</span>
      <LaneCircle name={label} color={color} />
      <span dir="auto" className="min-w-0 flex-1 truncate text-[12px] font-extrabold" style={{ color: nameColor }}>
        <bdi>{label}</bdi>
      </span>
      {sub}
      <button
        type="button"
        onClick={onSelect}
        aria-pressed={selected}
        aria-label={`Move ${label}`}
        className="h-[32px] w-[24px] flex-none cursor-pointer border-0 bg-transparent p-0 text-[14px] font-black"
        style={{ color: selected ? 'var(--deep-ow)' : 'var(--t-text-3)' }}
      >
        ⋮
      </button>
    </div>
  );
}

export default LaneChip;
