/**
 * ResultsLoadModeHelper — эффективный режим загрузки результатов соревнования.
 *
 * Источник правды — админ-настройка ResultsLoadMode (GET /api/client-config):
 *   'full'   — принудительно грузить всё соревнование целиком (URL-параметр игнорируется);
 *   'paged'  — принудительно постранично с серверными фильтрами (URL игнорируется);
 *   'client' — режим выбирает клиент через ?loadMode=paged|full, по умолчанию 'full'.
 *
 * Кэш + fallback по образцу CategoryHelper: при недоступном /api/client-config
 * работаем как 'client' (т.е. URL-параметр, иначе 'full') — сайт не ломается.
 */

export type ResultsLoadMode = 'full' | 'paged';

type ServerLoadMode = ResultsLoadMode | 'client';

interface ClientConfigDto {
  resultsLoadMode?: string;
}

export default class ResultsLoadModeHelper {
  private static cachedServerMode: ServerLoadMode | null = null;
  private static loadPromise: Promise<ServerLoadMode> | null = null;

  private static async loadServerMode(): Promise<ServerLoadMode> {
    if (this.cachedServerMode) return this.cachedServerMode;
    if (this.loadPromise) return this.loadPromise;

    this.loadPromise = (async () => {
      try {
        const response = await fetch('/api/client-config');
        if (!response.ok) {
          throw new Error(`Failed to load client-config: ${response.status}`);
        }
        const config = (await response.json()) as ClientConfigDto;
        const mode = config.resultsLoadMode;
        this.cachedServerMode =
          mode === 'full' || mode === 'paged' || mode === 'client' ? mode : 'client';
        return this.cachedServerMode;
      } catch (error) {
        console.error('Error loading client-config:', error);
        return 'client'; // не кэшируем ошибку — следующий вызов попробует снова
      } finally {
        this.loadPromise = null;
      }
    })();

    return this.loadPromise;
  }

  /** Значение ?loadMode= из URL; всё, кроме 'paged', трактуем как 'full'. */
  private static urlMode(): ResultsLoadMode {
    const raw = new URLSearchParams(window.location.search).get('loadMode');
    return raw === 'paged' ? 'paged' : 'full';
  }

  /**
   * Эффективный режим: принудительная админ-настройка побеждает URL;
   * 'client' отдаёт выбор URL-параметру.
   */
  static async getMode(): Promise<ResultsLoadMode> {
    const serverMode = await this.loadServerMode();
    return serverMode === 'client' ? this.urlMode() : serverMode;
  }
}
