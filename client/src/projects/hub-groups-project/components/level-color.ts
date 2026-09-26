import type { HubGroupLevel } from '../types';

/**
 * Цвет уровня по рангу, когда тренер свой не выбрал (docs/plans/lane-plans-plan.md).
 * Один источник для карточки Levels и будущей доски дорожек — чтобы «Advanced» был одного
 * цвета везде. Точки и полоски на обеих темах, поэтому насыщенные средние тона.
 */
const RANK_COLORS = [
  '#e53935', '#fb8c00', '#fdd835', '#43a047', '#1e88e5', '#8e24aa',
  '#00acc1', '#6d4c41', '#d81b60', '#7cb342', '#3949ab', '#546e7a',
];

export function rankColor(rank: number): string {
  return RANK_COLORS[(Math.max(rank, 1) - 1) % RANK_COLORS.length];
}

export function levelColor(level: Pick<HubGroupLevel, 'rank' | 'color'>): string {
  return level.color || rankColor(level.rank);
}
