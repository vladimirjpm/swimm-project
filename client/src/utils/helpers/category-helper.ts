/**
 * CategoryHelper - живые данные категорий соревнований (name/badge) с сервера
 *
 * Источник правды — /api/categories (таблица Categories в БД, см. Admin/Categories).
 * Ключи категорий соревнований (young8_11/junior/masters/all) остаются client-only
 * и заданы в results-categories.ts — CategoryHelper только подтягивает актуальные
 * name/badge для отображения, не подменяет сам URL-контракт категорий.
 */

export interface CategoryApiDto {
  key: string;
  name: string;
  badge: string | null;
  display_order: number;
}

/** name + badge для отображения (badge может отсутствовать). */
export interface CategoryDisplay {
  name: string;
  badge: string | null;
}

export default class CategoryHelper {
  private static cachedCategories: CategoryApiDto[] | null = null;
  private static loadPromise: Promise<CategoryApiDto[]> | null = null;

  /**
   * Канонический ключ клиента (results-categories.ts) → Category.Key в БД. 'all' — синтетика,
   * в БД её нет.
   *
   * ⚠ Имена ключей исторические и НЕ совпадают с подписями (2026-07-28 ступени переименованы):
   *   young8_11 → «Kids» (8–11),  junior → «Youth» (11–14),  results-main → «Juniors» (בוגרים).
   * Ключи оставлены как есть: они в URL (?category=) и в закладках пользователей.
   */
  private static readonly CANONICAL_TO_DB_KEY: Record<string, string> = {
    young8_11: 'results-youth-team',
    junior: 'results-junior-results',
    adults: 'results-main',
    masters: 'results-masters',
  };

  /** Системные ключи БД, уже покрытые статичными табами (results-main — таб 'adults').
   *  Всё, чего здесь нет, — кастомная категория (result-maccabiah и т.п.): отдельный таб. */
  static readonly SYSTEM_DB_KEYS: ReadonlySet<string> = new Set([
    'results-main',
    'results-masters',
    'results-youth-team',
    'results-junior-results',
  ]);

  private static async loadCategories(): Promise<CategoryApiDto[]> {
    if (this.cachedCategories) {
      return this.cachedCategories;
    }

    if (this.loadPromise) {
      return this.loadPromise;
    }

    this.loadPromise = (async () => {
      try {
        const response = await fetch('/api/categories');

        if (!response.ok) {
          throw new Error(`Failed to load categories: ${response.status}`);
        }

        const categories = (await response.json()) as CategoryApiDto[];

        if (!Array.isArray(categories) || categories.length === 0) {
          throw new Error('Invalid categories response: empty array');
        }

        this.cachedCategories = categories;
        return categories;
      } catch (error) {
        console.error('Error loading categories:', error);
        return this.getFallbackCategories();
      } finally {
        this.loadPromise = null;
      }
    })();

    return this.loadPromise;
  }

  /** Резервные категории на случай ошибки загрузки (совпадают с сидом БД). */
  private static getFallbackCategories(): CategoryApiDto[] {
    return [
      { key: 'results-main', name: 'Juniors', badge: 'J', display_order: 1 },
      { key: 'results-masters', name: 'Masters', badge: 'M', display_order: 2 },
      { key: 'results-youth-team', name: 'Kids', badge: 'K', display_order: 3 },
      { key: 'results-junior-results', name: 'Youth', badge: 'Y', display_order: 4 },
    ];
  }

  /**
   * name/badge для канонического ключа клиента ('young8_11' | 'junior' | 'masters').
   * null — категории нет в БД (напр. синтетический 'all').
   */
  static async getByCanonicalKey(canonicalKey: string): Promise<CategoryDisplay | null> {
    const dbKey = this.CANONICAL_TO_DB_KEY[canonicalKey];
    if (!dbKey) return null;

    const categories = await this.loadCategories();
    const cat = categories.find((c) => c.key === dbKey);
    return cat ? { name: cat.name, badge: cat.badge } : null;
  }

  /** Все канонические ключи разом (для предзагрузки списка табов одним запросом). */
  static async getCanonicalMap(): Promise<Record<string, CategoryDisplay>> {
    const categories = await this.loadCategories();
    const map: Record<string, CategoryDisplay> = {};

    for (const [canonicalKey, dbKey] of Object.entries(this.CANONICAL_TO_DB_KEY)) {
      const cat = categories.find((c) => c.key === dbKey);
      if (cat) map[canonicalKey] = { name: cat.name, badge: cat.badge };
    }

    return map;
  }

  /** Полный список категорий из БД (включая кастомные) — для динамических табов селектора. */
  static async getAll(): Promise<CategoryApiDto[]> {
    return this.loadCategories();
  }

  /** Сброс кэша (для тестирования или перезагрузки конфига). */
  static clearCache(): void {
    this.cachedCategories = null;
    this.loadPromise = null;
  }
}
