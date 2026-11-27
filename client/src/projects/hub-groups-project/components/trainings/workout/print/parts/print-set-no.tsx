import React from 'react';

/**
 * Номер в кружке контуром (①②… сетов, ② ранга уровня в шапке). Рисуем рамкой, а не символами
 * «①»: их нет в половине системных шрифтов, а после ⑳ нет вовсе. `className` — классы вида
 * (по умолчанию `large`: `wpl-no`).
 */
export const PrintSetNo: React.FC<{ n: number; className?: string }> = ({ n, className = 'wpl-no' }) => (
  <span className={className}>{n}</span>
);
