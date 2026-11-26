import React from 'react';

/**
 * Номер в кружке контуром (①②… сетов, ② ранга уровня в шапке). Рисуем рамкой, а не символами
 * «①»: их нет в половине системных шрифтов, а после ⑳ нет вовсе.
 */
export const PrintSetNo: React.FC<{ n: number }> = ({ n }) => <span className="wpl-no">{n}</span>;
