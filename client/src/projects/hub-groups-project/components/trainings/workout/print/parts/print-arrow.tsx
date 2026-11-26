import React from 'react';

/**
 * Стрелка листа печати (docs/plans/workout-print-plan.md §3.4): SVG, чёрная, без символов шрифта
 * («◀▶▼» в системных шрифтах разные и не везде есть). Размеры в `em` — растёт с основным шрифтом.
 * Масштаб рисунка: 1em = 20 единиц, толщина линии 0.12em = 2.4.
 *
 * - `left` / `right` — 1.6em × 1em: ровно промежуток между кубами (`ARROW_GAP_EM`);
 * - `down` — 1em × 1.6em: ряд-переход «змейки»;
 * - `stretch` — горизонтальная во всю ширину родителя (указатель «➜ ④» раскладки «главный справа»).
 */
export type PrintArrowDir = 'left' | 'right' | 'down';

const STROKE = 2.4;
const ink = { stroke: '#000', strokeWidth: STROKE, fill: 'none' } as const;

/** Наконечник вправо в коробке 10×20 (для растянутой стрелки). */
const Head: React.FC<{ flip: boolean }> = ({ flip }) => (
  <svg viewBox="0 0 10 20" width="0.5em" height="1em" aria-hidden className="block flex-none">
    <polygon points={flip ? '10,4 0.5,10 10,16' : '0,4 9.5,10 0,16'} fill="#000" />
  </svg>
);

export const PrintArrow: React.FC<{ dir: PrintArrowDir; stretch?: boolean; className?: string }> = ({ dir, stretch = false, className }) => {
  if (stretch && dir !== 'down') {
    const flip = dir === 'left';
    return (
      <span className={`flex items-center ${className ?? ''}`} aria-hidden>
        {flip && <Head flip />}
        {/* Растягивается только по ширине: у горизонтальной линии толщина от этого не меняется. */}
        <svg viewBox="0 0 100 20" preserveAspectRatio="none" height="1em" className="block min-w-0 flex-1" style={{ width: 0 }}>
          <line x1="0" y1="10" x2="100" y2="10" {...ink} />
        </svg>
        {!flip && <Head flip={false} />}
      </span>
    );
  }
  if (dir === 'down') {
    return (
      <svg viewBox="0 0 20 32" width="1em" height="1.6em" aria-hidden className={`block ${className ?? ''}`}>
        <line x1="10" y1="1" x2="10" y2="23" {...ink} />
        <polygon points="4,22 10,31 16,22" fill="#000" />
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 32 20" width="1.6em" height="1em" aria-hidden className={`block ${className ?? ''}`}>
      <g transform={dir === 'left' ? 'translate(32 0) scale(-1 1)' : undefined}>
        <line x1="2" y1="10" x2="23" y2="10" {...ink} />
        <polygon points="22,4 31,10 22,16" fill="#000" />
      </g>
    </svg>
  );
};
