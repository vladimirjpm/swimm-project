/**
 * Возрастные секции карточек времён клуба — общая раскладка для «Season best» и
 * «Record wall» (решение Влада: обе карточки режутся по возрасту, а не по дисциплине).
 *
 * Ступени приходят из двух разных источников с разными наборами ключей:
 *  • Season best — `age_key` из AgeBucket: «8».. «18» / «adults» / «45-49» / «n/a»;
 *  • Record wall — тот же вид ключа плюс `category='open'` (рекорд без возраста).
 * Поэтому порядок и подпись считаются здесь, а не в каждой карточке отдельно.
 *
 * Порядок: сначала абсолютные (open), дальше от старших к младшим, «n/a» всегда последним —
 * иначе ступень «возраст неизвестен» вылезает в начало при обычной сортировке по убыванию.
 */

/** Числовой ранг ступени: чем больше, тем выше в списке. */
export function ageRank(ageKey: string, category?: string): number {
  if (category === 'open' || !ageKey) return 1000;   // абсолютный рекорд — над возрастами
  if (ageKey === 'n/a') return -1;                   // возраст неизвестен — в самый конец
  if (ageKey === 'adults') return 19;                // 19-24 одной ступенью
  const m = ageKey.match(/^(\d+)/);                  // «45-49» → 45, «16» → 16
  return m ? Number(m[1]) : 0;
}

/** Подпись секции: «Open», «Masters 45-49», «Adults», «Age 16», «Age unknown». */
export function ageSectionLabel(ageKey: string, category?: string): string {
  if (category === 'open' || !ageKey) return 'Open';
  if (ageKey === 'n/a') return 'Age unknown';
  if (ageKey === 'adults') return 'Adults';
  if (ageKey.includes('-')) return `Masters ${ageKey}`;
  return `Age ${ageKey}`;
}

/** Ключ группировки: у open возраста нет, иначе он бы слился с «n/a». */
export function ageSectionKey(ageKey: string, category?: string): string {
  return category === 'open' || !ageKey ? 'open' : ageKey;
}

/**
 * Порядок плиток внутри секции: дисциплина, затем дистанция, затем бассейн.
 * Дистанция — строка («100m», «4X50m»), поэтому сравнивается по числу, а не лексически:
 * иначе 100 встаёт перед 50.
 */
export function disciplineSortKey(style: string, distance: string, poolType: string | null) {
  const relay = /^\d+X/i.test(distance) ? 1 : 0;     // эстафеты после личных
  const meters = Number(distance.replace(/^\d+X/i, '').replace(/\D/g, '')) || 0;
  return { relay, style, meters, pool: poolType ?? '' };
}

export function compareDiscipline(
  a: { style: string; distance: string; poolType: string | null },
  b: { style: string; distance: string; poolType: string | null },
): number {
  const x = disciplineSortKey(a.style, a.distance, a.poolType);
  const y = disciplineSortKey(b.style, b.distance, b.poolType);
  return (
    x.relay - y.relay
    || x.style.localeCompare(y.style)
    || x.meters - y.meters
    || x.pool.localeCompare(y.pool)
  );
}

/** Группировка списка по ступени + сортировка секций (open → старшие → младшие → n/a). */
export function groupByAge<T>(
  items: T[],
  ageKeyOf: (item: T) => { ageKey: string; category?: string },
): { key: string; label: string; items: T[] }[] {
  const map = new Map<string, { key: string; label: string; rank: number; items: T[] }>();
  for (const item of items) {
    const { ageKey, category } = ageKeyOf(item);
    const key = ageSectionKey(ageKey, category);
    let section = map.get(key);
    if (!section) {
      section = {
        key,
        label: ageSectionLabel(ageKey, category),
        rank: ageRank(ageKey, category),
        items: [],
      };
      map.set(key, section);
    }
    section.items.push(item);
  }
  return [...map.values()]
    .sort((a, b) => b.rank - a.rank)
    .map(({ key, label, items: sectionItems }) => ({ key, label, items: sectionItems }));
}
