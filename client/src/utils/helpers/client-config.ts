/**
 * Публичная конфигурация клиента — `GET /api/client-config` (Program.cs). Не секреты: режимы
 * и лимиты из /Admin/Settings, которые клиенту нужно знать ДО запроса к серверу.
 *
 * Один запрос на страницу: промис кэшируется, сбой — нет (следующий вызов попробует снова,
 * вызывающий работает на своих дефолтах).
 *
 * ⚠ `results-load-mode.ts`, `record-age-axis.ts` и `useGroupCreationPolicy` пока ходят за
 * конфигом сами — они появились раньше этого модуля. Новые читатели — только через него.
 */

export type FavoriteTarget = 'swimmer' | 'club';

/** Лимит избранного одного типа (`FavoritesRules` на сервере). */
export interface FavoritesLimit {
  max: number;
  /** Подсказка у погашенного сердечка — тот же текст, что в отказе 422. */
  fullHint: string;
}

export interface ClientConfig {
  resultsLoadMode?: string;
  recordAgeAxis?: string;
  hubGroupCreationPolicy?: string;
  favoritesLimits?: Partial<Record<FavoriteTarget, FavoritesLimit>>;
  debug?: { ageRecordsDetails?: boolean };
}

let cached: ClientConfig | null = null;
let pending: Promise<ClientConfig | null> | null = null;

export function loadClientConfig(): Promise<ClientConfig | null> {
  if (cached) return Promise.resolve(cached);
  if (pending) return pending;

  pending = (async () => {
    try {
      const response = await fetch('/api/client-config');
      if (!response.ok) throw new Error(`client-config: ${response.status}`);
      cached = (await response.json()) as ClientConfig;
      return cached;
    } catch (error) {
      console.error('Error loading client-config:', error);
      return null;
    } finally {
      pending = null;
    }
  })();

  return pending;
}
