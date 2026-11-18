// Правила плитки соревнования: «есть ли что показывать» и «что рисовать» (переключатель). Отдельный модуль без исполняемых
// импортов — чтобы правило проверялось юнит-тестом (`competition-tile-content.test.mjs`);
// наружу оно отдаётся из `competition-source.ts`, рядом с `competitionTileData`.

import type { CompetitionTileData } from './competition-source';

/**
 * Плитка содержательна, если в ней есть буква категории ИЛИ возрастная группа. Плитка с одним
 * значком сезона и/или кубком почти пустая (решение Влада 13.11.2026: «не надо, если там почти
 * пусто и нет хотя бы M или K 8-11… вместо него поставь иконку зимы или лета»). Так бывает у
 * соревнования без канонической категории — например с одной кастомной («Age 8-99»).
 *
 * Применяет правило сам `CompetitionTile`: несодержательную плитку он рисует значком сезона в
 * своём габарите. Вызывающим проверять ничего не нужно.
 */
export function hasTileContent(tile: Pick<CompetitionTileData, 'letter' | 'ageGroup'>): boolean {
  return Boolean(tile.letter) || Boolean(tile.ageGroup);
}

/**
 * Что рисует плитка соревнования (`CompetitionTile` — переключатель, решение Влада 18.11.2026):
 * - `emblem` — у серии турнира есть значок: он на месте плитки, а сезон, кубок и буква категории —
 *   уменьшенной полосой сверху (возрастная лента, если есть, — снизу, как у плитки);
 * - `tile` — обычная плитка: полоса значков, буква, возрастная лента;
 * - `mark` — несодержательная плитка: значок сезона (и кубок) в её габарите;
 * - `none` — показать нечего (нет ни сезона, ни кубка).
 */
export type CompetitionTileKind = 'emblem' | 'tile' | 'mark' | 'none';

export function competitionTileKind(tile: CompetitionTileData): CompetitionTileKind {
  if (tile.emblemUrl) return 'emblem';
  if (hasTileContent(tile)) return 'tile';
  return tile.season || tile.isChampionship ? 'mark' : 'none';
}
