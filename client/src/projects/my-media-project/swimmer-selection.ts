/**
 * Выбор пловцов в фильтре «Swimmers» страницы My media — мультиселект (решение Влада 18.11.2026):
 * можно отметить несколько пловцов, пустой выбор — «All». При загрузке отмечены «Me» и семья
 * (золотые сердечки, family-favorites-plan.md), если у них есть заплывы. Чистые функции — с тестом
 * `swimmer-selection.test.mjs`.
 */

/** Начальный выбор: «Me» и семья из тех, у кого есть заплывы, в порядке списка; никого — «All» ([]). */
export function defaultSwimmerSelection(
  presentIds: readonly number[], primaryId: number | null, familyIds: ReadonlySet<number>,
): number[] {
  return presentIds.filter((id) => id === primaryId || familyIds.has(id));
}

/**
 * Выбор, который действует сейчас: отмеченные из тех, у кого есть заплывы в сезоне (сезон сменили —
 * отмеченного там может не быть). Никого не осталось — «All» ([]), а не пустой экран.
 */
export function effectiveSwimmerSelection(selected: readonly number[], presentIds: readonly number[]): number[] {
  const present = new Set(presentIds);
  return selected.filter((id) => present.has(id));
}

/** Отметить / снять пловца; порядок — как в списке (`presentIds`). */
export function toggleSwimmer(selected: readonly number[], id: number, presentIds: readonly number[]): number[] {
  const next = new Set(selected);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  const ordered = presentIds.filter((x) => next.has(x));
  // Отмеченные, которых нет в этом сезоне, не теряем — вернутся, когда сменят сезон.
  return [...ordered, ...[...next].filter((x) => !presentIds.includes(x))];
}

/** Подпись выбора: «All», «Dana», «Dana, Noa», «Dana + 2». */
export function swimmerSelectionLabel(names: readonly string[]): string {
  if (names.length === 0) return 'All';
  if (names.length <= 2) return names.join(', ');
  return `${names[0]} + ${names.length - 1}`;
}
