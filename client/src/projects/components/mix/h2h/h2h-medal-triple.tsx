import React from 'react';
import './h2h.css';
import type { H2HMedals } from './h2h.types';

/**
 * Три медальных кружка с числами (макет 1b, §2). Нули НЕ прячем: пустая полка — тоже факт,
 * то же решение, что в KPI-плитке шапки страницы.
 *
 * Подсветки «кто лучше» у медалей нет сознательно (правило хендоффа): cyan означает
 * быстрее/лидер, а медальный набор — это три числа сразу, и лидера по ним не выбрать.
 */
const UI_H2HMedalTriple: React.FC<{ medals: H2HMedals }> = ({ medals }) => (
  <span className="h2h-medals">
    <span className="h2h-medal h2h-medal--gold" title="gold">{medals.gold}</span>
    <span className="h2h-medal h2h-medal--silver" title="silver">{medals.silver}</span>
    <span className="h2h-medal h2h-medal--bronze" title="bronze">{medals.bronze}</span>
  </span>
);

export default UI_H2HMedalTriple;
