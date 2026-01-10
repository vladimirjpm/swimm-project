/**
 * ClubPointsHelper - управление системой начисления очков клубам
 * 
 * Поддерживает:
 * - Версионирование правил начисления очков
 * - Автоматический выбор правил по дате соревнования/тренировки
 * - Кэширование конфига для оптимизации
 */

import type { Result } from '../interfaces/results';

interface PointsRule {
  version: string;
  effectiveFrom: string; // ISO date "YYYY-MM-DD"
  description?: string;
  defaultPoints: number;
  maxScoringPlace?: number;
  pointsByPlace: Record<string, number>;
}

interface PointsConfig {
  rules: PointsRule[];
}

export default class ClubPointsHelper {
  private static cachedConfig: PointsConfig | null = null;
  private static configPromise: Promise<PointsConfig> | null = null;

  /**
   * Загружает конфиг с правилами начисления очков (с кэшированием)
   */
  private static async loadConfig(): Promise<PointsConfig> {
    if (this.cachedConfig) {
      return this.cachedConfig;
    }

    // Если загрузка уже идёт, возвращаем тот же промис
    if (this.configPromise) {
      return this.configPromise;
    }

    this.configPromise = (async () => {
      try {
        const baseUrl = import.meta.env.BASE_URL || '/';
        const url = `${baseUrl}data/config/club-points-config.json`;
        const response = await fetch(url);
        
        if (!response.ok) {
          throw new Error(`Failed to load club points config: ${response.status}`);
        }
        
        const config = await response.json() as PointsConfig;
        
        // Валидация
        if (!config.rules || !Array.isArray(config.rules) || config.rules.length === 0) {
          throw new Error('Invalid club points config: missing or empty rules array');
        }
        
        this.cachedConfig = config;
        return config;
      } catch (error) {
        console.error('Error loading club points config:', error);
        // Возвращаем fallback конфиг
        return this.getFallbackConfig();
      } finally {
        this.configPromise = null;
      }
    })();

    return this.configPromise;
  }

  /**
   * Резервный конфиг на случай ошибки загрузки
   */
  private static getFallbackConfig(): PointsConfig {
    return {
      rules: [
        {
          version: 'fallback',
          effectiveFrom: '2000-01-01',
          description: 'Fallback points system',
          defaultPoints: 0,
          maxScoringPlace: 24,
          pointsByPlace: {
            '1': 30, '2': 28, '3': 26, '4': 24, '5': 23,
            '6': 22, '7': 21, '8': 20, '9': 19, '10': 18,
            '11': 16, '12': 15, '13': 14, '14': 13, '15': 12,
            '16': 11, '17': 10, '18': 9, '19': 8, '20': 7,
            '21': 5, '22': 3, '23': 2, '24': 1
          }
        }
      ]
    };
  }

  /**
   * Парсит дату формата "dd/MM/yyyy" или "dd.MM.yyyy" → Date
   */
  private static parseDate(dateStr: string): Date {
    if (!dateStr || typeof dateStr !== 'string') {
      return new Date(); // fallback to current date
    }

    // Пробуем разные разделители
    const separators = ['/', '.', '-'];
    for (const sep of separators) {
      if (dateStr.includes(sep)) {
        const parts = dateStr.split(sep).map(p => parseInt(p.trim(), 10));
        
        if (parts.length === 3) {
          const [first, second, third] = parts;
          
          // dd/MM/yyyy or dd.MM.yyyy
          if (first <= 31 && second <= 12 && third > 1900) {
            return new Date(third, second - 1, first);
          }
          
          // yyyy-MM-dd (ISO format)
          if (first > 1900 && second <= 12 && third <= 31) {
            return new Date(first, second - 1, third);
          }
        }
      }
    }

    // Fallback: try native Date parsing
    const parsed = new Date(dateStr);
    return isNaN(parsed.getTime()) ? new Date() : parsed;
  }

  /**
   * Выбирает правила, актуальные на указанную дату
   */
  private static selectRule(config: PointsConfig, eventDate: Date): PointsRule {
    // Сортируем правила по дате (от новых к старым)
    const sorted = [...config.rules].sort((a, b) => {
      const dateA = new Date(a.effectiveFrom).getTime();
      const dateB = new Date(b.effectiveFrom).getTime();
      return dateB - dateA;
    });

    // Находим первое правило, которое действует на дату события
    const rule = sorted.find(r => {
      const ruleDate = new Date(r.effectiveFrom);
      return ruleDate <= eventDate;
    });

    if (!rule) {
      console.warn(
        `No matching points rule found for date ${eventDate.toISOString()}, using oldest rule`
      );
      return sorted[sorted.length - 1];
    }

    return rule;
  }

  /**
   * Вычисляет очки для клуба по месту пловца
   * 
   * @param position - место пловца (1, 2, 3, ...)
   * @param eventDate - дата заплыва в формате "dd/MM/yyyy" или "dd.MM.yyyy"
   * @returns количество очков для клуба
   */
  static async getPoints(
    position: number | null | undefined,
    eventDate: string
  ): Promise<number> {
    // Если место не указано или не валидно
    if (!position || position <= 0) {
      return 0;
    }

    try {
      const config = await this.loadConfig();
      const parsedDate = this.parseDate(eventDate);
      const rule = this.selectRule(config, parsedDate);

      const points = rule.pointsByPlace[position.toString()];
      
      if (points !== undefined) {
        return points;
      }

      // Место не найдено в таблице - возвращаем defaultPoints
      return rule.defaultPoints;
    } catch (error) {
      console.error('Error calculating club points:', error);
      return 0;
    }
  }

  /**
   * Вычисляет очки для результата (удобная обёртка)
   * 
   * @param result - объект результата с полями position и date
   * @returns количество очков для клуба
   */
  static async getPointsForResult(result: Result): Promise<number> {
    const isRelay =
      (result as any)?.is_relay === true ||
      String((result as any)?.is_relay ?? '').toLowerCase() === 'true' ||
      String((result as any)?.is_relay ?? '') === '1';

    const basePoints = await this.getPoints(result.position, result.date);
    return isRelay ? basePoints * 2 : basePoints;
  }

  /**
   * Вычисляет очки для массива результатов
   * 
   * @param results - массив результатов
   * @returns массив объектов с результатом и начисленными очками
   */
  static async getPointsForResults(
    results: Result[]
  ): Promise<Array<{ result: Result; points: number }>> {
    const pointsPromises = results.map(async (result) => ({
      result,
      points: await this.getPointsForResult(result)
    }));

    return Promise.all(pointsPromises);
  }

  /**
   * Сброс кэша (для тестирования или перезагрузки конфига)
   */
  static clearCache(): void {
    this.cachedConfig = null;
    this.configPromise = null;
  }
}
