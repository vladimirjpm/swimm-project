/**
 * Короткие подписи плашек редактора дорожек (решение Влада 09.11.2026): только имя, а у тёзок —
 * имя и первая буква фамилии («רן כ.»). Если и буква совпала — фамилия целиком. Нет раздельных
 * имени и фамилии (пловец ушёл из состава, а в плане остался) — полное имя как есть.
 *
 * Тёзки ищутся по всему составу, а не по одной дорожке: подпись пловца не должна меняться от того,
 * куда его перетащили.
 */
export interface ShortNameInput {
  id: number;
  first: string;
  last: string;
  /** Полное имя — когда раздельных нет. */
  full: string;
}

const norm = (s: string) => s.trim().toLocaleLowerCase();

export function laneShortNames(people: ShortNameInput[]): Map<number, string> {
  const result = new Map<number, string>();
  const named = people.filter((p) => p.first.trim() !== '');
  people.filter((p) => p.first.trim() === '').forEach((p) => result.set(p.id, p.full.trim()));

  const byFirst = new Map<string, ShortNameInput[]>();
  named.forEach((p) => {
    const key = norm(p.first);
    byFirst.set(key, [...(byFirst.get(key) ?? []), p]);
  });

  byFirst.forEach((group) => {
    if (group.length === 1) {
      result.set(group[0].id, group[0].first.trim());
      return;
    }
    const initial = (p: ShortNameInput) => p.last.trim().charAt(0);
    group.forEach((p) => {
      const last = p.last.trim();
      if (last === '') { result.set(p.id, p.first.trim()); return; }
      const sameInitial = group.filter((o) => o !== p && norm(initial(o)) === norm(initial(p))).length > 0;
      result.set(p.id, sameInitial ? `${p.first.trim()} ${last}` : `${p.first.trim()} ${initial(p)}.`);
    });
  });
  return result;
}
