/**
 * RecordAgeAxis — по какой оси возраста сверять заплыв со справочником рекордов.
 * Источник правды — админ-настройка (GET /api/client-config), та же, что у сервера:
 *
 *   'calendar' (дефолт) — год заплыва минус год рождения, как ведёт ступени федерация;
 *   'season'            — возраст в сезоне, как считаем возраст у себя.
 *
 * ⚠ Зачем клиенту вообще знать: бейдж «рекорд» в строке результата считает клиент, а
 * карточку «New records» — сервер. Разъедутся оси — на одной странице появятся два разных
 * ответа на вопрос «это рекорд?» (docs/data-integrity.md §13).
 *
 * Кэш + fallback как у ResultsLoadModeHelper: недоступен конфиг — работаем по 'calendar'.
 */

export type RecordAgeAxis = 'calendar' | 'season';

interface ClientConfigDto {
  recordAgeAxis?: string;
}

let cached: RecordAgeAxis | null = null;
let loadPromise: Promise<RecordAgeAxis> | null = null;

/** Синхронное значение для рендера: пока конфиг не приехал — ось федерации. */
export function recordAgeAxisNow(): RecordAgeAxis {
  return cached ?? 'calendar';
}

export async function loadRecordAgeAxis(): Promise<RecordAgeAxis> {
  if (cached) return cached;
  if (loadPromise) return loadPromise;

  loadPromise = (async () => {
    try {
      const response = await fetch('/api/client-config');
      if (!response.ok) throw new Error(`client-config: ${response.status}`);

      const config = (await response.json()) as ClientConfigDto;
      cached = config.recordAgeAxis === 'season' ? 'season' : 'calendar';
      return cached;
    } catch (error) {
      console.error('Error loading record age axis:', error);
      return 'calendar';       // ошибку не кэшируем — следующий вызов попробует снова
    } finally {
      loadPromise = null;
    }
  })();

  return loadPromise;
}
