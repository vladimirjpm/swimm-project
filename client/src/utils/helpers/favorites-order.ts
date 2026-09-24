/**
 * Порядок «своих» пловцов на экранах: Me → семья → остальное избранное
 * (docs/plans/family-favorites-plan.md, решение Влада 24.09.2026).
 *
 * Одно место на правило: экраны, где выводится список своих (стартовый протокол, полоса «мои»
 * в шапке соревнования, карточка Favorites, H2H, My media), сортируют через него, а не своим
 * sort. Семья прав не даёт — только поднимает пловца в списке и красит сердечко в золото.
 */

/** 0 — Me (primary), 1 — семья, 2 — остальные. */
export function favoriteRank(
  swimmerId: number | null | undefined,
  primarySwimmerId: number | null,
  familySwimmerIds: ReadonlySet<number>,
): number {
  if (swimmerId == null) return 2;
  if (swimmerId === primarySwimmerId) return 0;
  return familySwimmerIds.has(swimmerId) ? 1 : 2;
}

/**
 * Стабильная сортировка по рангу: внутри ранга сохраняется исходный порядок (как пришёл с
 * сервера или как его построил экран). Исходный массив не меняется.
 */
export function sortByFavoriteRank<T>(
  items: readonly T[],
  swimmerIdOf: (item: T) => number | null | undefined,
  primarySwimmerId: number | null,
  familySwimmerIds: ReadonlySet<number>,
): T[] {
  return items
    .map((item, index) => ({ item, index, rank: favoriteRank(swimmerIdOf(item), primarySwimmerId, familySwimmerIds) }))
    .sort((a, b) => a.rank - b.rank || a.index - b.index)
    .map((x) => x.item);
}
