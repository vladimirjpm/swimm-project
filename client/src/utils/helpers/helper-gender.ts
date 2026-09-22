/**
 * Хелпер для работы с полом
 */
export type Gender = 'male' | 'female' | 'none';

/**
 * «men» / «women» / «mixed» — одна подпись пола дисциплины на весь сайт. Раньше было
 * «female ? Women : Men» по месту, и смешанная эстафета печаталась мужской (Э2/Э5,
 * docs/plans/records-relays-plan.md). Неизвестный пол — «men»: так было и до шва.
 */
export function genderLabel(g: string | null | undefined, capital = false): string {
  const w = g === 'female' ? 'women' : g === 'mixed' ? 'mixed' : 'men';
  return capital ? w[0].toUpperCase() + w.slice(1) : w;
}

export default class HelperGender {
  /**
   * Нормализует значение пола к типу Gender
   * @param gender - значение пола из данных
   * @returns 'male' | 'female' | 'none'
   */
  static resolveGender(gender: unknown): Gender {
    const value = gender === null || gender === undefined
      ? ''
      : String(gender).trim().toLowerCase();

    if (value === 'female' || value === 'f' || value === 'w') return 'female';
    if (value === 'male' || value === 'm') return 'male';
    return 'none';
  }

  /**
   * Возвращает CSS класс фона в зависимости от пола
   * @param gender - пол: 'male' | 'female' | 'none'
   * @returns Tailwind CSS класс фона
   */
  /**
   * Класс мягкой заливки строки по полу. Нет пола → без заливки (пусто).
   * Значения токенов — в теме (light/dark).
   */
  static getGenderBgClass(gender: string): string {
    if (gender === 'female') return 'bg-[var(--theme-mode-row-female)]';
    if (gender === 'male') return 'bg-[var(--theme-mode-row-male)]';
    return '';
  }

  /**
   * Определяет пол по НАЗВАНИЮ события (в данных нет отдельного поля gender).
   * Приоритет: если backend прислал явный gender/sex — берём его; иначе парсим
   * ключевые слова из названия. Иврит: בנות/נערות/נשים/בוגרות = W;
   * בנים/נערים/גברים/בוגרים = M. Англ.: girls/women = W, boys/men = M.
   */
  static resolveGenderFromEvent(eventText: unknown, explicitGender?: unknown): Gender {
    const explicit = HelperGender.resolveGender(explicitGender);
    if (explicit !== 'none') return explicit;

    const s = String(eventText ?? '').toLowerCase();
    if (!s) return 'none';

    const womenKeys = ['בנות', 'נערות', 'נשים', 'בוגרות', 'girls', 'women', 'female'];
    const menKeys = ['בנים', 'נערים', 'גברים', 'בוגרים', 'boys', 'men', 'male'];
    if (womenKeys.some((k) => s.includes(k))) return 'female';
    if (menKeys.some((k) => s.includes(k))) return 'male';
    return 'none';
  }
}
