import React from 'react';
import type { SheetRichText } from '../print-types';
import { PrintChip } from './print-intensity';
import { PrintText } from './print-text';

/**
 * Текст строки с уровнями нагрузки чипами (хендофф 27.11 §5): «חתירה 1-3V» → «חתירה 1-[V3]».
 * Куски уже разобраны моделью (`SheetLine.rich`, `richText`). Приставка «1-» и чип — одна
 * неразрывная пара; чип изолирован LTR. Направление всей строки — `rich.dir` (без букв — листа).
 * Чипы поля `intensity` модель оставляет, только если уровня в тексте нет; их рисует вызывающий.
 */
export const PrintRichText: React.FC<{ rich: SheetRichText; chipClassName: string; nowrapClassName: string }> = ({
  rich, chipClassName, nowrapClassName,
}) => (
  <bdi dir={rich.dir}>
    {rich.parts.map((p, i) =>
      p.kind === 'text' ? (
        <PrintText key={i} text={p.text} />
      ) : p.prefix ? (
        <span key={i} className={nowrapClassName}>
          {p.prefix}
          <PrintChip label={p.label} className={chipClassName} />
        </span>
      ) : (
        <PrintChip key={i} label={p.label} className={chipClassName} />
      ),
    )}
  </bdi>
);
