/**
 * Возрастная лента ступени лестницы, общая для фильтра и грида Season × Group.
 * Ключи Category.Key ротировались (см. utils/constants/results-categories.ts),
 * поэтому опираемся на имя ступени, а не на key.
 * Ступени без внятной границы (Juniors/Adults/Masters) ленты не получают.
 */
const AGE_RANGE_BY_GROUP_NAME: Record<string, string> = {
  kids: '8–11',
  young: '11–14',
};

export function groupAgeRange(groupName: string): string | null {
  return AGE_RANGE_BY_GROUP_NAME[groupName.trim().toLowerCase()] ?? null;
}
